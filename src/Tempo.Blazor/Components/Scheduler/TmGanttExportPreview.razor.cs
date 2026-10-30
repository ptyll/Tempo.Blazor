using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The export dialog's built-in live preview: a pure SVG/HTML miniature of the chosen format and
/// options — no JavaScript, no rasterization, no more than a screenful of markup. It reacts to every
/// option the dialog exposes (format, paper, orientation, zoom, the toggles, the Excel columns) and
/// is described to assistive tech by one localized <c>role="img"</c> label. Internal detail of
/// <see cref="TmGanttExportDialog"/>; a host that wants its own preview uses the dialog's
/// <c>PreviewContent</c> slot, which always wins.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class TmGanttExportPreview
{
    /// <summary>The paper sizes the miniature page can imitate, in mm.</summary>
    private static readonly Dictionary<string, (int Width, int Height)> Papers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A4"] = (210, 297),
        ["A3"] = (297, 420),
        ["Letter"] = (216, 279),
    };

    /// <summary>Bar count cap: the preview never draws more nodes than fit a card.</summary>
    internal const int MaxVisibleBars = 40;

    /// <summary>Row cap of the Excel table.</summary>
    internal const int MaxRows = 20;

    /// <summary>The workbook's default column set, shared with <c>GanttXlsxExporter</c>.</summary>
    internal static readonly GanttColumnKey[] DefaultColumns =
    [
        GanttColumnKey.Title, GanttColumnKey.Start, GanttColumnKey.End, GanttColumnKey.Progress,
        GanttColumnKey.Status, GanttColumnKey.Priority, GanttColumnKey.Duration, GanttColumnKey.Assignees,
    ];

    private string? _signature;
    private PreviewModel _model = PreviewModel.Empty;

    /// <summary>Visible tasks to sketch.</summary>
    [Parameter] public IReadOnlyList<TmWorkItem> Tasks { get; set; } = [];

    /// <summary>Dependencies between the visible tasks (drawn as short connectors).</summary>
    [Parameter] public IReadOnlyList<GanttDependency> Dependencies { get; set; } = [];

    /// <summary>Ids of the tasks on the critical path; painted only when the option asks for it.</summary>
    [Parameter] public IReadOnlyList<string>? CriticalPathIds { get; set; }

    /// <summary>The dialog's current selection: format, paper, orientation, zoom, toggles, columns.</summary>
    [Parameter] public GanttExportOptions Options { get; set; } = new(GanttExportFormat.Pdf);

    /// <summary>The resolved layout of the dialog; a fullscreen mobile dialog collapses the preview into a <c>details</c>.</summary>
    [Parameter] public TmLayoutMode Layout { get; set; } = TmLayoutMode.Desktop;

    private bool IsMobile => Layout == TmLayoutMode.Mobile;

    private bool IsImage => Options.Format is GanttExportFormat.Pdf or GanttExportFormat.Png;

    private int Zoom => Options.ZoomLevel ?? 100;

    private (int Width, int Height) Paper => Papers.GetValueOrDefault(Options.PaperSize ?? "A4", (210, 297));

    private string AspectRatio
    {
        get
        {
            var (width, height) = Paper;
            return Options.Landscape ? $"{height}/{width}" : $"{width}/{height}";
        }
    }

    /// <summary>The localized one-line description the <c>role="img"</c> root announces.</summary>
    private string AriaSummary
    {
        get
        {
            var parts = new List<string>();
            if (IsImage)
            {
                var orientation = Options.Landscape ? Loc["TmGantt_Landscape"] : Loc["TmGantt_Portrait"];
                parts.Add($"{Options.PaperSize ?? "A4"} {orientation}");
            }

            parts.Add(Loc.Plural("TmGantt_ExportPreviewTasks", Model.BarCount));
            if (Options.IncludeCriticalPath) parts.Add(Loc["TmGantt_ExportPreviewCriticalPath"]);
            if (Options.IncludeToday) parts.Add(Loc["TmGantt_ExportPreviewToday"]);
            if (Options.IncludeWorkload) parts.Add(Loc["TmGantt_ExportPreviewWorkload"]);

            return Loc["TmGantt_ExportPreviewAria", string.Join(", ", parts)];
        }
    }

    /// <summary>The model, memoized by a signature of the options and the data size it renders from.</summary>
    private PreviewModel Model
    {
        get
        {
            var signature = BuildSignature();
            if (signature != _signature)
            {
                _signature = signature;
                _model = PreviewModel.Build(Tasks, Dependencies, CriticalPathIds, Options);
            }

            return _model;
        }
    }

    private string BuildSignature() => string.Join('\u001f',
        (int)Options.Format,
        Options.PaperSize,
        Options.Landscape,
        Options.ZoomLevel,
        Options.IncludeCriticalPath,
        Options.IncludeToday,
        Options.IncludeWorkload,
        string.Join(',', Options.Columns ?? []),
        Tasks.Count,
        Tasks.Count > 0 ? Tasks[0].Id : string.Empty,
        Tasks.Count > 0 ? Tasks[^1].End.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty,
        Dependencies.Count,
        CriticalPathIds?.Count ?? 0,
        Layout);

    /// <summary>Everything the markup needs, precomputed once per option change.</summary>
    internal sealed record PreviewModel
    {
        internal static readonly PreviewModel Empty = new();

        /// <summary>The sketched bars, capped to <see cref="MaxVisibleBars"/>.</summary>
        public IReadOnlyList<BarModel> Bars { get; init; } = [];

        /// <summary>How many tasks were cut by the bar cap.</summary>
        public int SkippedBars { get; init; }

        public int BarCount => Bars.Count + SkippedBars;

        /// <summary>The sketch's time range (axis labels); default(DateTime) when there is nothing to draw.</summary>
        public DateTime RangeStart { get; init; }

        public DateTime RangeEnd { get; init; }

        /// <summary>Horizontal position of the today line in percent; null when outside the range or off.</summary>
        public double? TodayPercent { get; init; }

        public HashSet<string> Critical { get; init; } = new(StringComparer.Ordinal);

        /// <summary>Dependency connectors in percent coordinates of the sketch.</summary>
        public IReadOnlyList<ConnectorModel> Connectors { get; init; } = [];

        /// <summary>Excel: the columns the table shows.</summary>
        public IReadOnlyList<GanttColumnKey> Columns { get; init; } = [];

        /// <summary>Excel: the first rows, capped to <see cref="MaxRows"/>.</summary>
        public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = [];

        /// <summary>Excel: how many further rows the workbook carries.</summary>
        public int HiddenRows { get; init; }

        /// <summary>XML: the summary counts.</summary>
        public int TaskCount { get; init; }

        public int DependencyCount { get; init; }

        public int MilestoneCount { get; init; }

        internal static PreviewModel Build(
            IReadOnlyList<TmWorkItem> tasks,
            IReadOnlyList<GanttDependency> dependencies,
            IReadOnlyList<string>? criticalPathIds,
            GanttExportOptions options)
        {
            if (tasks.Count == 0)
            {
                return Empty;
            }

            var critical = options.IncludeCriticalPath
                ? new HashSet<string>(criticalPathIds ?? [], StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            if (options.Format is GanttExportFormat.Xlsx)
            {
                var columns = options.Columns is { Count: > 0 } chosen ? chosen : DefaultColumns;
                var rows = tasks
                    .Take(MaxRows)
                    .Select(task => (IReadOnlyList<string>)columns.Select(c => GanttColumnValues.Format(task, c)).ToArray())
                    .ToList();
                return new PreviewModel
                {
                    Columns = columns,
                    Rows = rows,
                    HiddenRows = Math.Max(0, tasks.Count - MaxRows),
                    TaskCount = tasks.Count,
                    DependencyCount = dependencies.Count,
                    MilestoneCount = tasks.Count(t => t.IsMilestone),
                };
            }

            if (options.Format == GanttExportFormat.Xml)
            {
                return new PreviewModel
                {
                    TaskCount = tasks.Count,
                    DependencyCount = dependencies.Count,
                    MilestoneCount = tasks.Count(t => t.IsMilestone),
                };
            }

            // PDF / PNG: the miniature page.
            var rangeStart = tasks.Min(t => t.ScheduledStart());
            var rangeEnd = tasks.Max(t => t.ScheduledEnd());
            if (rangeStart == rangeEnd) rangeEnd = rangeEnd.AddDays(1);

            var visible = tasks.Take(MaxVisibleBars).ToList();
            var bars = visible.Select(task =>
            {
                var (left, width) = GanttHelper.CalculateBarPosition(
                    task.ScheduledStart(), task.ScheduledEnd(), rangeStart, rangeEnd, 100);
                return new BarModel(task, left, Math.Max(1, width)) { IsCritical = critical.Contains(task.Id) };
            }).ToList();

            var byId = visible.ToDictionary(t => t.Id, StringComparer.Ordinal);
            var connectors = dependencies
                .Where(d => byId.ContainsKey(d.FromId) && byId.ContainsKey(d.ToId))
                .Select(d =>
                {
                    var (fromLeft, fromWidth) = GanttHelper.CalculateBarPosition(
                        byId[d.FromId].ScheduledStart(), byId[d.FromId].ScheduledEnd(), rangeStart, rangeEnd, 100);
                    var (toLeft, _) = GanttHelper.CalculateBarPosition(
                        byId[d.ToId].ScheduledStart(), byId[d.ToId].ScheduledEnd(), rangeStart, rangeEnd, 100);
                    var tail = (GanttDependencyType)d.Type switch
                    {
                        GanttDependencyType.StartToStart => fromLeft,
                        GanttDependencyType.StartToFinish => fromLeft,
                        _ => fromLeft + fromWidth,
                    };
                    return new ConnectorModel(tail, toLeft);
                })
                .ToList();

            // The sketch is decorative: a today outside the range pins the line to the page edge
            // instead of dropping it, so a turned-on option always shows its subject.
            var days = (rangeEnd - rangeStart).TotalDays;
            var todayOffset = options.IncludeToday
                ? GanttHelper.GetTodayOffset(rangeStart, 100.0 / days, DateTime.Today)
                : 0;
            double? todayPercent = options.IncludeToday ? Math.Clamp(todayOffset, 0, 100) : null;

            return new PreviewModel
            {
                Bars = bars,
                SkippedBars = Math.Max(0, tasks.Count - visible.Count),
                TodayPercent = todayPercent,
                Critical = critical,
                Connectors = connectors,
                RangeStart = rangeStart,
                RangeEnd = rangeEnd,
                TaskCount = tasks.Count,
                DependencyCount = dependencies.Count,
                MilestoneCount = tasks.Count(t => t.IsMilestone),
            };
        }
    }

    /// <summary>One sketched bar: the task plus its percent geometry on the miniature page.</summary>
    internal sealed record BarModel(TmWorkItem Task, double LeftPercent, double WidthPercent)
    {
        public bool IsCritical { get; init; }
    }

    /// <summary>One dependency connector between two bars, in percent coordinates.</summary>
    internal sealed record ConnectorModel(double FromPercent, double ToPercent);
}
