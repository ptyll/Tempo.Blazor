using System.Globalization;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Presentation-state surface of <see cref="TmModelingEditor"/>: the derived state/label/badge
/// properties the markup binds, the provider/notation/viewpoint effective values and the cached
/// tree/issue projections. Split out of <see cref="TmModelingEditor.razor.cs"/> to keep the
/// code-behind under the repository's 1200-line source ceiling (N270/F13).
/// </summary>
public partial class TmModelingEditor
{
    private string StateName => _state switch
    {
        ModelingEditorState.Loading => "loading",
        ModelingEditorState.Loaded => "loaded",
        ModelingEditorState.Error => "error",
        _ => "empty"
    };

    private string StateLabelKey => _state switch
    {
        ModelingEditorState.Loading => "TmModelingEditor_StateLoading",
        ModelingEditorState.Loaded => "TmModelingEditor_StateLoaded",
        ModelingEditorState.Error => "TmModelingEditor_StateError",
        _ => "TmModelingEditor_StateEmpty"
    };

    /// <summary>Color variant of the title state badge — success when loaded, danger on error,
    /// primary while loading, neutral when no provider is selected.</summary>
    private BadgeVariant StateBadgeVariant => _state switch
    {
        ModelingEditorState.Loaded => BadgeVariant.Success,
        ModelingEditorState.Error => BadgeVariant.Danger,
        ModelingEditorState.Loading => BadgeVariant.Primary,
        _ => BadgeVariant.Default
    };

    /// <summary>Icon of the empty/error state mark — a box for "no model", an alert circle on error.</summary>
    private string StateMarkIcon => _state == ModelingEditorState.Error
        ? IconNames.AlertCircle
        : IconNames.Box;

    /// <summary>Text of the first status strip item: the last-loaded time when the loaded model
    /// carries metadata (shared "g" format with <see cref="TmModelingSourcePanel"/>), else the
    /// state label. The state TEXT itself lives only on the title badge (MO3 dedupe).</summary>
    private string StatusStripStateText
    {
        get
        {
            var loadedAt = _model?.Metadata.LoadedAt;
            return loadedAt is { } timestamp && timestamp != default
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    Loc["TmModelingEditor_LastLoaded"],
                    timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                : Loc[StateLabelKey];
        }
    }

    /// <summary>Leading status strip icon: check when loaded, alert on error, clock while loading —
    /// and the alert TRIANGLE when the loaded metadata is stale (final review m3: the freshness
    /// warning is colour-plus-shape, not the success check).</summary>
    private string StatusStripIcon => SourceIsStale
        ? IconNames.AlertTriangle
        : _state switch
        {
            ModelingEditorState.Loaded => IconNames.CheckCircle,
            ModelingEditorState.Error => IconNames.AlertCircle,
            ModelingEditorState.Loading => IconNames.Clock,
            _ => IconNames.Box
        };

    /// <summary>Class of the leading status icon span: the warning tint while the source is
    /// stale (final review m3), the state-driven color otherwise.</summary>
    private string StatusStripIconClass => SourceIsStale
        ? "tm-modeling-editor__status-icon tm-modeling-editor__status-icon--stale"
        : "tm-modeling-editor__status-icon";

    private string DisplayProviderKey => string.IsNullOrWhiteSpace(EffectiveProviderKey) ? Loc["TmModelingEditor_NotSelected"] : ResolveProviderDisplayName(EffectiveProviderKey!);

    /// <summary>Visible text of a provider option (header select and chip): the host's
    /// <see cref="ProviderDisplayName"/> resolution when supplied, otherwise the raw key.</summary>
    private string ResolveProviderDisplayName(string providerKey)
        => ProviderDisplayName?.Invoke(providerKey) ?? providerKey;

    /// <summary>The provider the editor loads from: the host parameter until the user picks
    /// another one in the header selector (the in-editor override wins until the parameter
    /// itself changes — same pattern as the notation/viewpoint overrides).</summary>
    private string? EffectiveProviderKey => _effectiveProviderKey ?? ProviderKey;

    /// <summary>Distinct registered provider keys for the header selector, ordered; the current
    /// key is kept as an option even when no registered provider carries it (unknown-host-key
    /// edge case) so the select never silently jumps to another value.</summary>
    private IReadOnlyList<string> ProviderOptions
    {
        get
        {
            var keys = ModelProviders
                .Select(provider => provider.ProviderKey)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();

            var current = EffectiveProviderKey;
            if (!string.IsNullOrWhiteSpace(current) && !keys.Contains(current, StringComparer.Ordinal))
            {
                keys.Insert(0, current);
            }

            return keys;
        }
    }

    private string EffectiveNotationKey => _effectiveNotationKey ?? string.Empty;

    private string EffectiveViewpointKey => _effectiveViewpointKey ?? string.Empty;

    private string ActivePanelName => _activePanel switch
    {
        ModelingEditorPanel.Tree => "tree",
        ModelingEditorPanel.Inspector => "inspector",
        ModelingEditorPanel.Issues => "issues",
        _ => "preview"
    };

    private IReadOnlyList<ModelingElementDto> LoadedElements => _model?.Elements ?? [];

    private IReadOnlyList<ModelingElementDto> VisibleTreeElements
    {
        get
        {
            var elements = LoadedElements;
            if (ReferenceEquals(elements, _visibleTreeElementsSource)
                && string.Equals(EffectiveNotationKey, _visibleTreeElementsNotationKey, StringComparison.Ordinal)
                && string.Equals(EffectiveViewpointKey, _visibleTreeElementsViewpointKey, StringComparison.Ordinal))
            {
                return _visibleTreeElements;
            }

            _visibleTreeElementsSource = elements;
            _visibleTreeElementsNotationKey = EffectiveNotationKey;
            _visibleTreeElementsViewpointKey = EffectiveViewpointKey;
            _visibleTreeElements = elements.Where(IsElementVisibleInViewpoint).ToArray();
            return _visibleTreeElements;
        }
    }

    /// <summary>Cached concatenated issues (final review: the array was rebuilt on EVERY access —
    /// the markup reads it through several fragments per render) keyed on the (_model, _issues)
    /// references; the null-model case returns the generated issues list as-is.</summary>
    private IReadOnlyList<ModelingIssueDto>? _displayIssuesCache;
    private (ModelingModelDto? Model, IReadOnlyList<ModelingIssueDto> Issues) _displayIssuesKey;

    private IReadOnlyList<ModelingIssueDto> DisplayIssues
    {
        get
        {
            if (_model is null)
            {
                return _issues;
            }

            if (_displayIssuesCache is null || !_displayIssuesKey.Equals((_model, _issues)))
            {
                _displayIssuesKey = (_model, _issues);
                _displayIssuesCache = _model.Issues.Concat(_issues).ToArray();
            }

            return _displayIssuesCache;
        }
    }

    private int IssueCount => DisplayIssues.Count;
}
