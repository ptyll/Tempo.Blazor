using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe.Stencil;

/// <summary>Compiled stencil pack definitions plus non-fatal validation diagnostics.</summary>
public sealed record StencilPackCompilationResult(
    IReadOnlyList<WireframeComponentDef> Definitions,
    IReadOnlyList<StencilPackValidationWarning> Warnings);

/// <summary>Compiles declarative stencil pack components into wireframe component definitions.</summary>
public sealed class StencilPackCompiler
{
    private static readonly Lazy<UiRoleVocabulary> BuiltInVocabulary =
        new(() => new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]));

    private readonly NativeRendererRegistry _nativeRenderers;

    public StencilPackCompiler()
        : this(new NativeRendererRegistry())
    {
    }

    public StencilPackCompiler(NativeRendererRegistry nativeRenderers)
    {
        ArgumentNullException.ThrowIfNull(nativeRenderers);
        _nativeRenderers = nativeRenderers;
    }

    public StencilPackCompilationResult CompileWithDiagnostics(
        StencilPack pack,
        UiRoleVocabulary? vocabulary = null)
    {
        ArgumentNullException.ThrowIfNull(pack);

        var effectiveVocabulary = vocabulary ?? BuiltInVocabulary.Value;
        var warnings = StencilPackValidator.ValidateRoles(pack, effectiveVocabulary);
        return new StencilPackCompilationResult(
            CompileDefinitions(pack).ToArray(),
            warnings);
    }

    public IEnumerable<WireframeComponentDef> Compile(StencilPack pack)
        => CompileWithDiagnostics(pack).Definitions;

    private IEnumerable<WireframeComponentDef> CompileDefinitions(StencilPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        StencilThemeScope.Register(pack.Themes.Keys);
        // The token layers depend on the ambient stencil theme of the render pass (StencilThemeScope): one scope per theme name, built on first use.
        var scopes = new System.Collections.Concurrent.ConcurrentDictionary<string, StencilTokenScope>(StringComparer.Ordinal);
        StencilTokenScope TokensForCurrentTheme()
        {
            var name = StencilThemeScope.CurrentName;
            return scopes.GetOrAdd(name ?? string.Empty, key => StencilTokenScope.FromPack(pack, key.Length == 0 ? null : key));
        }
        var packMap = new Dictionary<string, StencilPack>(StringComparer.Ordinal)
        {
            [pack.Namespace] = pack
        };
        var composition = new StencilCompositionScope(
            registry: null,
            scope: null,
            currentPack: pack,
            packsByNamespace: packMap);

        foreach (var component in pack.Components)
        {
            var capturedComponent = component;
            var type = ResolveType(pack, component);
            var render = component.Native is { } native
                ? ResolveNative(pack, native)
                : (Action<WireframeElement, RenderTreeBuilder>)((element, builder) =>
                    StencilPackRenderer.Render(capturedComponent, element, TokensForCurrentTheme(), builder, composition, logger: null));

            yield return new WireframeComponentDef
            {
                Type = type,
                ScopeAppId = pack.IsBuiltIn ? null : TryGetAppScopeId(pack.Namespace),
                LocalType = WireframeComponentScope.GetLocalType(component.Type),
                Category = component.Category,
                DisplayName = component.DisplayName,
                Roles = component.Roles,
                IsContainer = component.IsContainer,
                Icon = component.Icon,
                DefaultWidth = component.DefaultSize.Width,
                DefaultHeight = component.DefaultSize.Height,
                Props = component.Props,
                RenderSvg = render,
                IsBuiltIn = pack.IsBuiltIn,
                PackId = pack.Id,
                NativeType = component.Native?.NativeType,
                Impl = component.Impl,
                SizePresets = component.SizePresets.ToDictionary(
                    static x => x.Key,
                    static x => (x.Value.Width, x.Value.Height),
                    StringComparer.Ordinal)
            };
        }
    }

    internal static string ResolveType(StencilPack pack, StencilComponent component)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(component);
        if (pack.IsBuiltIn)
            return component.Type.Trim();

        var scopeAppId = TryGetAppScopeId(pack.Namespace);
        if (!string.IsNullOrWhiteSpace(scopeAppId))
            return WireframeComponentScope.ForApp(scopeAppId).NamespaceType(component.Type);

        return $"{pack.Namespace.Trim()}:{component.Type.Trim()}";
    }

    internal static string? TryGetAppScopeId(string? namespaceOrType)
    {
        if (string.IsNullOrWhiteSpace(namespaceOrType))
            return null;

        var trimmed = namespaceOrType.Trim();
        if (WireframeComponentScope.TryGetAppId(trimmed, out var parsedAppId))
            return parsedAppId;

        const string appPrefix = "app:";
        if (!trimmed.StartsWith(appPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var appId = trimmed[appPrefix.Length..];
        return !string.IsNullOrWhiteSpace(appId) && !appId.Contains(':', StringComparison.Ordinal)
            ? appId
            : null;
    }

    private Action<WireframeElement, RenderTreeBuilder> ResolveNative(StencilPack pack, StencilNative native)
    {
        if (!pack.IsBuiltIn)
        {
            throw new InvalidOperationException(
                $"native{{}} is only allowed in the built-in Tempo pack (component '{native.NativeType}').");
        }

        return _nativeRenderers.TryGet(native.NativeType, out var renderer)
            ? renderer!
            : throw new InvalidOperationException($"No native renderer registered for '{native.NativeType}'.");
    }
}
