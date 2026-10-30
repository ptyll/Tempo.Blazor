using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>Shows metadata and reload controls for the modeling data source.</summary>
public partial class TmModelingSourcePanel
{
    private bool _localLoadPending;

    /// <summary>Metadata describing the currently loaded modeling source.</summary>
    [Parameter] public ModelingMetadataDto? Metadata { get; set; }

    /// <summary>Whether a model load is currently in progress.</summary>
    [Parameter] public bool IsLoading { get; set; }

    /// <summary>Callback raised when the user requests loading the model again.</summary>
    [Parameter] public EventCallback OnLoadRequested { get; set; }

    /// <summary>Additional CSS class applied to the panel root.</summary>
    [Parameter] public string? Class { get; set; }

    private bool EffectiveIsLoading => IsLoading || _localLoadPending;

    private string RootClass => string.Join(" ", new[] { "tm-modeling-source-panel", Class }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private string FreshnessState => Metadata is null
        ? "unknown"
        : Metadata.IsFresh ? "fresh" : "stale";

    private string FreshnessLabel => Metadata is null
        ? Loc["TmModelingSourcePanel_Unknown"]
        : Metadata.IsFresh ? Loc["TmModelingSourcePanel_Fresh"] : Loc["TmModelingSourcePanel_Stale"];

    /// <summary>Badge color variant for the freshness indicator (success when fresh, warning when stale).</summary>
    private BadgeVariant FreshnessBadgeVariant => Metadata?.IsFresh == true
        ? BadgeVariant.Success
        : BadgeVariant.Warning;

    /// <summary>Badge icon: a check for fresh metadata, a warning triangle for stale metadata.</summary>
    private string? FreshnessIcon => Metadata?.IsFresh == true
        ? IconNames.CheckCircle
        : IconNames.AlertTriangle;

    /// <summary>Refresh icon on the load button; suppressed while the loading spinner replaces it.</summary>
    private string? LoadButtonIcon => EffectiveIsLoading ? null : IconNames.Refresh;

    private string DisplaySourceSystem => string.IsNullOrWhiteSpace(Metadata?.SourceSystem)
        ? Loc["TmModelingSourcePanel_Unknown"]
        : Metadata.SourceSystem;

    private string DisplaySourceVersion => string.IsNullOrWhiteSpace(Metadata?.SourceVersion)
        ? Loc["TmModelingSourcePanel_Unknown"]
        : Metadata.SourceVersion;

    private string DisplayLoadedAt => Metadata is null || Metadata.LoadedAt == default
        ? Loc["TmModelingSourcePanel_Unknown"]
        : Metadata.LoadedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (!IsLoading)
        {
            _localLoadPending = false;
        }
    }

    private async Task HandleLoadRequestedAsync()
    {
        if (EffectiveIsLoading)
        {
            return;
        }

        _localLoadPending = true;
        await OnLoadRequested.InvokeAsync();
    }
}
