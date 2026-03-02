using FluentAssertions;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>TDD tests for RecurrenceEngine — RRULE parse, serialize, expand.</summary>
public class RecurrenceEngineTests
{
    // ── Parse ──

    [Fact]
    public void Parse_Daily_Rule()
    {
        var rule = RecurrenceEngine.Parse("FREQ=DAILY;INTERVAL=1");

        rule.Should().NotBeNull();
        rule!.Frequency.Should().Be(TmRecurrenceFrequency.Daily);
        rule.Interval.Should().Be(1);
    }

    [Fact]
    public void Parse_Weekly_With_ByDay()
    {
        var rule = RecurrenceEngine.Parse("FREQ=WEEKLY;BYDAY=MO,WE,FR");

        rule.Should().NotBeNull();
        rule!.Frequency.Should().Be(TmRecurrenceFrequency.Weekly);
        rule.ByDay.Should().BeEquivalentTo(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday });
    }

    [Fact]
    public void Parse_Monthly_With_Count()
    {
        var rule = RecurrenceEngine.Parse("FREQ=MONTHLY;INTERVAL=2;COUNT=5");

        rule.Should().NotBeNull();
        rule!.Frequency.Should().Be(TmRecurrenceFrequency.Monthly);
        rule.Interval.Should().Be(2);
        rule.Count.Should().Be(5);
    }

    [Fact]
    public void Parse_Yearly_With_Until()
    {
        var rule = RecurrenceEngine.Parse("FREQ=YEARLY;UNTIL=20261231T000000Z");

        rule.Should().NotBeNull();
        rule!.Frequency.Should().Be(TmRecurrenceFrequency.Yearly);
        rule.Until.Should().Be(new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Parse_Returns_Null_For_Null_Input()
    {
        RecurrenceEngine.Parse(null).Should().BeNull();
    }

    [Fact]
    public void Parse_Returns_Null_For_Empty_Input()
    {
        RecurrenceEngine.Parse("").Should().BeNull();
    }

    // ── Serialize ──

    [Fact]
    public void Serialize_Daily_Rule()
    {
        var rule = new TmRecurrenceRule
        {
            Frequency = TmRecurrenceFrequency.Daily,
            Interval = 1
        };

        var rrule = RecurrenceEngine.Serialize(rule);

        rrule.Should().Contain("FREQ=DAILY");
        rrule.Should().Contain("INTERVAL=1");
    }

    [Fact]
    public void Serialize_Weekly_With_ByDay()
    {
        var rule = new TmRecurrenceRule
        {
            Frequency = TmRecurrenceFrequency.Weekly,
            ByDay = [DayOfWeek.Tuesday, DayOfWeek.Thursday]
        };

        var rrule = RecurrenceEngine.Serialize(rule);

        rrule.Should().Contain("FREQ=WEEKLY");
        rrule.Should().Contain("BYDAY=TU,TH");
    }

    [Fact]
    public void Serialize_With_Count()
    {
        var rule = new TmRecurrenceRule
        {
            Frequency = TmRecurrenceFrequency.Monthly,
            Count = 10
        };

        var rrule = RecurrenceEngine.Serialize(rule);

        rrule.Should().Contain("COUNT=10");
    }

    // ── Expand ──

    [Fact]
    public void Expand_Daily_Within_Range()
    {
        var source = new TmScheduleEvent
        {
            Id = "r1",
            Title = "Daily Standup",
            Start = new(2025, 6, 1, 9, 0, 0),
            End = new(2025, 6, 1, 9, 30, 0),
            RecurrenceRule = "FREQ=DAILY;INTERVAL=1"
        };

        var result = RecurrenceEngine.ExpandRecurrence(source, new(2025, 6, 1), new(2025, 6, 4));

        result.Should().HaveCount(3); // June 1, 2, 3
        result[0].Start.Day.Should().Be(1);
        result[1].Start.Day.Should().Be(2);
        result[2].Start.Day.Should().Be(3);
    }

    [Fact]
    public void Expand_Preserves_Event_Duration()
    {
        var source = new TmScheduleEvent
        {
            Id = "r1",
            Title = "Meeting",
            Start = new(2025, 6, 1, 14, 0, 0),
            End = new(2025, 6, 1, 15, 30, 0),
            RecurrenceRule = "FREQ=DAILY;INTERVAL=1"
        };

        var result = RecurrenceEngine.ExpandRecurrence(source, new(2025, 6, 1), new(2025, 6, 3));

        result[0].Start.Hour.Should().Be(14);
        result[0].End.Hour.Should().Be(15);
        result[0].End.Minute.Should().Be(30);
    }

    [Fact]
    public void Expand_Weekly_With_ByDay()
    {
        var source = new TmScheduleEvent
        {
            Id = "r2",
            Title = "Sprint Planning",
            Start = new(2025, 6, 2, 10, 0, 0), // Monday
            End = new(2025, 6, 2, 11, 0, 0),
            RecurrenceRule = "FREQ=WEEKLY;BYDAY=MO,WE,FR"
        };

        var result = RecurrenceEngine.ExpandRecurrence(source, new(2025, 6, 1), new(2025, 6, 8));

        // June 2 (Mon), June 4 (Wed), June 6 (Fri) = 3 occurrences
        result.Should().HaveCount(3);
        result[0].Start.DayOfWeek.Should().Be(DayOfWeek.Monday);
        result[1].Start.DayOfWeek.Should().Be(DayOfWeek.Wednesday);
        result[2].Start.DayOfWeek.Should().Be(DayOfWeek.Friday);
    }

    [Fact]
    public void Expand_With_Count_Limit()
    {
        var source = new TmScheduleEvent
        {
            Id = "r3",
            Title = "Daily",
            Start = new(2025, 6, 1, 9, 0, 0),
            End = new(2025, 6, 1, 10, 0, 0),
            RecurrenceRule = "FREQ=DAILY;COUNT=3"
        };

        var result = RecurrenceEngine.ExpandRecurrence(source, new(2025, 6, 1), new(2025, 6, 30));

        result.Should().HaveCount(3); // Capped by COUNT
    }

    [Fact]
    public void Expand_With_Until_Limit()
    {
        var source = new TmScheduleEvent
        {
            Id = "r4",
            Title = "Daily",
            Start = new(2025, 6, 1, 9, 0, 0),
            End = new(2025, 6, 1, 10, 0, 0),
            RecurrenceRule = "FREQ=DAILY;UNTIL=20250603T000000Z"
        };

        var result = RecurrenceEngine.ExpandRecurrence(source, new(2025, 6, 1), new(2025, 6, 30));

        result.Should().HaveCount(3); // June 1, 2, 3
    }

    [Fact]
    public void Expand_Respects_Exceptions()
    {
        var source = new TmScheduleEvent
        {
            Id = "r5",
            Title = "Daily",
            Start = new(2025, 6, 1, 9, 0, 0),
            End = new(2025, 6, 1, 10, 0, 0),
            RecurrenceRule = "FREQ=DAILY;INTERVAL=1",
            RecurrenceExceptions = [new(2025, 6, 2, 9, 0, 0)]
        };

        var result = RecurrenceEngine.ExpandRecurrence(source, new(2025, 6, 1), new(2025, 6, 4));

        result.Should().HaveCount(2); // June 1 and June 3 (June 2 excluded)
        result.Select(e => e.Start.Day).Should().BeEquivalentTo(new[] { 1, 3 });
    }

    [Fact]
    public void Expand_Sets_Unique_Instance_Ids()
    {
        var source = new TmScheduleEvent
        {
            Id = "r6",
            Title = "Daily",
            Start = new(2025, 6, 1, 9, 0, 0),
            End = new(2025, 6, 1, 10, 0, 0),
            RecurrenceRule = "FREQ=DAILY;INTERVAL=1"
        };

        var result = RecurrenceEngine.ExpandRecurrence(source, new(2025, 6, 1), new(2025, 6, 4));

        result.Select(e => e.Id).Should().OnlyHaveUniqueItems();
        result[0].Id.Should().Contain("r6");
    }

    [Fact]
    public void Expand_Monthly_By_MonthDay()
    {
        var source = new TmScheduleEvent
        {
            Id = "r7",
            Title = "Monthly Report",
            Start = new(2025, 1, 15, 10, 0, 0),
            End = new(2025, 1, 15, 11, 0, 0),
            RecurrenceRule = "FREQ=MONTHLY;BYMONTHDAY=15"
        };

        var result = RecurrenceEngine.ExpandRecurrence(source, new(2025, 6, 1), new(2025, 9, 1));

        // June 15, July 15, Aug 15 = 3 occurrences
        result.Should().HaveCount(3);
        result.All(e => e.Start.Day == 15).Should().BeTrue();
    }

    [Fact]
    public void Expand_With_Interval()
    {
        var source = new TmScheduleEvent
        {
            Id = "r8",
            Title = "Biweekly",
            Start = new(2025, 6, 2, 9, 0, 0), // Monday
            End = new(2025, 6, 2, 10, 0, 0),
            RecurrenceRule = "FREQ=WEEKLY;INTERVAL=2"
        };

        var result = RecurrenceEngine.ExpandRecurrence(source, new(2025, 6, 1), new(2025, 7, 1));

        // June 2, June 16, June 30 = 3 occurrences (every 2 weeks)
        result.Should().HaveCount(3);
        result[0].Start.Day.Should().Be(2);
        result[1].Start.Day.Should().Be(16);
        result[2].Start.Day.Should().Be(30);
    }

    [Fact]
    public void Expand_Returns_Empty_For_No_RecurrenceRule()
    {
        var source = new TmScheduleEvent
        {
            Id = "e1",
            Title = "Regular",
            Start = new(2025, 6, 10, 9, 0, 0),
            End = new(2025, 6, 10, 10, 0, 0),
        };

        var result = RecurrenceEngine.ExpandRecurrence(source, new(2025, 6, 1), new(2025, 7, 1));

        result.Should().BeEmpty();
    }

    // ── Round-trip ──

    [Fact]
    public void Roundtrip_Parse_Serialize()
    {
        var original = "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,FR;COUNT=10";
        var rule = RecurrenceEngine.Parse(original);
        var serialized = RecurrenceEngine.Serialize(rule!);

        var reparsed = RecurrenceEngine.Parse(serialized);
        reparsed!.Frequency.Should().Be(TmRecurrenceFrequency.Weekly);
        reparsed.Interval.Should().Be(2);
        reparsed.Count.Should().Be(10);
        reparsed.ByDay.Should().BeEquivalentTo(new[] { DayOfWeek.Monday, DayOfWeek.Friday });
    }
}
