using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// TmGantt redesign, phase 3 (internal split into TmGanttToolbar / TmGanttTree / TmGanttTimeline).
/// The split is a pure refactor: the rendered DOM - element order, classes, <c>data-testid</c>s,
/// attributes, text - must stay byte-identical (modulo the CSS-isolation scope attributes, which are
/// stripped because every extracted component owns a scope of its own now).
/// <para>
/// The snapshots in <c>__snapshots__/</c> were recorded from the monolithic component BEFORE the split.
/// Set <c>TM_UPDATE_SNAPSHOTS=1</c> to re-record them deliberately (a redesign phase that changes the
/// markup on purpose); never to make a refactor green.
/// </para>
/// Culture, clock and zone are pinned: CI runs Linux / UTC / invariant culture.
/// </summary>
public class TmGanttMarkupSnapshotTests : LocalizationTestBase
{
    private static readonly Regex ScopeAttribute = new(@" b-[a-z0-9]{10}(?:=""[^""]*"")?", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex HashCodeName = new(@"(taskloc|density|theme)-\d+", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex FrameworkComment = new(@"<!--!?-->", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex ElementReference = new(@"blazor:elementReference=""[0-9a-f-]*""", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex EventHandlerId = new(@"blazor:on([a-z]+)=""\d+""", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex RandomElementId = new(@"\bid=""[0-9a-f]{8}""", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    // TmDecimalInput (the task panel hour fields) generates a "tm-decimal-<guid>" id per instance; the portfolio card body id carries a per-instance token.
    private static readonly Regex GeneratedGuidId = new(@"(tm-decimal-|gantt-portfolio-)[0-9a-f]{8,32}", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    // Core toolbar items carry per-instance GUID ids (data-tm-toolbar-group, data-tm-toolbar-item).
    private static readonly Regex ToolbarId = new(@"(data-tm-toolbar-(?:group|item))=""[a-z-]*[0-9a-f]{32}""", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex AvatarHue = new(@"hsl\(\d+, 60%, 50%\)", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex EdgeSpace = new(@"(?<=>) | (?=<)", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex TagBoundary = new(@">(?=<)", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private static string SnapshotDir =>
        Path.Combine(ThemeCss.RepositoryRoot().FullName, "tests", "Tempo.Blazor.Tests", "Components", "Scheduler", "__snapshots__");

    private void Pin()
    {
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2024, 6, 12, 10, 0, 0, TimeSpan.Zero)));
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(TimeZoneInfo.Utc));
    }

    private static List<TmWorkItem> Tasks() =>
    [
        new()
        {
            Id = "1", Title = "Project", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30),
            Priority = TmWorkItemPriority.High, Status = TmWorkItemStatus.InProgress, PercentComplete = 40,
            Comments = [new TmCommentEntry { Id = "c1", ThreadId = "1", Body = "Hello", CreatedAt = new DateTimeOffset(2024, 6, 2, 9, 0, 0, TimeSpan.Zero) }],
            Attachments = [new TmAttachment { Id = "a1", FileName = "spec.pdf" }],
        },
        new()
        {
            Id = "2", Title = "Design", ParentId = "1", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 10),
            Status = TmWorkItemStatus.Done, PercentComplete = 100, Color = "#336699",
            Assignees = [new TmWorkItemAssignee { Id = "u1", Name = "Alice" }, new TmWorkItemAssignee { Id = "v1", Name = "Dev pool", IsVirtual = true }],
            CustomFields = new Dictionary<string, string?> { ["cf1"] = "Alpha" },
        },
        new()
        {
            Id = "3", Title = "Development", ParentId = "1", Start = new DateTime(2024, 6, 11), End = new DateTime(2024, 6, 25),
            Priority = TmWorkItemPriority.Highest, PercentComplete = 50, DueDate = new DateTime(2024, 6, 20),
            Assignees = [new TmWorkItemAssignee { Id = "u2", Name = "Bob" }],
        },
        new() { Id = "4", Title = "Launch", IsMilestone = true, Start = new DateTime(2024, 6, 30), End = new DateTime(2024, 6, 30) },
        new() { Id = "5", Title = "Overdue", Start = new DateTime(2024, 5, 20), End = new DateTime(2024, 6, 5), Priority = TmWorkItemPriority.Low },
    ];

    private static List<GanttDependency> Dependencies() =>
    [
        new() { Id = "d1", FromId = "2", ToId = "3" },
        new() { Id = "d2", FromId = "3", ToId = "4", LagDays = 2, DepType = GanttDependencyType.StartToStart },
    ];

    private static string Normalise(string markup)
    {
        var text = ScopeAttribute.Replace(markup, string.Empty);
        text = HashCodeName.Replace(text, "$1-N");
        text = FrameworkComment.Replace(text, string.Empty);
        // Random per render (element references, generated ids) or a render-frame counter (handler ids):
        // neither is part of the contract, and the counter legitimately moves when the tree is split.
        text = ElementReference.Replace(text, "blazor:elementReference=\"R\"");
        text = EventHandlerId.Replace(text, "blazor:on$1=\"H\"");
        text = RandomElementId.Replace(text, "id=\"G\"");
        text = GeneratedGuidId.Replace(text, "$1G");
        text = ToolbarId.Replace(text, "$1=\"T\"");
        // The avatar hue is string.GetHashCode(), randomised per process.
        text = AvatarHue.Replace(text, "hsl(H, 60%, 50%)");
        // Whitespace is insignificant to rendering: collapse runs and trim text edges, so re-indenting a
        // moved block is not a "change", while any element, attribute, class or word still is. Tags go on
        // their own line so a failure points at the element.
        text = Whitespace.Replace(text, " ");
        text = EdgeSpace.Replace(text, string.Empty);
        return TagBoundary.Replace(text, ">\n").Trim() + "\n";
    }

    private static void AssertMatchesSnapshot(string name, string markup)
    {
        var path = Path.Combine(SnapshotDir, name + ".html");
        var actual = Normalise(markup);

        if (Environment.GetEnvironmentVariable("TM_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(SnapshotDir);
            File.WriteAllText(path, actual);
        }

        File.Exists(path).Should().BeTrue($"snapshot {name}.html must be recorded (TM_UPDATE_SNAPSHOTS=1)");
        var expected = File.ReadAllText(path).Replace("\r\n", "\n");
        actual.Should().Be(expected, "the internal split must not change the rendered DOM ({0})", name);
    }

    [Fact]
    public void TmGantt_Markup_Snapshot_Default()
    {
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        Pin();

        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, Tasks())
            .Add(c => c.DependencyItems, Dependencies())
            .Add(c => c.CustomFields, [new TmCustomFieldDefinition { Id = "cf1", Name = "Phase" }]));

        AssertMatchesSnapshot("Default", cut.Markup);
    }

    [Fact]
    public void TmGantt_Markup_Snapshot_WithTaskPanel()
    {
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        Pin();
        var tasks = Tasks();

        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, tasks)
            .Add(c => c.DependencyItems, Dependencies())
            .Add(c => c.SelectedTask, tasks[1])
            .Add(c => c.ShowTaskPanel, true));

        AssertMatchesSnapshot("WithTaskPanel", cut.Markup);
    }

    [Fact]
    public void TmGantt_Markup_Snapshot_CompactDensity()
    {
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        Pin();

        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, Tasks())
            .Add(c => c.DependencyItems, Dependencies())
            .Add(c => c.ViewSettings, new GanttViewSettings { ViewDensity = GanttViewDensity.Compact, ShowProgressLabel = true }));

        AssertMatchesSnapshot("CompactDensity", cut.Markup);
    }

    [Fact]
    public void TmGantt_Markup_Snapshot_AllOptions()
    {
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        Pin();
        var tasks = Tasks();

        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, tasks)
            .Add(c => c.DependencyItems, Dependencies())
            .Add(c => c.AllowBulkSelect, true)
            .Add(c => c.AllowDependencyCreation, true)
            .Add(c => c.ShowOverdueHighlight, true)
            .Add(c => c.ShowCriticalPath, true)
            .Add(c => c.ActiveTimerTaskId, "3")
            .Add(c => c.SelectedTask, tasks[2])
            .Add(c => c.ShowWorkloadPanel, true)
            .Add(c => c.Baselines, [new GanttBaseline { Id = "b1", Name = "Base", Tasks = [new GanttBaselineTask("3", new DateTime(2024, 6, 8), new DateTime(2024, 6, 20))] }])
            .Add(c => c.ActiveBaselineId, "b1")
            .Add(c => c.ZoomPreset, GanttZoomPreset.Days));

        AssertMatchesSnapshot("AllOptions", cut.Markup);
    }

    [Fact]
    public void TmGantt_Markup_Snapshot_ViewSettingsMenuOpen()
    {
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        Pin();

        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, Tasks())
            .Add(c => c.NotificationSettings, new TmNotificationPreferences()));
        cut.Find("[data-testid='gantt-view-settings-btn']").Click();

        AssertMatchesSnapshot("ViewSettingsMenuOpen", cut.Markup);
    }

    [Fact]
    public void TmGantt_Markup_Snapshot_ListView_With_Sidebar_And_Filter()
    {
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        Pin();

        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, Tasks())
            .Add(c => c.View, GanttView.List));
        cut.Find("[data-testid='gantt-filter-btn']").Click();
        cut.Find("[data-testid='sidebar-btn-tasks']").Click();

        AssertMatchesSnapshot("ListViewSidebarFilter", cut.Markup);
    }
}
