using FluentAssertions;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>TDD tests for InMemoryScheduleDataProvider.</summary>
public class InMemoryScheduleDataProviderTests
{
    private static TmScheduleEvent Evt(string title, DateTime start, DateTime end) =>
        new() { Title = title, Start = start, End = end };

    [Fact]
    public async Task Returns_Events_Within_Range()
    {
        var events = new[]
        {
            Evt("Inside", new(2025, 6, 15, 10, 0, 0), new(2025, 6, 15, 11, 0, 0)),
            Evt("Outside", new(2025, 7, 1, 10, 0, 0), new(2025, 7, 1, 11, 0, 0)),
        };
        var provider = new InMemoryScheduleDataProvider(events);
        var query = new TmScheduleQuery(new(2025, 6, 1), new(2025, 7, 1));

        var result = await provider.GetEventsAsync(query);

        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Inside");
    }

    [Fact]
    public async Task Returns_Empty_For_No_Matching_Events()
    {
        var events = new[]
        {
            Evt("Event", new(2025, 3, 1, 9, 0, 0), new(2025, 3, 1, 10, 0, 0)),
        };
        var provider = new InMemoryScheduleDataProvider(events);
        var query = new TmScheduleQuery(new(2025, 6, 1), new(2025, 7, 1));

        var result = await provider.GetEventsAsync(query);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Includes_Events_Overlapping_Range_Start()
    {
        var events = new[]
        {
            Evt("Overlapping", new(2025, 5, 31, 22, 0, 0), new(2025, 6, 1, 2, 0, 0)),
        };
        var provider = new InMemoryScheduleDataProvider(events);
        var query = new TmScheduleQuery(new(2025, 6, 1), new(2025, 7, 1));

        var result = await provider.GetEventsAsync(query);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task Includes_Events_Overlapping_Range_End()
    {
        var events = new[]
        {
            Evt("Overlapping", new(2025, 6, 30, 22, 0, 0), new(2025, 7, 1, 2, 0, 0)),
        };
        var provider = new InMemoryScheduleDataProvider(events);
        var query = new TmScheduleQuery(new(2025, 6, 1), new(2025, 7, 1));

        var result = await provider.GetEventsAsync(query);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task Filters_By_ResourceId_When_Specified()
    {
        var events = new[]
        {
            new TmScheduleEvent { Title = "Room A", Start = new(2025, 6, 15, 10, 0, 0), End = new(2025, 6, 15, 11, 0, 0), ResourceId = "room-a" },
            new TmScheduleEvent { Title = "Room B", Start = new(2025, 6, 15, 10, 0, 0), End = new(2025, 6, 15, 11, 0, 0), ResourceId = "room-b" },
        };
        var provider = new InMemoryScheduleDataProvider(events);
        var query = new TmScheduleQuery(new(2025, 6, 1), new(2025, 7, 1), ResourceId: "room-a");

        var result = await provider.GetEventsAsync(query);

        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Room A");
    }

    [Fact]
    public async Task Returns_All_Resources_When_ResourceId_Is_Null()
    {
        var events = new[]
        {
            new TmScheduleEvent { Title = "Room A", Start = new(2025, 6, 15, 10, 0, 0), End = new(2025, 6, 15, 11, 0, 0), ResourceId = "room-a" },
            new TmScheduleEvent { Title = "Room B", Start = new(2025, 6, 15, 10, 0, 0), End = new(2025, 6, 15, 11, 0, 0), ResourceId = "room-b" },
        };
        var provider = new InMemoryScheduleDataProvider(events);
        var query = new TmScheduleQuery(new(2025, 6, 1), new(2025, 7, 1));

        var result = await provider.GetEventsAsync(query);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task Expands_Recurring_Events()
    {
        var events = new[]
        {
            new TmScheduleEvent
            {
                Title = "Daily Standup",
                Start = new(2025, 6, 1, 9, 0, 0),
                End = new(2025, 6, 1, 9, 30, 0),
                RecurrenceRule = "FREQ=DAILY;INTERVAL=1"
            }
        };
        var provider = new InMemoryScheduleDataProvider(events);
        var query = new TmScheduleQuery(new(2025, 6, 1), new(2025, 6, 4));

        var result = await provider.GetEventsAsync(query);

        // Should get 3 expanded occurrences (June 1, 2, 3)
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task Recurring_And_Regular_Events_Together()
    {
        var events = new[]
        {
            new TmScheduleEvent
            {
                Title = "Daily",
                Start = new(2025, 6, 1, 9, 0, 0),
                End = new(2025, 6, 1, 9, 30, 0),
                RecurrenceRule = "FREQ=DAILY;COUNT=3"
            },
            new TmScheduleEvent
            {
                Title = "One-off",
                Start = new(2025, 6, 2, 14, 0, 0),
                End = new(2025, 6, 2, 15, 0, 0)
            }
        };
        var provider = new InMemoryScheduleDataProvider(events);
        var query = new TmScheduleQuery(new(2025, 6, 1), new(2025, 6, 5));

        var result = await provider.GetEventsAsync(query);

        // 3 expanded occurrences + 1 regular event = 4
        result.Should().HaveCount(4);
    }
}
