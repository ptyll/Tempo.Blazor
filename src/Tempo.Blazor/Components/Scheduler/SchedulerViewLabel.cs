namespace Tempo.Blazor.Components.Scheduler;

using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;

/// <summary>The localized name of a scheduler view, shared by the toolbar and the body label.</summary>
internal static class SchedulerViewLabel
{
    /// <summary>The view's display name in the current culture.</summary>
    public static string Of(ITmLocalizer loc, TmScheduleViewType view) => view switch
    {
        TmScheduleViewType.Day => loc["TmScheduler_Day"],
        TmScheduleViewType.Week => loc["TmScheduler_Week"],
        TmScheduleViewType.Month => loc["TmScheduler_Month"],
        TmScheduleViewType.Agenda => loc["TmScheduler_Agenda"],
        TmScheduleViewType.Timeline => loc["TmScheduler_Timeline"],
        _ => view.ToString()
    };
}
