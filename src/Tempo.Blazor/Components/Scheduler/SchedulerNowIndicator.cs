using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>Which way the current-time line runs.</summary>
internal enum SchedulerNowAxis
{
    /// <summary>A vertical time grid: the line is horizontal and offset from the top.</summary>
    Vertical,

    /// <summary>A horizontal timeline: the line is vertical and offset from the left.</summary>
    Horizontal
}

/// <summary>
/// The current-time line of one column or row. It computes "today" from the injected clock and the
/// display zone itself, so a tick that crosses midnight hides yesterday's line without asking the
/// grid. A tick calls <see cref="StateHasChanged"/> on this component only.
/// </summary>
internal sealed class SchedulerNowIndicator : ComponentBase, IDisposable
{
    private bool _disposed;

    /// <summary>The column's date, or the timeline's displayed day.</summary>
    [Parameter] public DateOnly Date { get; set; }

    /// <summary>First visible minute. Defaults to midnight.</summary>
    [Parameter] public int RangeStartMinutes { get; set; }

    /// <summary>Length of the visible range in minutes. Defaults to a full day.</summary>
    [Parameter] public int RangeMinutes { get; set; } = 24 * 60;

    /// <summary>Display zone. Null uses the clock's local zone.</summary>
    [Parameter] public TimeZoneInfo? TimeZone { get; set; }

    /// <summary>Vertical for the time grid, horizontal for the timeline.</summary>
    [Parameter] public SchedulerNowAxis Axis { get; set; } = SchedulerNowAxis.Vertical;

    /// <summary>When false the line is hidden from assistive tech. The first row of a timeline is the named one.</summary>
    [Parameter] public bool Labelled { get; set; } = true;

    /// <summary>The grid's ticker. Cascaded with <c>IsFixed</c>, so a tick does not re-render the grid.</summary>
    [CascadingParameter] public SchedulerNowTicker? Ticker { get; set; }

    /// <summary>The grid drops this line from its tick list when it is disposed.</summary>
    internal Action<SchedulerNowIndicator>? OnDisposed { get; set; }

    /// <summary>How many times this line itself rendered. A test reads it; the grid's count must not move.</summary>
    internal int RenderCount { get; private set; }

    [Inject] private ITmLocalizer Loc { get; set; } = default!;

    [Inject] private IServiceProvider Services { get; set; } = default!;

    private TimeProvider? _clock;

    private TimeProvider Clock
        => _clock ??= Services.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        // The ticker starts here, after the grid has captured this instance, so the first callback
        // already has someone to notify.
        Ticker?.Start();
    }

    /// <inheritdoc />
    protected override void OnParametersSet() => RenderCount++;

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var zone = TimeZone ?? Clock.LocalTimeZone;
        var now = TimeZoneInfo.ConvertTime(Clock.GetUtcNow(), zone);
        if (DateOnly.FromDateTime(now.DateTime) != Date) return;

        var offset = now.Hour * 60 + now.Minute - RangeStartMinutes;
        if (offset < 0 || offset > RangeMinutes) return;

        var horizontal = Axis == SchedulerNowAxis.Horizontal;
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", horizontal ? "tm-scheduler-timeline-now" : "tm-scheduler-timegrid-now");
        builder.AddAttribute(2, "style", $"--tm-scheduler-now: {offset.ToString(CultureInfo.InvariantCulture)};");
        if (Labelled)
        {
            builder.AddAttribute(3, "role", "img");
            builder.AddAttribute(4, "aria-label",
                string.Format(Loc["TmScheduler_NowIndicator"], now.ToString("t", CultureInfo.CurrentUICulture)));
        }
        else
        {
            builder.AddAttribute(3, "aria-hidden", "true");
        }

        builder.CloseElement();
    }

    /// <summary>The ticker calls this. The render stays on this component.</summary>
    public void Tick()
    {
        if (_disposed) return;
        _ = InvokeAsync(StateHasChanged);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        OnDisposed?.Invoke(this);
    }
}
