using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AutoDirty
{
    [Generator]
    public sealed class AutoDirtyGenerator : ISourceGenerator
    {
        private const string AutoDirtyAttributeName = "AutoDirtyAttribute";
        private const string AutoDirtyPropertyAttributeName = "AutoDirtyPropertyAttribute";
        private const string AutoDirtyIgnoreAttributeName = "AutoDirtyIgnoreAttribute";
        private const string AutoDirtyPropertyNameAttributeName = "AutoDirtyPropertyNameAttribute";

        private static readonly DiagnosticDescriptor ClassMustBePartial = new DiagnosticDescriptor(
            "AD0001",
            "AutoDirty class must be partial",
            "Class '{0}' is marked with [AutoDirty] but is not partial",
            "AutoDirty",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MissingMarkDirty = new DiagnosticDescriptor(
            "AD0003",
            "AutoDirty class must define MarkDirty",
            "Class '{0}' must define an instance method named MarkDirty()",
            "AutoDirty",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MissingProperties = new DiagnosticDescriptor(
            "AD0004",
            "AutoDirty class has no generated properties",
            "Class '{0}' has [AutoDirty] but no m_ fields or [AutoDirtyProperty(name, type)] attributes",
            "AutoDirty",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor DuplicateProperty = new DiagnosticDescriptor(
            "AD0005",
            "AutoDirty generated property already exists",
            "Property '{0}' already exists in class '{1}'; remove the handwritten property or exclude its backing field",
            "AutoDirty",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new AutoDirtySyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            context.AddSource("AutoDirty.Attributes.g.cs", SourceText.From(AttributeSource, Encoding.UTF8));

            if (!(context.SyntaxReceiver is AutoDirtySyntaxReceiver receiver))
            {
                return;
            }

            foreach (var classDeclaration in receiver.Candidates)
            {
                if (!HasAttributeNamed(classDeclaration, AutoDirtyAttributeName))
                {
                    continue;
                }

                var semanticModel = context.Compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken);
                if (classSymbol == null)
                {
                    continue;
                }

                if (!classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    context.ReportDiagnostic(Diagnostic.Create(ClassMustBePartial, classDeclaration.Identifier.GetLocation(), classSymbol.ToDisplayString()));
                    continue;
                }

                if (!HasParameterlessMarkDirty(classSymbol))
                {
                    context.ReportDiagnostic(Diagnostic.Create(MissingMarkDirty, classDeclaration.Identifier.GetLocation(), classSymbol.ToDisplayString()));
                    continue;
                }

                var properties = GetGeneratedProperties(classDeclaration, classSymbol, semanticModel).ToList();
                if (properties.Count == 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(MissingProperties, classDeclaration.Identifier.GetLocation(), classSymbol.ToDisplayString()));
                    continue;
                }

                var existingPropertyNames = new HashSet<string>(classSymbol.GetMembers().OfType<IPropertySymbol>().Select(p => p.Name));
                var hasDuplicate = false;
                foreach (var property in properties)
                {
                    if (existingPropertyNames.Contains(property.Name))
                    {
                        hasDuplicate = true;
                        context.ReportDiagnostic(Diagnostic.Create(DuplicateProperty, classDeclaration.Identifier.GetLocation(), property.Name, classSymbol.ToDisplayString()));
                    }
                }

                if (hasDuplicate)
                {
                    continue;
                }

                var source = GenerateClass(classSymbol, properties);
                context.AddSource(GetHintName(classSymbol) + ".AutoDirty.g.cs", SourceText.From(source, Encoding.UTF8));
            }
        }

        private static IEnumerable<DirtyProperty> GetGeneratedProperties(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol classSymbol, SemanticModel semanticModel)
        {
            var emitted = new HashSet<string>();

            foreach (var attribute in classDeclaration.AttributeLists.SelectMany(list => list.Attributes))
            {
                if (!IsAttributeNamed(attribute, AutoDirtyPropertyAttributeName) || attribute.ArgumentList == null || attribute.ArgumentList.Arguments.Count < 2)
                {
                    continue;
                }

                var nameExpression = attribute.ArgumentList.Arguments[0].Expression;
                var typeExpression = attribute.ArgumentList.Arguments[1].Expression;

                var propertyName = semanticModel.GetConstantValue(nameExpression).Value as string;
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    continue;
                }

                if (typeExpression is TypeOfExpressionSyntax typeOfExpression)
                {
                    var typeSymbol = semanticModel.GetTypeInfo(typeOfExpression.Type).Type;
                    if (typeSymbol != null && emitted.Add(propertyName!))
                    {
                        yield return DirtyProperty.FromAttribute(propertyName!, typeSymbol);
                    }
                }
            }

            foreach (var field in classSymbol.GetMembers().OfType<IFieldSymbol>())
            {
                if (field.IsStatic || field.IsConst || field.IsReadOnly || field.AssociatedSymbol != null)
                {
                    continue;
                }

                if (!field.Name.StartsWith("m_") || HasFieldAttributeNamed(field, AutoDirtyIgnoreAttributeName))
                {
                    continue;
                }

                var propertyName = GetFieldPropertyName(field, semanticModel);
                if (string.IsNullOrEmpty(propertyName) || !emitted.Add(propertyName))
                {
                    continue;
                }

                yield return DirtyProperty.FromField(propertyName, field.Type, field.Name);
            }
        }

        private static bool HasAttributeNamed(ClassDeclarationSyntax classDeclaration, string attributeName)
        {
            foreach (var attribute in classDeclaration.AttributeLists.SelectMany(list => list.Attributes))
            {
                if (IsAttributeNamed(attribute, attributeName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAttributeNamed(ISymbol symbol, string attributeName)
        {
            return symbol.GetAttributes().Any(attribute =>
            {
                var name = attribute.AttributeClass == null ? null : attribute.AttributeClass.Name;
                return name == attributeName || name == attributeName.Replace("Attribute", string.Empty);
            });
        }

        private static bool HasFieldAttributeNamed(IFieldSymbol field, string attributeName)
        {
            foreach (var syntaxReference in field.DeclaringSyntaxReferences)
            {
                if (!(syntaxReference.GetSyntax() is VariableDeclaratorSyntax variable) ||
                    !(variable.Parent is VariableDeclarationSyntax declaration) ||
                    !(declaration.Parent is FieldDeclarationSyntax fieldDeclaration))
                {
                    continue;
                }

                foreach (var attribute in fieldDeclaration.AttributeLists.SelectMany(list => list.Attributes))
                {
                    if (IsAttributeNamed(attribute, attributeName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string GenerateClass(INamedTypeSymbol classSymbol, IReadOnlyList<DirtyProperty> properties)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("#nullable disable");
            builder.AppendLine();

            var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : classSymbol.ContainingNamespace.ToDisplayString();

            if (namespaceName != null)
            {
                builder.Append("namespace ").Append(namespaceName).AppendLine();
                builder.AppendLine("{");
            }

            AppendContainingTypes(builder, classSymbol);
            AppendTypeStart(builder, classSymbol, GetIndent(classSymbol), true);

            var indent = GetIndent(classSymbol) + "    ";
            builder.Append(indent).AppendLine("private global::System.Action __autoDirtyParentDirty;");
            builder.AppendLine();

            foreach (var property in properties)
            {
                var fieldName = property.BackingFieldName;
                var typeName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var propertyTypeName = property.GetPropertyTypeName();

                if (property.GenerateBackingField)
                {
                    builder.Append(indent).Append("private ").Append(typeName).Append(' ').Append(fieldName).AppendLine(";");
                }

                if (property.IsList)
                {
                    builder.Append(indent)
                        .Append("private global::Amanda.AutoDirtyList<")
                        .Append(property.ListElementTypeName)
                        .Append("> __autoDirty_")
                        .Append(ToCamelCase(property.Name))
                        .AppendLine(";");
                }
                else if (property.IsDictionary)
                {
                    builder.Append(indent)
                        .Append("private global::Amanda.AutoDirtyDictionary<")
                        .Append(property.DictionaryKeyTypeName)
                        .Append(", ")
                        .Append(property.DictionaryValueTypeName)
                        .Append("> __autoDirty_")
                        .Append(ToCamelCase(property.Name))
                        .AppendLine(";");
                }
                else if (property.IsHashSet)
                {
                    builder.Append(indent)
                        .Append("private global::Amanda.AutoDirtySet<")
                        .Append(property.HashSetElementTypeName)
                        .Append("> __autoDirty_")
                        .Append(ToCamelCase(property.Name))
                        .AppendLine(";");
                }

                builder.Append(indent).Append("public ").Append(propertyTypeName).Append(' ').Append(property.Name).AppendLine();
                builder.Append(indent).AppendLine("{");
                AppendGetter(builder, indent, property, fieldName);
                builder.Append(indent).AppendLine("    set");
                builder.Append(indent).AppendLine("    {");
                AppendSetter(builder, indent, property, fieldName, typeName);
                builder.Append(indent).AppendLine("    }");
                builder.Append(indent).AppendLine("}");
                builder.AppendLine();
            }

            builder.Append(indent).AppendLine("void global::Amanda.IAutoDirtyNode.SetDirtyCallback(global::System.Action markDirty)");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).AppendLine("    __autoDirtyParentDirty = markDirty;");
            builder.Append(indent).AppendLine("}");
            builder.AppendLine();
            builder.Append(indent).AppendLine("private void __AutoDirtyMarkDirty()");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).AppendLine("    MarkDirty();");
            builder.Append(indent).AppendLine("    __autoDirtyParentDirty?.Invoke();");
            builder.Append(indent).AppendLine("}");

            AppendTypeEnd(builder, classSymbol);

            if (namespaceName != null)
            {
                builder.AppendLine("}");
            }

            return builder.ToString();
        }

        private static void AppendGetter(StringBuilder builder, string indent, DirtyProperty property, string fieldName)
        {
            if (!property.IsList && !property.IsDictionary && !property.IsHashSet)
            {
                builder.Append(indent).AppendLine("    get");
                builder.Append(indent).AppendLine("    {");
                if (property.CanContainDirtyNode)
                {
                    builder.Append(indent).Append("        if (").Append(fieldName).AppendLine(" is global::Amanda.IAutoDirtyNode __autoDirtyChild)");
                    builder.Append(indent).AppendLine("            __autoDirtyChild.SetDirtyCallback(__AutoDirtyMarkDirty);");
                }

                builder.Append(indent).Append("        return ").Append(fieldName).AppendLine(";");
                builder.Append(indent).AppendLine("    }");
                return;
            }

            var wrapperName = "__autoDirty_" + ToCamelCase(property.Name);
            builder.Append(indent).AppendLine("    get");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).Append("        if (").Append(fieldName).AppendLine(" == null)");
            if (property.IsList)
            {
                builder.Append(indent).Append("            ").Append(fieldName).Append(" = new global::System.Collections.Generic.List<")
                    .Append(property.ListElementTypeName)
                    .AppendLine(">();");
            }
            else if (property.IsDictionary)
            {
                builder.Append(indent).Append("            ").Append(fieldName).Append(" = new global::System.Collections.Generic.Dictionary<")
                    .Append(property.DictionaryKeyTypeName)
                    .Append(", ")
                    .Append(property.DictionaryValueTypeName)
                    .AppendLine(">();");
            }
            else
            {
                builder.Append(indent).Append("            ").Append(fieldName).Append(" = new global::System.Collections.Generic.HashSet<")
                    .Append(property.HashSetElementTypeName)
                    .AppendLine(">();");
            }

            builder.Append(indent).Append("        if (").Append(wrapperName).AppendLine(" == null)");
            builder.Append(indent).AppendLine("        {");
            builder.Append(indent).Append("            ").Append(wrapperName);
            if (property.IsList)
            {
                builder.Append(" = new global::Amanda.AutoDirtyList<")
                    .Append(property.ListElementTypeName)
                    .Append(">(");
            }
            else if (property.IsDictionary)
            {
                builder.Append(" = new global::Amanda.AutoDirtyDictionary<")
                    .Append(property.DictionaryKeyTypeName)
                    .Append(", ")
                    .Append(property.DictionaryValueTypeName)
                    .Append(">(");
            }
            else
            {
                builder.Append(" = new global::Amanda.AutoDirtySet<")
                    .Append(property.HashSetElementTypeName)
                    .Append(">(");
            }

            builder.Append(fieldName).AppendLine(", __AutoDirtyMarkDirty);");
            builder.Append(indent).AppendLine("        }");
            builder.AppendLine();
            builder.Append(indent).Append("        return ").Append(wrapperName).AppendLine(";");
            builder.Append(indent).AppendLine("    }");
        }

        private static void AppendSetter(StringBuilder builder, string indent, DirtyProperty property, string fieldName, string typeName)
        {
            if (property.IsList)
            {
                var wrapperName = "__autoDirty_" + ToCamelCase(property.Name);
                builder.Append(indent).Append("        if (global::System.Object.ReferenceEquals(").Append(fieldName).AppendLine(", value))");
                builder.Append(indent).AppendLine("            return;");
                builder.Append(indent).Append("        ").Append(fieldName).Append(" = value == null ? null : new global::System.Collections.Generic.List<")
                    .Append(property.ListElementTypeName)
                    .AppendLine(">(value);");
                builder.Append(indent).Append("        ").Append(wrapperName).AppendLine(" = null;");
                builder.Append(indent).AppendLine("        __AutoDirtyMarkDirty();");
                return;
            }

            if (property.IsDictionary)
            {
                var wrapperName = "__autoDirty_" + ToCamelCase(property.Name);
                builder.Append(indent).Append("        if (global::System.Object.ReferenceEquals(").Append(fieldName).AppendLine(", value))");
                builder.Append(indent).AppendLine("            return;");
                builder.Append(indent).Append("        ").Append(fieldName).Append(" = value == null ? null : new global::System.Collections.Generic.Dictionary<")
                    .Append(property.DictionaryKeyTypeName)
                    .Append(", ")
                    .Append(property.DictionaryValueTypeName)
                    .AppendLine(">(value);");
                builder.Append(indent).Append("        ").Append(wrapperName).AppendLine(" = null;");
                builder.Append(indent).AppendLine("        __AutoDirtyMarkDirty();");
                return;
            }

            if (property.IsHashSet)
            {
                var wrapperName = "__autoDirty_" + ToCamelCase(property.Name);
                builder.Append(indent).Append("        if (global::System.Object.ReferenceEquals(").Append(fieldName).AppendLine(", value))");
                builder.Append(indent).AppendLine("            return;");
                builder.Append(indent).Append("        ").Append(fieldName).Append(" = value == null ? null : new global::System.Collections.Generic.HashSet<")
                    .Append(property.HashSetElementTypeName)
                    .AppendLine(">(value);");
                builder.Append(indent).Append("        ").Append(wrapperName).AppendLine(" = null;");
                builder.Append(indent).AppendLine("        __AutoDirtyMarkDirty();");
                return;
            }

            builder.Append(indent).Append("        if (global::System.Collections.Generic.EqualityComparer<").Append(typeName).Append(">.Default.Equals(").Append(fieldName).AppendLine(", value))");
            builder.Append(indent).AppendLine("            return;");
            builder.Append(indent).Append("        ").Append(fieldName).AppendLine(" = value;");
            if (property.CanContainDirtyNode)
            {
                builder.Append(indent).AppendLine("        if (value is global::Amanda.IAutoDirtyNode __autoDirtyChild)");
                builder.Append(indent).AppendLine("            __autoDirtyChild.SetDirtyCallback(__AutoDirtyMarkDirty);");
            }

            builder.Append(indent).AppendLine("        __AutoDirtyMarkDirty();");
        }

        private static void AppendContainingTypes(StringBuilder builder, INamedTypeSymbol classSymbol)
        {
            var containingTypes = new Stack<INamedTypeSymbol>();
            var current = classSymbol.ContainingType;
            while (current != null)
            {
                containingTypes.Push(current);
                current = current.ContainingType;
            }

            var indent = string.Empty;
            while (containingTypes.Count > 0)
            {
                AppendTypeStart(builder, containingTypes.Pop(), indent, false);
                indent += "    ";
            }
        }

        private static void AppendTypeStart(StringBuilder builder, INamedTypeSymbol typeSymbol, string indent, bool implementsDirtyNode)
        {
            builder.Append(indent)
                .Append(GetAccessibility(typeSymbol.DeclaredAccessibility))
                .Append(" partial ")
                .Append(typeSymbol.TypeKind == TypeKind.Struct ? "struct " : "class ")
                .Append(typeSymbol.Name);

            if (typeSymbol.TypeParameters.Length > 0)
            {
                builder.Append('<').Append(string.Join(", ", typeSymbol.TypeParameters.Select(p => p.Name))).Append('>');
            }

            if (implementsDirtyNode)
            {
                builder.Append(" : global::Amanda.IAutoDirtyNode");
            }

            builder.AppendLine();
            builder.Append(indent).AppendLine("{");
        }

        private static void AppendTypeEnd(StringBuilder builder, INamedTypeSymbol classSymbol)
        {
            var depth = 1;
            var current = classSymbol.ContainingType;
            while (current != null)
            {
                depth++;
                current = current.ContainingType;
            }

            for (var i = depth - 1; i >= 0; i--)
            {
                builder.Append(new string(' ', i * 4)).AppendLine("}");
            }
        }

        private static string GetIndent(INamedTypeSymbol classSymbol)
        {
            var depth = 0;
            var current = classSymbol.ContainingType;
            while (current != null)
            {
                depth++;
                current = current.ContainingType;
            }

            return new string(' ', depth * 4);
        }

        private static bool HasParameterlessMarkDirty(INamedTypeSymbol classSymbol)
        {
            return classSymbol.GetMembers("MarkDirty")
                .OfType<IMethodSymbol>()
                .Any(method =>
                    !method.IsStatic &&
                    method.Parameters.Length == 0 &&
                    method.MethodKind == MethodKind.Ordinary);
        }

        private static bool IsAttributeNamed(AttributeSyntax attribute, string attributeName)
        {
            var name = attribute.Name.ToString();
            if (name.Contains("."))
            {
                name = name.Substring(name.LastIndexOf('.') + 1);
            }

            return name == attributeName || name == attributeName.Replace("Attribute", string.Empty);
        }

        private static string GetAccessibility(Accessibility accessibility)
        {
            switch (accessibility)
            {
                case Accessibility.Public:
                    return "public";
                case Accessibility.Internal:
                    return "internal";
                case Accessibility.Private:
                    return "private";
                case Accessibility.Protected:
                    return "protected";
                case Accessibility.ProtectedAndInternal:
                    return "private protected";
                case Accessibility.ProtectedOrInternal:
                    return "protected internal";
                default:
                    return "private";
            }
        }

        private static string ToCamelCase(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "value";
            }

            if (name.Length == 1)
            {
                return char.ToLowerInvariant(name[0]).ToString();
            }

            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        private static string FieldNameToPropertyName(string fieldName)
        {
            var name = fieldName.StartsWith("m_") ? fieldName.Substring(2) : fieldName;
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        private static string GetFieldPropertyName(IFieldSymbol field, SemanticModel semanticModel)
        {
            foreach (var syntaxReference in field.DeclaringSyntaxReferences)
            {
                if (!(syntaxReference.GetSyntax() is VariableDeclaratorSyntax variable) ||
                    !(variable.Parent is VariableDeclarationSyntax declaration) ||
                    !(declaration.Parent is FieldDeclarationSyntax fieldDeclaration))
                {
                    continue;
                }

                foreach (var attribute in fieldDeclaration.AttributeLists.SelectMany(list => list.Attributes))
                {
                    if (!IsAttributeNamed(attribute, AutoDirtyPropertyNameAttributeName) ||
                        attribute.ArgumentList == null ||
                        attribute.ArgumentList.Arguments.Count == 0)
                    {
                        continue;
                    }

                    var value = semanticModel.GetConstantValue(attribute.ArgumentList.Arguments[0].Expression);
                    if (value.HasValue && value.Value is string name && !string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }

            return FieldNameToPropertyName(field.Name);
        }

        private static string GetHintName(INamedTypeSymbol symbol)
        {
            var fullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return fullName.Replace("global::", string.Empty)
                .Replace('<', '_')
                .Replace('>', '_')
                .Replace('.', '_')
                .Replace(',', '_')
                .Replace(' ', '_');
        }

private const string AttributeSource =
@"// <auto-generated />
#nullable disable

namespace Amanda
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Class, Inherited = false)]
    internal sealed class AutoDirtyAttribute : global::System.Attribute
    {
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    internal sealed class AutoDirtyPropertyAttribute : global::System.Attribute
    {
        public AutoDirtyPropertyAttribute(string name, global::System.Type type)
        {
        }
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.Field | global::System.AttributeTargets.Property, Inherited = false)]
    internal sealed class AutoDirtyIgnoreAttribute : global::System.Attribute
    {
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.Field, Inherited = false)]
    internal sealed class AutoDirtyPropertyNameAttribute : global::System.Attribute
    {
        public AutoDirtyPropertyNameAttribute(string name)
        {
        }
    }

    internal interface IAutoDirtyNode
    {
        void SetDirtyCallback(global::System.Action markDirty);
    }

    internal sealed class AutoDirtyList<T> : global::System.Collections.Generic.IList<T>
    {
        private readonly global::System.Collections.Generic.IList<T> _inner;
        private readonly global::System.Action _markDirty;

        public AutoDirtyList(global::System.Collections.Generic.IList<T> inner, global::System.Action markDirty)
        {
            _inner = inner;
            _markDirty = markDirty;

            for (var i = 0; i < _inner.Count; i++)
            {
                BindChild(_inner[i]);
            }
        }

        public T this[int index]
        {
            get => _inner[index];
            set
            {
                if (global::System.Collections.Generic.EqualityComparer<T>.Default.Equals(_inner[index], value))
                    return;
                _inner[index] = value;
                BindChild(value);
                _markDirty();
            }
        }

        public int Count => _inner.Count;

        public bool IsReadOnly => _inner.IsReadOnly;

        public void Add(T item)
        {
            _inner.Add(item);
            BindChild(item);
            _markDirty();
        }

        public void Clear()
        {
            if (_inner.Count == 0)
                return;
            _inner.Clear();
            _markDirty();
        }

        public bool Contains(T item) => _inner.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);

        public global::System.Collections.Generic.IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();

        public int IndexOf(T item) => _inner.IndexOf(item);

        public void Insert(int index, T item)
        {
            _inner.Insert(index, item);
            BindChild(item);
            _markDirty();
        }

        public bool Remove(T item)
        {
            var removed = _inner.Remove(item);
            if (removed)
                _markDirty();
            return removed;
        }

        public void RemoveAt(int index)
        {
            _inner.RemoveAt(index);
            _markDirty();
        }

        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private void BindChild(T item)
        {
            if (item is IAutoDirtyNode child)
                child.SetDirtyCallback(_markDirty);
        }
    }

    internal sealed class AutoDirtyDictionary<TKey, TValue> : global::System.Collections.Generic.IDictionary<TKey, TValue>
    {
        private readonly global::System.Collections.Generic.IDictionary<TKey, TValue> _inner;
        private readonly global::System.Action _markDirty;

        public AutoDirtyDictionary(global::System.Collections.Generic.IDictionary<TKey, TValue> inner, global::System.Action markDirty)
        {
            _inner = inner;
            _markDirty = markDirty;

            foreach (var value in _inner.Values)
            {
                BindChild(value);
            }
        }

        public TValue this[TKey key]
        {
            get => _inner[key];
            set
            {
                if (_inner.TryGetValue(key, out var oldValue) &&
                    global::System.Collections.Generic.EqualityComparer<TValue>.Default.Equals(oldValue, value))
                    return;

                _inner[key] = value;
                BindChild(value);
                _markDirty();
            }
        }

        public global::System.Collections.Generic.ICollection<TKey> Keys => _inner.Keys;

        public global::System.Collections.Generic.ICollection<TValue> Values => _inner.Values;

        public int Count => _inner.Count;

        public bool IsReadOnly => _inner.IsReadOnly;

        public void Add(TKey key, TValue value)
        {
            _inner.Add(key, value);
            BindChild(value);
            _markDirty();
        }

        public void Add(global::System.Collections.Generic.KeyValuePair<TKey, TValue> item)
        {
            _inner.Add(item);
            BindChild(item.Value);
            _markDirty();
        }

        public void Clear()
        {
            if (_inner.Count == 0)
                return;
            _inner.Clear();
            _markDirty();
        }

        public bool Contains(global::System.Collections.Generic.KeyValuePair<TKey, TValue> item) => _inner.Contains(item);

        public bool ContainsKey(TKey key) => _inner.ContainsKey(key);

        public void CopyTo(global::System.Collections.Generic.KeyValuePair<TKey, TValue>[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);

        public global::System.Collections.Generic.IEnumerator<global::System.Collections.Generic.KeyValuePair<TKey, TValue>> GetEnumerator() => _inner.GetEnumerator();

        public bool Remove(TKey key)
        {
            var removed = _inner.Remove(key);
            if (removed)
                _markDirty();
            return removed;
        }

        public bool Remove(global::System.Collections.Generic.KeyValuePair<TKey, TValue> item)
        {
            var removed = _inner.Remove(item);
            if (removed)
                _markDirty();
            return removed;
        }

        public bool TryGetValue(TKey key, out TValue value) => _inner.TryGetValue(key, out value);

        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private void BindChild(TValue value)
        {
            if (value is IAutoDirtyNode child)
                child.SetDirtyCallback(_markDirty);
        }
    }

    public sealed class AutoDirtySet<T> : global::System.Collections.Generic.ISet<T>
    {
        private readonly global::System.Collections.Generic.ISet<T> _inner;
        private readonly global::System.Action _markDirty;

        public AutoDirtySet(global::System.Collections.Generic.ISet<T> inner, global::System.Action markDirty)
        {
            _inner = inner;
            _markDirty = markDirty;

            foreach (var item in _inner)
            {
                BindChild(item);
            }
        }

        public T this[int index]
        {
            get
            {
                var i = 0;
                foreach (var item in _inner)
                {
                    if (i == index)
                        return item;
                    i++;
                }

                throw new global::System.ArgumentOutOfRangeException(nameof(index));
            }
            set
            {
                var oldValue = this[index];
                if (global::System.Collections.Generic.EqualityComparer<T>.Default.Equals(oldValue, value))
                    return;

                _inner.Remove(oldValue);
                _inner.Add(value);
                BindChild(value);
                _markDirty();
            }
        }

        public int Count => _inner.Count;

        public bool IsReadOnly => _inner.IsReadOnly;

        public bool Add(T item)
        {
            var added = _inner.Add(item);
            if (added)
            {
                BindChild(item);
                _markDirty();
            }

            return added;
        }

        void global::System.Collections.Generic.ICollection<T>.Add(T item)
        {
            Add(item);
        }

        public void Clear()
        {
            if (_inner.Count == 0)
                return;
            _inner.Clear();
            _markDirty();
        }

        public bool Contains(T item) => _inner.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);

        public void ExceptWith(global::System.Collections.Generic.IEnumerable<T> other)
        {
            var before = _inner.Count;
            _inner.ExceptWith(other);
            if (_inner.Count != before)
                _markDirty();
        }

        public global::System.Collections.Generic.IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();

        public void IntersectWith(global::System.Collections.Generic.IEnumerable<T> other)
        {
            var snapshot = new global::System.Collections.Generic.HashSet<T>(_inner);
            _inner.IntersectWith(other);
            if (!snapshot.SetEquals(_inner))
                _markDirty();
        }

        public bool IsProperSubsetOf(global::System.Collections.Generic.IEnumerable<T> other) => _inner.IsProperSubsetOf(other);

        public bool IsProperSupersetOf(global::System.Collections.Generic.IEnumerable<T> other) => _inner.IsProperSupersetOf(other);

        public bool IsSubsetOf(global::System.Collections.Generic.IEnumerable<T> other) => _inner.IsSubsetOf(other);

        public bool IsSupersetOf(global::System.Collections.Generic.IEnumerable<T> other) => _inner.IsSupersetOf(other);

        public bool Overlaps(global::System.Collections.Generic.IEnumerable<T> other) => _inner.Overlaps(other);

        public bool Remove(T item)
        {
            var removed = _inner.Remove(item);
            if (removed)
                _markDirty();
            return removed;
        }

        public bool SetEquals(global::System.Collections.Generic.IEnumerable<T> other) => _inner.SetEquals(other);

        public void SymmetricExceptWith(global::System.Collections.Generic.IEnumerable<T> other)
        {
            var snapshot = new global::System.Collections.Generic.HashSet<T>(_inner);
            _inner.SymmetricExceptWith(other);
            if (!snapshot.SetEquals(_inner))
                _markDirty();
        }

        public void UnionWith(global::System.Collections.Generic.IEnumerable<T> other)
        {
            var changed = false;
            foreach (var item in other)
            {
                if (_inner.Add(item))
                {
                    BindChild(item);
                    changed = true;
                }
            }

            if (changed)
                _markDirty();
        }

        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private void BindChild(T item)
        {
            if (item is IAutoDirtyNode child)
                child.SetDirtyCallback(_markDirty);
        }
    }
}
";

        private sealed class AutoDirtySyntaxReceiver : ISyntaxReceiver
        {
            public List<ClassDeclarationSyntax> Candidates { get; } = new List<ClassDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                var classDeclaration = syntaxNode as ClassDeclarationSyntax;
                if (classDeclaration != null && classDeclaration.AttributeLists.Count > 0)
                {
                    Candidates.Add(classDeclaration);
                }
            }
        }

        private sealed class DirtyProperty
        {
            private DirtyProperty(string name, ITypeSymbol type, string backingFieldName, bool generateBackingField)
            {
                Name = name;
                Type = type;
                BackingFieldName = backingFieldName;
                GenerateBackingField = generateBackingField;
            }

            public static DirtyProperty FromAttribute(string name, ITypeSymbol type)
            {
                return new DirtyProperty(name, type, "_" + ToCamelCase(name), true);
            }

            public static DirtyProperty FromField(string name, ITypeSymbol type, string backingFieldName)
            {
                return new DirtyProperty(name, type, backingFieldName, false);
            }

            public string Name { get; }

            public ITypeSymbol Type { get; }

            public string BackingFieldName { get; }

            public bool GenerateBackingField { get; }

            public bool IsList
            {
                get
                {
                    return Type is INamedTypeSymbol namedType &&
                        namedType.IsGenericType &&
                        namedType.Name == "List" &&
                        namedType.TypeArguments.Length == 1 &&
                        namedType.ContainingNamespace.ToDisplayString() == "System.Collections.Generic";
                }
            }

            public bool IsDictionary
            {
                get
                {
                    return Type is INamedTypeSymbol namedType &&
                        namedType.IsGenericType &&
                        namedType.Name == "Dictionary" &&
                        namedType.TypeArguments.Length == 2 &&
                        namedType.ContainingNamespace.ToDisplayString() == "System.Collections.Generic";
                }
            }

            public bool IsHashSet
            {
                get
                {
                    return Type is INamedTypeSymbol namedType &&
                        namedType.IsGenericType &&
                        namedType.Name == "HashSet" &&
                        namedType.TypeArguments.Length == 1 &&
                        namedType.ContainingNamespace.ToDisplayString() == "System.Collections.Generic";
                }
            }

            public string ListElementTypeName
            {
                get
                {
                    var namedType = (INamedTypeSymbol)Type;
                    return namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }

            public string DictionaryKeyTypeName
            {
                get
                {
                    var namedType = (INamedTypeSymbol)Type;
                    return namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }

            public string DictionaryValueTypeName
            {
                get
                {
                    var namedType = (INamedTypeSymbol)Type;
                    return namedType.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }

            public string HashSetElementTypeName
            {
                get
                {
                    var namedType = (INamedTypeSymbol)Type;
                    return namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }

            public string GetPropertyTypeName()
            {
                if (IsList)
                {
                    return "global::System.Collections.Generic.IList<" + ListElementTypeName + ">";
                }

                if (IsDictionary)
                {
                    return "global::System.Collections.Generic.IDictionary<" + DictionaryKeyTypeName + ", " + DictionaryValueTypeName + ">";
                }

                if (IsHashSet)
                {
                    return "global::Amanda.AutoDirtySet<" + HashSetElementTypeName + ">";
                }

                return Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            public bool CanContainDirtyNode
            {
                get
                {
                    if (Type.IsValueType)
                    {
                        return false;
                    }

                    var namedType = Type as INamedTypeSymbol;
                    return namedType == null || !namedType.IsSealed;
                }
            }
        }
    }
}
