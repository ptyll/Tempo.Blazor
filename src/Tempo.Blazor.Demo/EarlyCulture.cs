using System.Runtime.InteropServices.JavaScript;

namespace Tempo.Blazor.Demo;

/// <summary>
/// Reads the persisted culture before the host is built. The renderer's execution context captures
/// the culture at build time, so a culture applied afterwards never reaches a component render.
/// The JS runtime is already live when <c>Main</c> runs, which is what makes this callable here.
/// </summary>
internal static partial class EarlyCulture
{
    [JSImport("globalThis.localStorage.getItem")]
    internal static partial string? ReadStoredCulture(string key);
}
