using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;

namespace Tempo.Blazor.Demo.Api.Data;

/// <summary>
/// In-memory Gantt demo store covering every TmGantt feature:
/// task hierarchy, all statuses/priorities, milestones, deadlines, assignees (including virtual),
/// budget &amp; cost, time logs, comments, attachments, custom fields, all 4 dependency types,
/// baselines, history, resource calendars, reports, and working schedule.
/// </summary>
public class MockGanttStore
{
    private readonly List<TmWorkItem>             _tasks;
    private readonly List<GanttDependency>        _dependencies;
    private readonly List<GanttBaseline>          _baselines;
    private readonly List<TmCustomFieldDefinition> _customFields;
    private readonly List<TmActivityEntry>        _history;
    private readonly List<GanttResourceCalendar>  _resourceCalendars;
    private readonly List<GanttReport>            _reports;
    private readonly WorkingSchedule              _workingSchedule;
    private readonly List<TmWorkItemAssignee>          _assignees;

    public MockGanttStore()
    {
        var today = DateTime.Today;

        // ── Assignees ─────────────────────────────────────────────
        _assignees =
        [
            new() { Id = "a1", Name = "Alice Johnson",   AvatarUrl = "https://i.pravatar.cc/40?img=1",  HourlyRate = 120m },
            new() { Id = "a2", Name = "Bob Smith",       AvatarUrl = "https://i.pravatar.cc/40?img=3",  HourlyRate = 95m  },
            new() { Id = "a3", Name = "Carol White",     AvatarUrl = "https://i.pravatar.cc/40?img=5",  HourlyRate = 150m },
            new() { Id = "a4", Name = "David Lee",       AvatarUrl = "https://i.pravatar.cc/40?img=7",  HourlyRate = 80m  },
            new() { Id = "v1", Name = "UX Contractor",   AvatarUrl = null, HourlyRate = 110m, IsVirtual = true },
            new() { Id = "v2", Name = "DevOps Resource", AvatarUrl = null, HourlyRate = 130m, IsVirtual = true },
        ];

        TmWorkItemAssignee A(string id) => _assignees.First(x => x.Id == id);

        // ── Custom Fields ─────────────────────────────────────────
        _customFields =
        [
            new() { Id = "cf1", Name = "Sprint",       Type = TmCustomFieldType.List,      Options = ["Sprint 1", "Sprint 2", "Sprint 3", "Sprint 4"], AppliesToEntityTypes = ["work-item"] },
            new() { Id = "cf2", Name = "Story Points", Type = TmCustomFieldType.Number,    AppliesToEntityTypes = ["work-item"] },
            new() { Id = "cf3", Name = "Epic",         Type = TmCustomFieldType.Text,      AppliesToEntityTypes = ["work-item"] },
            new() { Id = "cf4", Name = "Billable",     Type = TmCustomFieldType.Checkbox,  AppliesToEntityTypes = ["work-item"] },
            new() { Id = "cf5", Name = "Team",         Type = TmCustomFieldType.Labels,    Options = ["Frontend", "Backend", "QA", "DevOps", "Design"], AppliesToEntityTypes = ["work-item"] },
            new() { Id = "cf6", Name = "Review URL",   Type = TmCustomFieldType.Text,      AppliesToEntityTypes = ["work-item"] },
        ];

        // ── Tasks ─────────────────────────────────────────────────
        _tasks =
        [
            // ── Root project group ─────────────────────────────
            new()
            {
                Id = "1", Title = "Project Alpha", ParentId = null,
                Start = today.AddDays(-21), End = today.AddDays(42),
                PercentComplete = 38, Status = TmWorkItemStatus.InProgress,
                Priority = TmWorkItemPriority.Highest, Color = "#6366f1",
                UseManualDates = false,
                Description = "Full product delivery — Phase 1 through Go-Live.",
                CustomFields = new() { ["cf3"] = "Alpha Platform", ["cf4"] = "true" },
            },

            // ── Planning Phase ─────────────────────────────────
            new()
            {
                Id = "2", Title = "Planning Phase", ParentId = "1",
                Start = today.AddDays(-21), End = today.AddDays(-9),
                PercentComplete = 100, Status = TmWorkItemStatus.Done,
                Priority = TmWorkItemPriority.High, Color = "#10b981",
                CustomFields = new() { ["cf1"] = "Sprint 1", ["cf5"] = "Backend" },
            },
            new()
            {
                Id = "3", Title = "Project Charter", ParentId = "2",
                Start = today.AddDays(-21), End = today.AddDays(-18),
                PercentComplete = 100, Status = TmWorkItemStatus.Done,
                Priority = TmWorkItemPriority.High,
                Assignees = [A("a1"), A("a3")],
                BudgetHours = 16, ActualCost = 1800m,
                EstimationHours = 16, LoggedHours = 14.5,
                CustomFields = new() { ["cf1"] = "Sprint 1", ["cf2"] = "5", ["cf4"] = "true" },
                TimeLog =
                [
                    new() { Id = "tl1", TaskId = "3", AssigneeId = "a1",
                            StartedAt = today.AddDays(-21).AddHours(9),  StoppedAt = today.AddDays(-21).AddHours(13), Notes = "Initial kickoff work" },
                    new() { Id = "tl2", TaskId = "3", AssigneeId = "a3",
                            StartedAt = today.AddDays(-20).AddHours(10), StoppedAt = today.AddDays(-20).AddHours(14.5), Notes = "Stakeholder docs" },
                ],
                Comments =
                [
                    new()
                    {
                        Id = "cm1",
                        ThreadId = "3",
                        Author = new TmUserRef { Id = "a1", DisplayName = "Alice Johnson", AvatarUrl = "https://i.pravatar.cc/40?img=1" },
                        Body = "Charter approved by board.",
                        BodyFormat = TmCommentBodyFormat.PlainText,
                        CreatedAt = today.AddDays(-18),
                        Metadata = new() { ["TaskId"] = "3" },
                    },
                ],
                Attachments =
                [
                    new() { Id = "at1", FileName = "project-charter-v1.docx", ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                            Url = "/files/project-charter-v1.docx", UploadedAt = today.AddDays(-20) },
                ],
            },
            new()
            {
                Id = "4", Title = "Stakeholder Analysis", ParentId = "2",
                Start = today.AddDays(-18), End = today.AddDays(-14),
                PercentComplete = 100, Status = TmWorkItemStatus.Done,
                Priority = TmWorkItemPriority.Medium,
                Assignees = [A("a2"), A("v1")],
                BudgetHours = 24, ActualCost = 2100m,
                EstimationHours = 24, LoggedHours = 22,
                CustomFields = new() { ["cf1"] = "Sprint 1", ["cf2"] = "8", ["cf5"] = "Design" },
            },
            new()
            {
                Id = "5", Title = "Design Phase", ParentId = "2",
                Start = today.AddDays(-11), End = today.AddDays(-9),
                PercentComplete = 100, Status = TmWorkItemStatus.Done,
                Priority = TmWorkItemPriority.Highest, IsMilestone = true,
                Assignees = [A("a1"), A("a2"), A("a3")],
                CustomFields = new() { ["cf1"] = "Sprint 1" },
            },

            // ── Design Phase ───────────────────────────────────
            new()
            {
                Id = "6", Title = "UI/UX Design", ParentId = "1",
                Start = today.AddDays(-9), End = today.AddDays(6),
                PercentComplete = 65, Status = TmWorkItemStatus.InProgress,
                Priority = TmWorkItemPriority.High, Color = "#f59e0b",
                CustomFields = new() { ["cf1"] = "Sprint 2", ["cf5"] = "Design,Frontend" },
            },
            new()
            {
                Id = "7", Title = "UI/UX Mockups", ParentId = "6",
                Start = today.AddDays(-9), End = today.AddDays(-1),
                PercentComplete = 100, Status = TmWorkItemStatus.Done,
                Priority = TmWorkItemPriority.High,
                Assignees = [A("v1"), A("a3")],
                BudgetHours = 40, ActualCost = 4200m,
                EstimationHours = 40, LoggedHours = 38,
                CustomFields = new() { ["cf1"] = "Sprint 2", ["cf2"] = "13", ["cf5"] = "Design", ["cf6"] = "https://figma.com/project-alpha-mockups" },
                Attachments =
                [
                    new() { Id = "at2", FileName = "alpha-mockups-v2.fig", ContentType = "application/octet-stream",
                            Url = "/files/alpha-mockups-v2.fig", UploadedAt = today.AddDays(-3) },
                    new() { Id = "at3", FileName = "style-guide.pdf", ContentType = "application/pdf",
                            Url = "/files/style-guide.pdf", UploadedAt = today.AddDays(-2) },
                ],
                Comments =
                [
                    new()
                    {
                        Id = "cm2",
                        ThreadId = "7",
                        Author = new TmUserRef { Id = "a3", DisplayName = "Carol White", AvatarUrl = "https://i.pravatar.cc/40?img=5" },
                        Body = "Mockups approved — ready for handoff.",
                        BodyFormat = TmCommentBodyFormat.PlainText,
                        CreatedAt = today.AddDays(-1),
                        Metadata = new() { ["TaskId"] = "7" },
                    },
                ],
            },
            new()
            {
                Id = "8", Title = "Database Schema Design", ParentId = "6",
                Start = today.AddDays(-7), End = today.AddDays(2),
                PercentComplete = 80, Status = TmWorkItemStatus.InProgress,
                Priority = TmWorkItemPriority.High,
                Assignees = [A("a2")],
                BudgetHours = 20, ActualCost = 950m,
                EstimationHours = 20, LoggedHours = 16,
                DueDate = today.AddDays(2),
                CustomFields = new() { ["cf1"] = "Sprint 2", ["cf2"] = "8", ["cf5"] = "Backend" },
            },
            new()
            {
                Id = "9", Title = "API Contract Definition", ParentId = "6",
                Start = today.AddDays(-5), End = today.AddDays(3),
                PercentComplete = 60, Status = TmWorkItemStatus.InProgress,
                Priority = TmWorkItemPriority.Medium,
                Assignees = [A("a1"), A("a2")],
                BudgetHours = 16, ActualCost = 700m,
                EstimationHours = 16, LoggedHours = 9.5,
                CustomFields = new() { ["cf1"] = "Sprint 2", ["cf2"] = "5", ["cf5"] = "Backend" },
            },
            new()
            {
                Id = "10", Title = "Design Review Sign-off", ParentId = "6",
                Start = today.AddDays(5), End = today.AddDays(6),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.Highest, IsMilestone = true,
                Assignees = [A("a1"), A("a3")],
                CustomFields = new() { ["cf1"] = "Sprint 2" },
            },

            // ── Development Phase ──────────────────────────────
            new()
            {
                Id = "11", Title = "Development Phase", ParentId = "1",
                Start = today.AddDays(5), End = today.AddDays(28),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.Highest, Color = "#ef4444",
                CustomFields = new() { ["cf1"] = "Sprint 3", ["cf5"] = "Backend,Frontend" },
            },
            new()
            {
                Id = "12", Title = "Backend Core Services", ParentId = "11",
                Start = today.AddDays(5), End = today.AddDays(17),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.High,
                Assignees = [A("a2"), A("a4")],
                BudgetHours = 80, ActualCost = 0m,
                EstimationHours = 80, LoggedHours = 0,
                DueDate = today.AddDays(18),
                CustomFields = new() { ["cf1"] = "Sprint 3", ["cf2"] = "21", ["cf5"] = "Backend", ["cf4"] = "true" },
            },
            new()
            {
                Id = "13", Title = "Frontend Components", ParentId = "11",
                Start = today.AddDays(7), End = today.AddDays(21),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.High,
                Assignees = [A("a3"), A("v1")],
                BudgetHours = 72, ActualCost = 0m,
                EstimationHours = 72, LoggedHours = 0,
                CustomFields = new() { ["cf1"] = "Sprint 3", ["cf2"] = "13", ["cf5"] = "Frontend,Design" },
            },
            new()
            {
                Id = "14", Title = "Integration Layer", ParentId = "11",
                Start = today.AddDays(14), End = today.AddDays(24),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.Medium,
                Assignees = [A("a1"), A("a2")],
                BudgetHours = 40, ActualCost = 0m,
                EstimationHours = 40, LoggedHours = 0,
                CustomFields = new() { ["cf1"] = "Sprint 3", ["cf2"] = "8", ["cf5"] = "Backend" },
            },
            new()
            {
                Id = "15", Title = "Feature A – Search & Filter", ParentId = "11",
                Start = today.AddDays(7), End = today.AddDays(20),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.Medium,
                Assignees = [A("a4")],
                BudgetHours = 32, ActualCost = 0m,
                EstimationHours = 32, LoggedHours = 0,
                CustomFields = new() { ["cf1"] = "Sprint 3", ["cf2"] = "8", ["cf5"] = "Frontend,Backend" },
            },
            new()
            {
                Id = "16", Title = "Feature B – Notifications", ParentId = "11",
                Start = today.AddDays(12), End = today.AddDays(28),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.Low,
                Assignees = [A("a4"), A("v2")],
                BudgetHours = 24, ActualCost = 0m,
                EstimationHours = 24, LoggedHours = 0,
                CustomFields = new() { ["cf1"] = "Sprint 4", ["cf2"] = "5", ["cf5"] = "Backend,DevOps" },
            },

            // ── Testing Phase ──────────────────────────────────
            new()
            {
                Id = "17", Title = "Testing Phase", ParentId = "1",
                Start = today.AddDays(26), End = today.AddDays(38),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.High, Color = "#8b5cf6",
                CustomFields = new() { ["cf1"] = "Sprint 4", ["cf5"] = "QA" },
            },
            new()
            {
                Id = "18", Title = "Unit Testing", ParentId = "17",
                Start = today.AddDays(26), End = today.AddDays(30),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.High,
                Assignees = [A("a2"), A("a4")],
                BudgetHours = 24, EstimationHours = 24,
                CustomFields = new() { ["cf1"] = "Sprint 4", ["cf2"] = "5", ["cf5"] = "QA,Backend" },
            },
            new()
            {
                Id = "19", Title = "Integration Testing", ParentId = "17",
                Start = today.AddDays(29), End = today.AddDays(34),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.High,
                Assignees = [A("a1"), A("a4")],
                BudgetHours = 32, EstimationHours = 32,
                CustomFields = new() { ["cf1"] = "Sprint 4", ["cf2"] = "8", ["cf5"] = "QA" },
            },
            new()
            {
                Id = "20", Title = "Performance Testing", ParentId = "17",
                Start = today.AddDays(30), End = today.AddDays(35),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.Medium,
                Assignees = [A("v2")],
                BudgetHours = 16, EstimationHours = 16,
                CustomFields = new() { ["cf1"] = "Sprint 4", ["cf2"] = "5", ["cf5"] = "QA,DevOps" },
            },
            new()
            {
                Id = "21", Title = "User Acceptance Testing", ParentId = "17",
                Start = today.AddDays(34), End = today.AddDays(38),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.Highest,
                Assignees = [A("a1"), A("a3")],
                BudgetHours = 24, EstimationHours = 24,
                DueDate = today.AddDays(38),
                CustomFields = new() { ["cf1"] = "Sprint 4", ["cf2"] = "13", ["cf5"] = "QA" },
            },
            new()
            {
                Id = "22", Title = "QA Sign-off", ParentId = "17",
                Start = today.AddDays(38), End = today.AddDays(38),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.Highest, IsMilestone = true,
                Assignees = [A("a1")],
                CustomFields = new() { ["cf1"] = "Sprint 4" },
            },

            // ── Deployment Phase ───────────────────────────────
            new()
            {
                Id = "23", Title = "Deployment Phase", ParentId = "1",
                Start = today.AddDays(37), End = today.AddDays(42),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.Highest, Color = "#ec4899",
                CustomFields = new() { ["cf1"] = "Sprint 4", ["cf5"] = "DevOps" },
            },
            new()
            {
                Id = "24", Title = "Staging Deployment", ParentId = "23",
                Start = today.AddDays(37), End = today.AddDays(39),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.High,
                Assignees = [A("v2"), A("a4")],
                BudgetHours = 8, EstimationHours = 8,
                CustomFields = new() { ["cf1"] = "Sprint 4", ["cf5"] = "DevOps", ["cf4"] = "true" },
            },
            new()
            {
                Id = "25", Title = "Production Deployment", ParentId = "23",
                Start = today.AddDays(40), End = today.AddDays(41),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.Highest,
                Assignees = [A("v2"), A("a2")],
                BudgetHours = 8, EstimationHours = 8,
                DueDate = today.AddDays(42),
                CustomFields = new() { ["cf1"] = "Sprint 4", ["cf5"] = "DevOps", ["cf4"] = "true" },
            },
            new()
            {
                Id = "26", Title = "Go-Live!", ParentId = "23",
                Start = today.AddDays(42), End = today.AddDays(42),
                PercentComplete = 0, Status = TmWorkItemStatus.Open,
                Priority = TmWorkItemPriority.Highest, IsMilestone = true,
                Assignees = [A("a1"), A("a2"), A("a3")],
                CustomFields = new() { ["cf1"] = "Sprint 4" },
            },

            // ── Extra scenarios ────────────────────────────────
            new()
            {
                Id = "27", Title = "Overdue – Security Audit", ParentId = "1",
                Start = today.AddDays(-8), End = today.AddDays(-1),
                PercentComplete = 45, Status = TmWorkItemStatus.InProgress,
                Priority = TmWorkItemPriority.Highest, Color = "#dc2626",
                Assignees = [A("a1")],
                BudgetHours = 20, ActualCost = 600m, EstimationHours = 20, LoggedHours = 9,
                DueDate = today.AddDays(-2),
                CustomFields = new() { ["cf5"] = "Backend", ["cf2"] = "8", ["cf4"] = "true" },
                TimeLog =
                [
                    new() { Id = "tl3", TaskId = "27", AssigneeId = "a1",
                            StartedAt = today.AddDays(-8).AddHours(9), StoppedAt = today.AddDays(-8).AddHours(13), Notes = "Initial audit scope" },
                    new() { Id = "tl4", TaskId = "27", AssigneeId = "a1",
                            StartedAt = today.AddDays(-6).AddHours(14), StoppedAt = today.AddDays(-6).AddHours(19), Notes = "OWASP checklist review" },
                ],
                Comments =
                [
                    new()
                    {
                        Id = "cm3",
                        ThreadId = "27",
                        Author = new TmUserRef { Id = "a1", DisplayName = "Alice Johnson", AvatarUrl = "https://i.pravatar.cc/40?img=1" },
                        Body = "Blocked on vendor response. Escalating.",
                        BodyFormat = TmCommentBodyFormat.PlainText,
                        CreatedAt = today.AddDays(-3),
                        Metadata = new() { ["TaskId"] = "27" },
                    },
                    new()
                    {
                        Id = "cm4",
                        ThreadId = "27",
                        Author = new TmUserRef { Id = "a2", DisplayName = "Bob Smith", AvatarUrl = "https://i.pravatar.cc/40?img=3" },
                        Body = "Moving deadline out by 3 days, awaiting approval.",
                        BodyFormat = TmCommentBodyFormat.PlainText,
                        CreatedAt = today.AddDays(-1),
                        Metadata = new() { ["TaskId"] = "27" },
                    },
                ],
            },
            new()
            {
                Id = "28", Title = "DevOps Infrastructure Setup", ParentId = "1",
                Start = today, End = today.AddDays(10),
                PercentComplete = 15, Status = TmWorkItemStatus.InProgress,
                Priority = TmWorkItemPriority.High, Color = "#0ea5e9",
                Assignees = [A("v2")],
                BudgetHours = 32, EstimationHours = 32, LoggedHours = 5,
                UseManualDates = true,
                CustomFields = new() { ["cf5"] = "DevOps", ["cf2"] = "13" },
                TimeLog =
                [
                    new() { Id = "tl5", TaskId = "28", AssigneeId = "v2",
                            StartedAt = today.AddHours(9), StoppedAt = today.AddHours(14), Notes = "CI/CD pipeline setup" },
                ],
            },
        ];

        // ── Dependencies (all 4 types) ────────────────────────────
        _dependencies =
        [
            // Planning: FS
            new() { Id = "d1",  FromId = "3",  ToId = "4",  DepType = GanttDependencyType.FinishToStart },
            new() { Id = "d2",  FromId = "4",  ToId = "5",  DepType = GanttDependencyType.FinishToStart },

            // Planning → Design: FS (from milestone)
            new() { Id = "d3",  FromId = "5",  ToId = "7",  DepType = GanttDependencyType.FinishToStart },

            // Design internal: FS
            new() { Id = "d4",  FromId = "7",  ToId = "8",  DepType = GanttDependencyType.FinishToStart },
            new() { Id = "d5",  FromId = "7",  ToId = "9",  DepType = GanttDependencyType.FinishToStart },
            new() { Id = "d6",  FromId = "8",  ToId = "10", DepType = GanttDependencyType.FinishToStart },
            new() { Id = "d7",  FromId = "9",  ToId = "10", DepType = GanttDependencyType.FinishToStart },

            // Design → Development: FS
            new() { Id = "d8",  FromId = "10", ToId = "12", DepType = GanttDependencyType.FinishToStart },
            new() { Id = "d9",  FromId = "10", ToId = "13", DepType = GanttDependencyType.FinishToStart },

            // SS: Backend and Feature A start together
            new() { Id = "d10", FromId = "12", ToId = "15", DepType = GanttDependencyType.StartToStart },

            // FF: Feature B finishes when Frontend Components finish
            new() { Id = "d11", FromId = "13", ToId = "16", DepType = GanttDependencyType.FinishToFinish },

            // Integration depends on Backend and Frontend: FS
            new() { Id = "d12", FromId = "12", ToId = "14", DepType = GanttDependencyType.FinishToStart },
            new() { Id = "d13", FromId = "13", ToId = "14", DepType = GanttDependencyType.FinishToStart },

            // SF: Performance Testing must start before Integration Testing ends
            new() { Id = "d14", FromId = "19", ToId = "20", DepType = GanttDependencyType.StartToFinish },

            // Development → Testing: FS
            new() { Id = "d15", FromId = "14", ToId = "18", DepType = GanttDependencyType.FinishToStart },

            // Testing chain: FS
            new() { Id = "d16", FromId = "18", ToId = "19", DepType = GanttDependencyType.FinishToStart },
            new() { Id = "d17", FromId = "19", ToId = "21", DepType = GanttDependencyType.FinishToStart },
            new() { Id = "d18", FromId = "21", ToId = "22", DepType = GanttDependencyType.FinishToStart },

            // Testing → Deployment: FS
            new() { Id = "d19", FromId = "22", ToId = "24", DepType = GanttDependencyType.FinishToStart },
            new() { Id = "d20", FromId = "24", ToId = "25", DepType = GanttDependencyType.FinishToStart },
            new() { Id = "d21", FromId = "25", ToId = "26", DepType = GanttDependencyType.FinishToStart },
        ];

        // ── Baselines ─────────────────────────────────────────────
        _baselines =
        [
            new()
            {
                Id = "b1", Name = "Initial Plan",
                CreatedAt = today.AddDays(-21).ToUniversalTime(),
                Tasks = _tasks.Where(t => !t.IsMilestone)
                              .Select(t => new GanttBaselineTask(t.Id, t.Start.AddDays(-3), t.End.AddDays(-2)))
                              .ToList(),
            },
            new()
            {
                Id = "b2", Name = "After Sprint 1 Review",
                CreatedAt = today.AddDays(-7).ToUniversalTime(),
                Tasks = _tasks.Where(t => !t.IsMilestone)
                              .Select(t => new GanttBaselineTask(t.Id, t.Start.AddDays(-1), t.End.AddDays(1)))
                              .ToList(),
            },
        ];

        // ── History ───────────────────────────────────────────────
        _history =
        [
            History("h1",  today.AddDays(-21).AddHours(8),    "Alice Johnson", "Created",       "1",  null,         "Project Alpha"),
            History("h2",  today.AddDays(-21).AddHours(8.5),  "Alice Johnson", "Created",       "3",  null,         "Project Charter"),
            History("h3",  today.AddDays(-19).AddHours(10),   "Bob Smith",     "StatusChanged", "3",  "Open",       "InProgress"),
            History("h4",  today.AddDays(-18).AddHours(16),   "Carol White",   "StatusChanged", "3",  "InProgress", "Done"),
            History("h5",  today.AddDays(-18).AddHours(16),   "Alice Johnson", "ProgressSet",   "3",  "80",         "100"),
            History("h6",  today.AddDays(-14).AddHours(9),    "Alice Johnson", "Created",       "6",  null,         "Design Phase"),
            History("h7",  today.AddDays(-9).AddHours(11),    "Carol White",   "StatusChanged", "7",  "Open",       "InProgress"),
            History("h8",  today.AddDays(-7).AddHours(14),    "Bob Smith",     "AssigneeAdded", "8",  null,         "Bob Smith"),
            History("h9",  today.AddDays(-5).AddHours(9),     "Alice Johnson", "DeadlineSet",   "8",  null,         today.AddDays(2).ToString("yyyy-MM-dd")),
            History("h10", today.AddDays(-3).AddHours(10),    "Carol White",   "StatusChanged", "7",  "InProgress", "Done"),
            History("h11", today.AddDays(-3).AddHours(10),    "Carol White",   "ProgressSet",   "7",  "90",         "100"),
            History("h12", today.AddDays(-2).AddHours(11),    "Alice Johnson", "CommentAdded",  "27", null,         "Blocked on vendor response. Escalating."),
            History("h13", today.AddDays(-1).AddHours(16),    "Bob Smith",     "CommentAdded",  "27", null,         "Moving deadline out by 3 days, awaiting approval."),
            History("h14", today.AddHours(9),                  "David Lee",     "StatusChanged", "28", "Open",       "InProgress"),
        ];

        // ── Resource Calendars ─────────────────────────────────────
        _resourceCalendars =
        [
            new()
            {
                AssigneeId = "a1",
                VacationDays = [new DateRange(today.AddDays(15), today.AddDays(22))],
                DaysOff      = [today.AddDays(7)],
            },
            new()
            {
                AssigneeId = "a2",
                VacationDays = [],
                DaysOff      = [today.AddDays(5), today.AddDays(6)],
            },
            new()
            {
                AssigneeId = "a3",
                VacationDays = [new DateRange(today.AddDays(30), today.AddDays(35))],
                DaysOff      = [],
            },
            new()
            {
                AssigneeId = "v2",
                VacationDays = [],
                DaysOff      = [today.AddDays(2), today.AddDays(9), today.AddDays(16)],
            },
        ];

        // ── Reports ────────────────────────────────────────────────
        _reports =
        [
            new() { Id = "r1", Name = "Sprint Status Summary",    Type = GanttReportType.StatusSummary,       Config = new() { ["sprint"] = "Sprint 2",    ["includeSubtasks"] = "true" } },
            new() { Id = "r2", Name = "Time Spent – This Month",  Type = GanttReportType.TimeSpent,           Config = new() { ["period"] = "month",       ["groupBy"] = "assignee" } },
            new() { Id = "r3", Name = "Resource Utilization Q2",  Type = GanttReportType.ResourceUtilization, Config = new() { ["quarter"] = "Q2",         ["includeVirtual"] = "true" } },
            new() { Id = "r4", Name = "Budget Overview – Alpha",  Type = GanttReportType.BudgetOverview,      Config = new() { ["project"] = "1",         ["currency"] = "USD" } },
            new() { Id = "r5", Name = "Milestone Progress",       Type = GanttReportType.MilestoneProgress,   Config = new() { ["includeFuture"] = "true" } },
        ];

        // ── Working Schedule ───────────────────────────────────────
        _workingSchedule = new WorkingSchedule
        {
            NonWorkingDaysOfWeek = [DayOfWeek.Saturday, DayOfWeek.Sunday],
            WorkDayStartHour = 8,
            WorkDayEndHour   = 17,
            Holidays         = [today.AddDays(8), today.AddDays(22)],
        };
    }

    private static TmActivityEntry History(
        string id,
        DateTime timestamp,
        string author,
        string action,
        string taskId,
        string? before,
        string? after)
    {
        var actorId = author.ToLowerInvariant().Replace(' ', '.');
        return new TmActivityEntry
        {
            Id = id,
            EntityRef = TmEntityRef.Create("work-item", taskId, sourceKey: "gantt-demo"),
            Actor = new TmUserRef { Id = actorId, DisplayName = author },
            Action = action,
            Timestamp = new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Local)).ToUniversalTime(),
            Before = before,
            After = after
        };
    }

    // ── Read accessors ─────────────────────────────────────────────

    public IReadOnlyList<TmWorkItem>             GetTasks()              => _tasks;
    public IReadOnlyList<GanttDependency>        GetDependencies()       => _dependencies;
    public IReadOnlyList<GanttBaseline>          GetBaselines()          => _baselines;
    public IReadOnlyList<TmCustomFieldDefinition> GetCustomFields()      => _customFields;
    public IReadOnlyList<TmActivityEntry>        GetHistory()            => _history;
    public IReadOnlyList<GanttResourceCalendar>  GetResourceCalendars()  => _resourceCalendars;
    public IReadOnlyList<GanttReport>            GetReports()            => _reports;
    public WorkingSchedule                       GetWorkingSchedule()    => _workingSchedule;
    public IReadOnlyList<TmWorkItemAssignee>          GetAssignees()          => _assignees;

    // ── Mutations ──────────────────────────────────────────────────

    public bool UpdateTask(string id, TmWorkItem updated)
    {
        var idx = _tasks.FindIndex(t => t.Id == id);
        if (idx < 0) return false;
        updated.Id = id;
        _tasks[idx] = updated;
        return true;
    }

    public TmWorkItem AddTask(TmWorkItem task)
    {
        task.Id = Guid.NewGuid().ToString();
        _tasks.Add(task);
        return task;
    }

    public bool RemoveTask(string id)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == id);
        if (task is null) return false;
        _tasks.Remove(task);
        _dependencies.RemoveAll(d => d.FromId == id || d.ToId == id);
        var childIds = _tasks.Where(t => t.ParentId == id).Select(t => t.Id).ToList();
        foreach (var childId in childIds)
            RemoveTask(childId);
        return true;
    }

    public GanttDependency AddDependency(GanttDependency dep)
    {
        dep.Id = Guid.NewGuid().ToString();
        _dependencies.Add(dep);
        return dep;
    }

    public bool RemoveDependency(string id)
    {
        var dep = _dependencies.FirstOrDefault(d => d.Id == id);
        if (dep is null) return false;
        _dependencies.Remove(dep);
        return true;
    }

    public GanttBaseline AddBaseline(GanttBaseline baseline)
    {
        baseline.Id = Guid.NewGuid().ToString();
        _baselines.Add(baseline);
        return baseline;
    }

    public void UpdateResourceCalendar(GanttResourceCalendar calendar)
    {
        var idx = _resourceCalendars.FindIndex(c => c.AssigneeId == calendar.AssigneeId);
        if (idx >= 0)
            _resourceCalendars[idx] = calendar;
        else
            _resourceCalendars.Add(calendar);
    }

    public TmActivityEntry AddHistory(TmActivityEntry entry)
    {
        _history.Add(entry);
        return entry;
    }

    public void Reset()
    {
        var fresh = new MockGanttStore();
        _tasks.Clear();             _tasks.AddRange(fresh._tasks);
        _dependencies.Clear();      _dependencies.AddRange(fresh._dependencies);
        _baselines.Clear();         _baselines.AddRange(fresh._baselines);
        _customFields.Clear();      _customFields.AddRange(fresh._customFields);
        _history.Clear();           _history.AddRange(fresh._history);
        _resourceCalendars.Clear(); _resourceCalendars.AddRange(fresh._resourceCalendars);
        _reports.Clear();           _reports.AddRange(fresh._reports);
        _assignees.Clear();         _assignees.AddRange(fresh._assignees);
    }
}
