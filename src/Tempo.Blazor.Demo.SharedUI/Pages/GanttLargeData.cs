using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.WorkItems;

namespace Tempo.Blazor.Demo.SharedUI.Pages;

/// <summary>
/// Deterministic data set for the <c>/gantt-large</c> demo page (Fáze 12): <paramref name="count"/> tasks in a
/// depth-4 tree (a group root, 5 mid groups of 3 sub groups with 4 leaves each) with ~1.2 dependencies per task.
/// The generator is a fixed-seed LCG — the same count produces the same tasks on WASM, Server and
/// InteractiveAuto, so the measurements and the E2E are reproducible.
/// </summary>
public static class GanttLargeData
{
    private const int LeavesPerSub = 4;   // level 4 under every level-3 sub group
    private const int SubsPerMid = 3;     // level 3 under every level-2 mid group
    private const int MidsPerRoot = 5;    // level 2 under every group root
    private const int GroupSize = 1 + MidsPerRoot + MidsPerRoot * SubsPerMid + MidsPerRoot * SubsPerMid * LeavesPerSub; // 81

    /// <summary>Generates the tasks (exactly <paramref name="count"/>) and the dependencies (~1.2 × count).</summary>
    public static (List<TmWorkItem> Tasks, List<GanttDependency> Dependencies) Generate(int count)
    {
        var tasks = new List<TmWorkItem>(count);
        var rng = new Lcg(20240612);
        var groupBase = 0; // 0-based index just before the current group's root task

        for (var i = 1; i <= count; i++)
        {
            var position = (i - 1) % GroupSize;
            if (position == 0) groupBase = i - 1;

            // Depth-4 structure inside the group (positions): 0 = root · 1–5 = mid · 6–20 = sub · 21–80 = leaf.
            string? parent = position switch
            {
                0 => null,
                <= MidsPerRoot => IdOf(groupBase + 1),
                <= MidsPerRoot + MidsPerRoot * SubsPerMid => IdOf(groupBase + 2 + (position - 1 - MidsPerRoot) / SubsPerMid),
                _ => IdOf(groupBase + 7 + (position - 1 - MidsPerRoot - MidsPerRoot * SubsPerMid) / LeavesPerSub),
            };

            var day = rng.Next(300);
            var duration = 2 + rng.Next(5);
            tasks.Add(new TmWorkItem
            {
                Id = IdOf(i),
                Title = $"Task {i}",
                ParentId = parent,
                Start = new DateTime(2024, 1, 1).AddDays(day),
                End = new DateTime(2024, 1, 1).AddDays(day + duration),
                PercentComplete = rng.Next(101),
                Status = (TmWorkItemStatus)rng.Next(4),
                Priority = (TmWorkItemPriority)rng.Next(5),
                IsExpanded = true,
            });
        }

        // ~1.2 dependencies per task: every task gets one forward link, a fifth of them two.
        var dependencies = new List<GanttDependency>((int)(count * 1.2));
        for (var i = 1; i <= count; i++)
        {
            var links = rng.Next(5) == 0 ? 2 : 1;
            for (var l = 0; l < links; l++)
            {
                var target = i + 1 + rng.Next(Math.Max(2, count / 10));
                if (target > count) continue;
                dependencies.Add(new GanttDependency { Id = $"d{i}-{target}-{l}", FromId = IdOf(i), ToId = IdOf(target) });
            }
        }

        return (tasks, dependencies);
    }

    private static string IdOf(int i) => $"n{i}";

    /// <summary>A 48-bit linear congruential generator: identical sequences on every runtime and platform.</summary>
    private struct Lcg(ulong seed)
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong Increment = 1442695040888963407UL;
        private ulong _state = seed;

        private ulong NextUlong()
        {
            _state = _state * Multiplier + Increment;
            return (_state >> 16) & 0xFFFFFFFFFFFF; // keep the high 48 bits
        }

        /// <summary>A non-negative value below <paramref name="bound"/> (bound > 0).</summary>
        public int Next(int bound) => bound <= 0 ? 0 : (int)(NextUlong() % (ulong)bound);
    }
}
