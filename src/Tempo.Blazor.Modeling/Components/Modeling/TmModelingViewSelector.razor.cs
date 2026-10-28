using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>Selects the active modeling notation and notation-specific viewpoint.</summary>
public partial class TmModelingViewSelector
{
    private string? _currentNotationOverride;
    private string? _currentViewpointOverride;
    private string? _lastNotationParameter;
    private string? _lastViewpointParameter;

    /// <summary>Stable per-instance id of the viewpoint hint — the disabled select's
    /// aria-describedby pairs with <c>{id}-hint</c> so the hint survives a TestIdPrefix rename
    /// (review fix, phase 3).</summary>
    private string ViewpointHintId { get; } = $"tm-modeling-view-selector-{Guid.NewGuid():N}-viewpoint-hint";

    private static readonly IReadOnlyList<NotationOption> BuiltInNotationOptions =
    [
        new("bpmn", "BPMN 2.0"),
        new("uml", "UML 2.5"),
        new("uml25", "UML 2.5"),
        new("archimate", "ArchiMate 3"),
        new("archimate32", "ArchiMate 3.2"),
        new("erd", "ERD")
    ];

    /// <summary>Profiles used to populate notation labels and notation-specific viewpoints.</summary>
    [Inject] public IEnumerable<IModelingNotationProfile> NotationProfiles { get; set; } = [];

    /// <summary>Selected notation key.</summary>
    [Parameter] public string? NotationKey { get; set; }

    /// <summary>Selected viewpoint key.</summary>
    [Parameter] public string? ViewpointKey { get; set; }

    /// <summary>Raised when the notation changes.</summary>
    [Parameter] public EventCallback<string?> OnNotationChanged { get; set; }

    /// <summary>Raised when the viewpoint changes.</summary>
    [Parameter] public EventCallback<string?> OnViewpointChanged { get; set; }

    /// <summary>Additional CSS class applied to the selector root.</summary>
    [Parameter] public string? Class { get; set; }

    private string RootClass => string.Join(" ", new[] { "tm-modeling-view-selector", Class }.Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (!string.Equals(_lastNotationParameter, NotationKey, StringComparison.Ordinal))
        {
            _currentNotationOverride = null;
            _lastNotationParameter = NotationKey;
        }

        if (!string.Equals(_lastViewpointParameter, ViewpointKey, StringComparison.Ordinal))
        {
            _currentViewpointOverride = null;
            _lastViewpointParameter = ViewpointKey;
        }
    }

    private string CurrentNotation => _currentNotationOverride ?? NotationKey ?? string.Empty;

    private string CurrentViewpoint => string.IsNullOrWhiteSpace(ViewpointKey)
        ? _currentViewpointOverride ?? DefaultViewpointKey
        : _currentViewpointOverride ?? ViewpointKey!;

    private IReadOnlyList<NotationOption> NotationOptions
    {
        get
        {
            var options = new Dictionary<string, NotationOption>(StringComparer.OrdinalIgnoreCase);
            foreach (var option in BuiltInNotationOptions)
            {
                options[option.Key] = option;
            }

            foreach (var profile in NotationProfiles)
            {
                if (string.IsNullOrWhiteSpace(profile.NotationKey))
                {
                    continue;
                }

                options[profile.NotationKey.Trim()] = new NotationOption(
                    profile.NotationKey.Trim(),
                    string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.NotationKey.Trim() : profile.DisplayName);
            }

            return options.Values
                .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(option => option.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private IModelingNotationProfile? CurrentProfile => NotationProfiles
        .FirstOrDefault(profile => string.Equals(profile.NotationKey, CurrentNotation, StringComparison.OrdinalIgnoreCase));

    private IReadOnlyList<ViewpointOption> ViewpointOptions => CurrentProfile?.SupportedViewpointKeys
        .Where(viewpoint => !string.IsNullOrWhiteSpace(viewpoint))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(viewpoint => GetViewpointLabel(viewpoint), StringComparer.OrdinalIgnoreCase)
        .Select(viewpoint => new ViewpointOption(viewpoint, GetViewpointLabel(viewpoint)))
        .ToArray() ?? [];

    private bool ShouldShowViewpointSelect => ViewpointOptions.Count > 0;

    private string DefaultViewpointKey => ViewpointOptions.FirstOrDefault()?.Key ?? string.Empty;

    private string ViewpointEmptyText => string.IsNullOrWhiteSpace(CurrentNotation)
        ? Loc["TmModelingViewSelector_SelectNotationHint"]
        : Loc["TmModelingViewSelector_NoViewpoints"];

    private async Task HandleNotationChangedAsync(ChangeEventArgs args)
    {
        var notation = args.Value?.ToString();
        if (string.IsNullOrWhiteSpace(notation))
        {
            notation = null;
        }

        _currentNotationOverride = notation;
        _currentViewpointOverride = null;
        await OnNotationChanged.InvokeAsync(notation);
    }

    private async Task HandleViewpointChangedAsync(ChangeEventArgs args)
    {
        var viewpoint = args.Value?.ToString();
        if (string.IsNullOrWhiteSpace(viewpoint))
        {
            viewpoint = null;
        }

        _currentViewpointOverride = viewpoint;
        await OnViewpointChanged.InvokeAsync(viewpoint);
    }

    private string GetViewpointLabel(string viewpointKey)
        => viewpointKey switch
        {
            "application" => Loc["TmModelingViewSelector_Viewpoint_Application"],
            "business" => Loc["TmModelingViewSelector_Viewpoint_Business"],
            "default" => Loc["TmModelingViewSelector_Viewpoint_Default"],
            "layered" => Loc["TmModelingViewSelector_Viewpoint_Layered"],
            "operations" => Loc["TmModelingViewSelector_Viewpoint_Operations"],
            "overview" => Loc["TmModelingViewSelector_Viewpoint_Overview"],
            "process" => Loc["TmModelingViewSelector_Viewpoint_Process"],
            "technology" => Loc["TmModelingViewSelector_Viewpoint_Technology"],
            _ => viewpointKey
        };

    private sealed record NotationOption(string Key, string Label);

    private sealed record ViewpointOption(string Key, string Label);
}
