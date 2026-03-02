using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.Api.Data;

public class MockScheduleStore
{
    private readonly List<TmScheduleEvent> _events;
    private readonly List<TmScheduleResource> _resources;

    public MockScheduleStore()
    {
        var today = DateTime.Today;
        var startOfWeek = today.AddDays(-(int)today.DayOfWeek + 1);
        
        _resources = new List<TmScheduleResource>
        {
            new() { Id = "room-a", Name = "Conference Room A", Color = "#3b82f6" },
            new() { Id = "room-b", Name = "Conference Room B", Color = "#10b981" },
            new() { Id = "room-c", Name = "Meeting Room C", Color = "#f59e0b" },
            new() { Id = "room-d", Name = "Board Room", Color = "#8b5cf6" },
        };

        _events = new List<TmScheduleEvent>
        {
            // This week events
            new() { Id = $"evt-{Guid.NewGuid()}", Title = "Team Standup", Start = startOfWeek.AddHours(9), End = startOfWeek.AddHours(9.5), Color = "#3b82f6" },
            new() { Id = $"evt-{Guid.NewGuid()}", Title = "Product Review", Start = startOfWeek.AddDays(1).AddHours(14), End = startOfWeek.AddDays(1).AddHours(15), Color = "#10b981" },
            new() { Id = $"evt-{Guid.NewGuid()}", Title = "Sprint Planning", Start = startOfWeek.AddDays(2).AddHours(10), End = startOfWeek.AddDays(2).AddHours(12), Color = "#f59e0b" },
            new() { Id = $"evt-{Guid.NewGuid()}", Title = "Client Meeting", Start = startOfWeek.AddDays(3).AddHours(11), End = startOfWeek.AddDays(3).AddHours(12), Color = "#ef4444", ResourceId = "room-a" },
            new() { Id = $"evt-{Guid.NewGuid()}", Title = "Code Review", Start = startOfWeek.AddDays(3).AddHours(14), End = startOfWeek.AddDays(3).AddHours(15), Color = "#8b5cf6" },
            new() { Id = $"evt-{Guid.NewGuid()}", Title = "Demo Session", Start = startOfWeek.AddDays(4).AddHours(10), End = startOfWeek.AddDays(4).AddHours(11), Color = "#ec4899", ResourceId = "room-b" },
            
            // Next week events
            new() { Id = $"evt-{Guid.NewGuid()}", Title = "Quarterly Review", Start = startOfWeek.AddDays(7).AddHours(9), End = startOfWeek.AddDays(7).AddHours(11), Color = "#3b82f6" },
            new() { Id = $"evt-{Guid.NewGuid()}", Title = "Architecture Discussion", Start = startOfWeek.AddDays(8).AddHours(13), End = startOfWeek.AddDays(8).AddHours(14), Color = "#10b981", ResourceId = "room-c" },
            new() { Id = $"evt-{Guid.NewGuid()}", Title = "HR Interview", Start = startOfWeek.AddDays(9).AddHours(10), End = startOfWeek.AddDays(9).AddHours(11), Color = "#f59e0b", ResourceId = "room-a" },
            
            // Previous week events
            new() { Id = $"evt-{Guid.NewGuid()}", Title = "Retrospective", Start = startOfWeek.AddDays(-2).AddHours(15), End = startOfWeek.AddDays(-2).AddHours(16), Color = "#8b5cf6" },
            new() { Id = $"evt-{Guid.NewGuid()}", Title = "All Hands", Start = startOfWeek.AddDays(-4).AddHours(9), End = startOfWeek.AddDays(-4).AddHours(10), Color = "#ef4444", ResourceId = "room-d" },
        };

        // Generate some random events for the current month
        var random = new Random(42);
        var titles = new[] { "1:1 Meeting", "Technical Discussion", "Design Review", "Standup", "Workshop", "Training", "Presentation", "Interview" };
        
        for (int i = 0; i < 30; i++)
        {
            var day = random.Next(1, 29);
            var hour = random.Next(9, 17);
            var duration = random.Next(1, 3);
            var title = titles[random.Next(titles.Length)];
            var colors = new[] { "#3b82f6", "#10b981", "#f59e0b", "#8b5cf6", "#ef4444", "#ec4899" };
            var color = colors[random.Next(colors.Length)];
            
            _events.Add(new TmScheduleEvent
            {
                Id = $"evt-{Guid.NewGuid()}",
                Title = title,
                Start = new DateTime(today.Year, today.Month, day).AddHours(hour),
                End = new DateTime(today.Year, today.Month, day).AddHours(hour + duration),
                Color = color
            });
        }
    }

    public IReadOnlyList<TmScheduleEvent> GetEvents(DateTime start, DateTime end)
    {
        return _events
            .Where(e => e.Start < end && e.End > start)
            .OrderBy(e => e.Start)
            .ToList();
    }

    public IReadOnlyList<TmScheduleResource> GetResources()
    {
        return _resources;
    }

    public TmScheduleEvent? GetEvent(string id)
    {
        return _events.FirstOrDefault(e => e.Id == id);
    }

    public bool UpdateEvent(string id, DateTime newStart, DateTime newEnd)
    {
        var evt = _events.FirstOrDefault(e => e.Id == id);
        if (evt == null) return false;
        
        evt.Start = newStart;
        evt.End = newEnd;
        return true;
    }

    public TmScheduleEvent CreateEvent(string title, DateTime start, DateTime end, string color, string? resourceId = null)
    {
        var evt = new TmScheduleEvent
        {
            Id = $"evt-{Guid.NewGuid()}",
            Title = title,
            Start = start,
            End = end,
            Color = color,
            ResourceId = resourceId
        };
        _events.Add(evt);
        return evt;
    }

    public bool DeleteEvent(string id)
    {
        var evt = _events.FirstOrDefault(e => e.Id == id);
        if (evt == null) return false;
        
        _events.Remove(evt);
        return true;
    }
}
