using Xunit;

namespace Tempo.Blazor.Tests.Icons;

/// <summary>
/// Every test class that writes the process-wide static <see cref="Tempo.Blazor.Components.Icons.IconRegistry"/>
/// (Register, RegisterProvider, Reset) runs in this collection. DisableParallelization also keeps it
/// apart from every other collection: Resolve/Contains read the registry without the lock, so a Reset
/// racing any TmIcon render could throw "Collection was modified" there too.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IconRegistryCollection
{
    public const string Name = "IconRegistry";
}
