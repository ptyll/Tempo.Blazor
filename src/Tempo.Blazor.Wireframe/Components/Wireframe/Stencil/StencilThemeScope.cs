using System.Collections.Concurrent;
using System.Text.RegularExpressions;
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
/// is the default tokens plus ONE log warning per name for the life of the process (at most <see cref="MaxWarnedNames"/> names, then one
/// "further unknown themes suppressed" line). A name longer than <see cref="WireframeStencilThemeName.MaxLength"/> characters or outside
/// <c>[A-Za-z0-9._-]</c> is treated as the default tokens silently, is never stored and never logged, so request input cannot grow memory or poison the log.</para>
/// <para><b>Global theme-name registry:</b> the set of known theme names is process-wide (not per registry or per app scope): every compiled pack adds
/// the theme names it declares, and a name known to ANY pack resolves; a pack that does not declare the name simply keeps its own default tokens.</para>
/// <para><b>Synchronous only:</b> the theme is held in an <see cref="AsyncLocal{T}"/> and <see cref="Use"/> must wrap synchronous <c>RenderSvg</c>
/// calls only. Do not <c>await</c> inside the scope (and do not hand the scope to another thread): a render function that awaits would resume with
/// whatever theme is ambient there. <c>RenderSvg</c> is a synchronous delegate, so a custom provider is safe by construction.</para>
/// </summary>
internal static class StencilThemeScope
{
    private static readonly AsyncLocal<string?> Ambient = new();

    // Theme names any compiled pack declares, canonical casing. Seeded with the built-in pack so a name is judged correctly even before a
    // registry compiled anything; custom packs add theirs when they compile.
    private static readonly ConcurrentDictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> Warned = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<bool> BuiltInLoaded = new(LoadBuiltIn);
    private static int _suppressedLogged;

    /// <summary>Upper bound of distinct unknown theme names that are warned about (and remembered) per process.</summary>
    internal const int MaxWarnedNames = 64;

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
        Ambient.Value = string.IsNullOrWhiteSpace(name) || !WireframeStencilThemeName.IsValid(name) ? null : Canonical(name.Trim());
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
        if (!WireframeStencilThemeName.IsValid(requested)) return null;   // malformed / over-long: default tokens, no warn, no store
        if (requested.Equals("light", StringComparison.OrdinalIgnoreCase) || requested.Equals("default", StringComparison.OrdinalIgnoreCase))
            return null;

        EnsureBuiltInKnown();
        if (Known.TryGetValue(requested, out var canonical)) return canonical;

        if (logger is null) return null;   // nobody to tell: nothing is stored either
        if (Warned.Count >= MaxWarnedNames)
        {
            if (!Warned.ContainsKey(requested) && Interlocked.Exchange(ref _suppressedLogged, 1) == 0)
                logger.LogWarning("Further unknown wireframe stencil themes are suppressed (limit {Limit}); the default tokens are used.", MaxWarnedNames);
            return null;
        }
        if (Warned.TryAdd(requested, 0))
            logger.LogWarning("Wireframe stencil theme '{Theme}' is not defined by any stencil pack; the default tokens are used.", requested);
        return null;
    }

    /// <summary>Test seam: forgets the warned names and the suppression flag (the registry is process-wide).</summary>
    internal static void ResetWarnedForTests()
    {
        Warned.Clear();
        Interlocked.Exchange(ref _suppressedLogged, 0);
    }

    private static string Canonical(string name) => Known.TryGetValue(name, out var canonical) ? canonical : name;

    private static string? First(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Its own flag (not Known.IsEmpty): a custom pack compiled first must not stop the built-in themes from being seeded.
    private static void EnsureBuiltInKnown() => _ = BuiltInLoaded.Value;

    private static bool LoadBuiltIn()
    {
        try { Register(BuiltInStencilPackProvider.LoadPack().Themes.Keys); }
        catch (Exception) { /* a broken embedded pack surfaces where it is compiled; here an unknown name just falls back */ }
        return true;
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
