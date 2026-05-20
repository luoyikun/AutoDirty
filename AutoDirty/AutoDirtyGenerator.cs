/*******************************************************************
 * 功能：源码生成器：[AutoDirty]的类，类成员变化自动调用MarkDirty()，支持属性和集合类型，不支持嵌套容器
 * 作者：罗翊坤
 * 时间：2026.05.20
*******************************************************************/
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
        private const string AutoDirtyRawCollectionAttributeName = "AutoDirtyRawCollectionAttribute";

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
            DiagnosticSeverity.Error,
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

                yield return DirtyProperty.FromField(
                    propertyName,
                    field.Type,
                    field.Name,
                    HasFieldAttributeNamed(field, AutoDirtyRawCollectionAttributeName));
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
            builder.Append(indent).AppendLine("private global::System.Action __autoDirtySelfDirty;");
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

                if (property.UseDirtyCollectionWrapper && property.IsList)
                {
                    builder.Append(indent)
                        .Append("private global::Amanda.AutoDirtyList<")
                        .Append(property.ListElementTypeName)
                        .Append("> __autoDirty_")
                        .Append(ToCamelCase(property.Name))
                        .AppendLine(";");
                }
                else if (property.UseDirtyCollectionWrapper && property.IsDictionary)
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
                else if (property.UseDirtyCollectionWrapper && property.IsHashSet)
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
            builder.Append(indent).AppendLine("    if (global::System.Object.ReferenceEquals(__autoDirtyParentDirty, markDirty))");
            builder.Append(indent).AppendLine("        return;");
            builder.Append(indent).AppendLine("    __autoDirtyParentDirty = markDirty;");
            builder.Append(indent).AppendLine("}");
            builder.AppendLine();
            builder.Append(indent).AppendLine("private global::System.Action __AutoDirtyGetSelfDirtyCallback()");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).AppendLine("    return __autoDirtySelfDirty ?? (__autoDirtySelfDirty = __AutoDirtyMarkDirty);");
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
            if (!property.UseDirtyCollectionWrapper || (!property.IsList && !property.IsDictionary && !property.IsHashSet))
            {
                builder.Append(indent).AppendLine("    get");
                builder.Append(indent).AppendLine("    {");
                if (property.IsReferenceType)
                {
                    builder.Append(indent).Append("        if (").Append(fieldName).AppendLine(" == null)");
                    builder.Append(indent).AppendLine("            return default;");
                }

                if (property.CanContainDirtyNode)
                {
                    builder.Append(indent).Append("        if (").Append(fieldName).AppendLine(" is global::Amanda.IAutoDirtyNode __autoDirtyChild)");
                    builder.Append(indent).AppendLine("            __autoDirtyChild.SetDirtyCallback(__AutoDirtyGetSelfDirtyCallback());");
                }

                builder.Append(indent).Append("        return ").Append(fieldName).AppendLine(";");
                builder.Append(indent).AppendLine("    }");
                return;
            }

            var wrapperName = "__autoDirty_" + ToCamelCase(property.Name);
            builder.Append(indent).AppendLine("    get");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).Append("        if (").Append(fieldName).AppendLine(" == null)");
            if (property.UseDirtyCollectionWrapper && property.IsList)
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
            if (property.UseDirtyCollectionWrapper && property.IsList)
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

            builder.Append(fieldName).AppendLine(", __AutoDirtyGetSelfDirtyCallback());");
            builder.Append(indent).AppendLine("        }");
            builder.AppendLine();
            builder.Append(indent).Append("        return ").Append(wrapperName).AppendLine(";");
            builder.Append(indent).AppendLine("    }");
        }

        private static void AppendSetter(StringBuilder builder, string indent, DirtyProperty property, string fieldName, string typeName)
        {
            if (property.UseDirtyCollectionWrapper && property.IsList)
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

            if (property.UseDirtyCollectionWrapper && property.IsDictionary)
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

            if (property.UseDirtyCollectionWrapper && property.IsHashSet)
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
                builder.Append(indent).AppendLine("            __autoDirtyChild.SetDirtyCallback(__AutoDirtyGetSelfDirtyCallback());");
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
            private DirtyProperty(string name, ITypeSymbol type, string backingFieldName, bool generateBackingField, bool preserveCollectionType)
            {
                Name = name;
                Type = type;
                BackingFieldName = backingFieldName;
                GenerateBackingField = generateBackingField;
                PreserveCollectionType = preserveCollectionType;
            }

            public static DirtyProperty FromAttribute(string name, ITypeSymbol type)
            {
                return new DirtyProperty(name, type, "_" + ToCamelCase(name), true, false);
            }

            public static DirtyProperty FromField(string name, ITypeSymbol type, string backingFieldName, bool preserveCollectionType)
            {
                return new DirtyProperty(name, type, backingFieldName, false, preserveCollectionType);
            }

            public string Name { get; }

            public ITypeSymbol Type { get; }

            public string BackingFieldName { get; }

            public bool GenerateBackingField { get; }

            public bool PreserveCollectionType { get; }

            public bool UseDirtyCollectionWrapper => !PreserveCollectionType;

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
                    if (!UseDirtyCollectionWrapper)
                    {
                        return Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    }

                    return "global::System.Collections.Generic.IList<" + ListElementTypeName + ">";
                }

                if (IsDictionary)
                {
                    if (!UseDirtyCollectionWrapper)
                    {
                        return Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    }

                    return "global::System.Collections.Generic.IDictionary<" + DictionaryKeyTypeName + ", " + DictionaryValueTypeName + ">";
                }

                if (IsHashSet)
                {
                    if (!UseDirtyCollectionWrapper)
                    {
                        return Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    }

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

                    if (Type is IArrayTypeSymbol)
                    {
                        return false;
                    }

                    var namedType = Type as INamedTypeSymbol;
                    return namedType == null || !namedType.IsSealed;
                }
            }

            public bool IsReferenceType => Type.IsReferenceType;
        }
    }
}

