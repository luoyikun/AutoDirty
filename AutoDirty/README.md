# AutoDirty

Unity-compatible Roslyn source generator. It finds classes marked with `[AutoDirty]` and emits dirty-marking properties from `m_` backing fields.

The generator targets `netstandard2.0` and references `Microsoft.CodeAnalysis.CSharp 3.8.0`, which matches Unity 2022's source generator requirements.

## Unity Usage

Build the generator:

```powershell
dotnet build AutoDirty\AutoDirty.csproj
```

The project copies `AutoDirty.dll` to:

```text
AutoDirtyUnity/Assets/Roslyn/AutoDirty.dll
```

In Unity, make sure the DLL has the `RoslynAnalyzer` label.

## Example

```csharp
using Amanda;
using UnityEngine;

namespace Amanda
{
    [AutoDirty]
    public partial class PlayerData
    {
        string m_PlayerName;
        int m_Level;
        List<int> m_ListInt = new();

        private void MarkDirty()
        {
            Debug.Log("PlayerData has been marked dirty.");
        }
    }
}
```

Generated shape:

```csharp
namespace Amanda
{
    public partial class PlayerData
    {
        public string PlayerName
        {
            get => m_PlayerName;
            set
            {
                if (global::System.Collections.Generic.EqualityComparer<string>.Default.Equals(m_PlayerName, value))
                    return;
                m_PlayerName = value;
                MarkDirty();
            }
        }
    }
}
```

Rules:

- Field names must start with `m_`.
- `m_PlayerName` becomes `PlayerName`; `m_ListInt` becomes `ListInt`.
- Add `[AutoDirtyPropertyName("data")]` to a field when you need a custom generated property name, such as `m_data` -> `data`.
- `List<T>` fields generate `IList<T>` properties backed by a dirty-aware wrapper, so `Add`, `Remove`, `Clear`, `Insert`, `RemoveAt`, and index assignment call `MarkDirty()`.
- `Dictionary<TKey, TValue>` fields generate `IDictionary<TKey, TValue>` properties backed by a dirty-aware wrapper, so `Dic[key] = value`, `Add`, `Remove`, and `Clear` call `MarkDirty()`.
- `HashSet<T>` fields generate `AutoDirtySet<T>` properties backed by a dirty-aware wrapper, so `Add`, `Remove`, `Clear`, set operations, and the helper indexer call `MarkDirty()`.
- Nested `[AutoDirty]` objects notify their parent when they are reached through the generated property, for example `data.Child.Name = "x"`.
- Generated nested dirty callbacks are cached per object, so repeated `SetDirtyCallback` calls do not allocate a new delegate each time.
- Add `[AutoDirtyIgnore]` to a field to skip it.
- Do not also handwrite `PlayerName` in the source class. A source generator can add source files, but it cannot remove or rewrite an existing auto-property.
- `[AutoDirtyProperty("Name", typeof(Type))]` is still supported for cases where you do not want to declare a backing field.
