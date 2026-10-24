using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Tempo.Blazor.Components.Wireframe.Stencil;

/// <summary>
/// The stencil theme a render pass paints with (decision WIREFRAME-STENCIL-THEME). <c>StencilTokenScope</c> stays internal: hosts pass a plain
/// <c>StencilTheme</c> string (editor, canvas, toolbox previews, headless renderer, export request) and every one of those entry points opens
/// <see cref="Use"/> around the synchronous <c>RenderSvg</c> calls; the compiled pack renderers read <see cref="CurrentName"/> to pick the pack
/// theme layer. A theme is ambient (async-local) instead of a parameter of <c>WireframeComponentDef.RenderSvg</c> because that delegate is the
/// frozen contract between the registry, custom providers and the renderers.
/// <para>Resolution order (<see cref="Resolve"/>): the host's <c>StencilTheme</c> → the page's <c>TargetTheme</c> → the document's
/// <c>TargetTheme</c> → the default tokens. A blank value counts as unset; <c>light</c> / <c>default</c> are the default tokens; an unknown name
/// is the default tokens plus ONE log warning per name for the life of the process.</para>
/// </summary>
internal static class StencilThemeScope
{
    private static readonly AsyncLocal<string?> Ambient = new();

    // Theme names any compiled pack declares, canonical casing. Seeded with the built-in pack so a name is judged correctly even before a
    // registry compiled anything; custom packs add theirs when they compile.
    private static readonly ConcurrentDictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> Warned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The theme the current render pass paints with (canonical name), or <c>null</c> for the default tokens.</summary>
    public static string? CurrentName => Ambient.Value;

    /// <summary>Records the theme names a compiled pack declares, so <see cref="Resolve"/> knows them.</summary>
    public static void Register(IEnumerable<string> themeNames)
    {
        foreach (var name in themeNames)
            if (!string.IsNullOrWhiteSpace(name)) Known.TryAdd(name.Trim(), name.Trim());
    }

    /// <summary>
    /// Opens a theme scope for the synchronous render calls inside it; disposing restores the previous theme. <paramref name="name"/> is a name
    /// already produced by <see cref="Resolve"/> (or <c>null</c> for the default tokens).
    /// </summary>
    public static IDisposable Use(string? name)
    {
        var previous = Ambient.Value;
        Ambient.Value = string.IsNullOrWhiteSpace(name) ? null : Canonical(name.Trim());
        return new Restore(previous);
    }

    /// <summary>
    /// Picks the first set value of <paramref name="host"/>, <paramref name="page"/>, <paramref name="document"/> and maps it to a canonical
    /// theme name, or <c>null</c> (default tokens) for none, <c>light</c> / <c>default</c> and an unknown name (warned once per name).
    /// </summary>
    public static string? Resolve(string? host, string? page, string? document, ILogger? logger)
    {
        var requested = First(host) ?? First(page) ?? First(document);
        if (requested is null) return null;
        if (requested.Equals("light", StringComparison.OrdinalIgnoreCase) || requested.Equals("default", StringComparison.OrdinalIgnoreCase))
            return null;

        EnsureBuiltInKnown();
        if (Known.TryGetValue(requested, out var canonical)) return canonical;

        if (Warned.TryAdd(requested, 0))
            logger?.LogWarning("Wireframe stencil theme '{Theme}' is not defined by any stencil pack; the default tokens are used.", requested);
        return null;
    }

    private static string Canonical(string name) => Known.TryGetValue(name, out var canonical) ? canonical : name;

    private static string? First(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureBuiltInKnown()
    {
        if (!Known.IsEmpty) return;
        try { Register(BuiltInStencilPackProvider.LoadPack().Themes.Keys); }
        catch (Exception) { /* a broken embedded pack surfaces where it is compiled; here an unknown name just falls back */ }
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            Ambient.Value = previous;
        }
    }
}
