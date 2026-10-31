using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 7b — the WALL-CLOCK performance evidence for the canvas changes of phases 2–7 (remaining task
/// <c>12bc2afb</c>, the phase-2 carry-forward). This box's rAF cadence swings by an order of magnitude,
/// so a single run can never prove a &lt;= +10 % budget: the claim is an IN-SESSION A/B of the
/// selection-drag scenario — the shipped <c>v2.9.0</c> <c>spreadsheet-canvas.js</c> (arm A, served
/// through Playwright route interception) against the current build (arm B, arm), alternating at least
/// seven times, reported as a MEDIAN plus its IQR.
/// <para>
/// The measurement itself runs IN-PAGE (<c>tmSpreadsheetBenchmark.runSelectionDragProbe</c>) with
/// synthetic PointerEvents, because a real Playwright mouse action round trip costs far more than the
/// canvas work it would be measuring. The probe verifies that the selection actually changed
/// (<c>changedCells &gt; 0</c>) — a no-op arm fails instead of producing a fake number.
/// </para>
/// <para>
/// Verdict rule (mandated by the phase): a median within +10 % passes, and an IQR above 30 % of the
/// median makes the test <c>Inconclusive</c> WITH the measured numbers — never a fake pass. Either way
/// the run writes <c>eng/release-evidence/spreadsheet-perf-&lt;yyyyMMdd&gt;/</c> (JSON + markdown), which
/// is what CHANGELOG and docs/spreadsheet.md link to.
/// </para>
/// </summary>
[TestClass]
public class SpreadsheetPerformanceE2ETests : WasmTestBase
{
    private const string GridSelector = "[data-testid='spreadsheet-demo'] .tm-spreadsheet-canvas-grid";
    private const string BaselineTag = "v2.9.0";
    private const string CanvasRelativePath = "src/Tempo.Blazor.Spreadsheet/wwwroot/js/spreadsheet-canvas.js";
    private const int Alternations = 7;
    private const int ProbeSteps = 8;
    private const double Budget = 1.10;
    private const double MaxIqrRatio = 0.30;

    /// <summary>The v2.9.0 canvas script, read once through git (the baseline deliberately is not a repo fixture that could go stale).</summary>
    private static readonly Lazy<string> BaselineCanvas = new(() =>
    {
        var root = RepoRoot();
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("show");
        start.ArgumentList.Add($"{BaselineTag}:{CanvasRelativePath}");
        using var process = Process.Start(start);
        Assert.IsNotNull(process, "git must be runnable to serve the v2.9.0 baseline");
        var output = process!.StandardOutput.ReadToEnd();
        process.WaitForExit(60_000);
        Assert.AreEqual(0, process.ExitCode, $"git show {BaselineTag}:{CanvasRelativePath} must succeed (stderr: {process.StandardError.ReadToEnd()})");
        Assert.IsTrue(output.Length > 10_000, "the baseline canvas script must not be empty");
        return output;
    });

    [TestMethod]
    [TestCategory("Performance")]
    public async Task SelectionDrag_AB_WithinBudget()
    {
        var baselinePath = Environment.GetEnvironmentVariable("TM_E2E_PERF_BASELINE_CANVAS");
        var baseline = !string.IsNullOrWhiteSpace(baselinePath) && File.Exists(baselinePath)
            ? await File.ReadAllTextAsync(baselinePath)
            : BaselineCanvas.Value;
        var current = await File.ReadAllTextAsync(Path.Combine(RepoRoot().FullName, CanvasRelativePath));

        var arm = "baseline";
        var page = await CreatePageAsync();
        await page.RouteAsync("**/spreadsheet-canvas.js*", async route =>
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/javascript",
                // A memory-cached script would silently keep the PREVIOUS arm across the alternation.
                Headers = new Dictionary<string, string> { ["cache-control"] = "no-store" },
                Body = arm == "baseline" ? baseline : current,
            });
        });

        async Task<ArmRun> RunAsync(string which)
        {
            arm = which;
            await GotoAppReadyAsync(page, $"{BaseUrl}/spreadsheet");
            var grid = page.Locator(GridSelector).First;
            await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60_000 });
            await page.WaitForFunctionAsync(
                "selector => !!document.querySelector(selector)?.__tmSpreadsheetCanvas?.model",
                GridSelector,
                new PageWaitForFunctionOptions { Timeout = 60_000 });

            // Prove the route served the right arm: resolveTouchTap is a phase-7 helper the v2.9.0
            // script cannot have, and getCellCenter is the phase-7b engine seam.
            var served = await page.EvaluateAsync<string>(
                "() => window.tmSpreadsheetCanvas?._internals?.resolveTouchTap ? 'current' : 'baseline'");
            Assert.AreEqual(which, served, $"the route interception must serve the {which} arm");

            var probe = await page.EvaluateAsync<SelectionDragProbeResult>(
                "args => window.tmSpreadsheetBenchmark.runSelectionDragProbe(args.selector, { steps: args.steps })",
                new { selector = GridSelector, steps = ProbeSteps });

            Assert.IsTrue(probe.Usable,
                $"[{which}] the selection-drag probe must actually move the selection (changedCells {probe.ChangedCells}) — "
                + "a no-op probe is not a measurement");
            Assert.IsTrue(probe.DurationMs > 0, $"[{which}] the probe must report a positive duration");
            return new ArmRun(which, probe);
        }

        var runs = new List<ArmRun>();
        for (var round = 0; round < Alternations; round++)
        {
            runs.Add(await RunAsync("baseline"));
            runs.Add(await RunAsync("current"));
        }

        var baselineRuns = runs.Where(run => run.Arm == "baseline").ToList();
        var currentRuns = runs.Where(run => run.Arm == "current").ToList();
        var baselineMedian = Median(baselineRuns.Select(run => run.Probe.DurationMs));
        var currentMedian = Median(currentRuns.Select(run => run.Probe.DurationMs));
        var (baselineQ1, baselineQ3) = Quartiles(baselineRuns.Select(run => run.Probe.DurationMs));
        var (currentQ1, currentQ3) = Quartiles(currentRuns.Select(run => run.Probe.DurationMs));
        var baselineIqr = baselineQ3 - baselineQ1;
        var currentIqr = currentQ3 - currentQ1;
        var noiseRatio = Math.Max(
            baselineMedian > 0 ? baselineIqr / baselineMedian : 0,
            currentMedian > 0 ? currentIqr / currentMedian : 0);
        var ratio = baselineMedian > 0 ? currentMedian / baselineMedian : double.PositiveInfinity;

        // Hard assertions, unchanged from phase 7 plus the phase-7b EXACT header count: the header layer
        // paints exactly once per selection change (the dirty-strip contract, not merely "at most as
        // often as the selection"), the selection layer covers every change, and a header paint stays
        // inside a frame.
        foreach (var run in currentRuns)
        {
            Assert.AreEqual(run.Probe.ChangedCells, run.Probe.HeaderLayerPaintCount,
                $"the header layer must repaint exactly once per selection change "
                + $"({run.Probe.HeaderLayerPaintCount} paints for {run.Probe.ChangedCells} changes)");
            Assert.IsTrue(run.Probe.HeaderLayerPaintCount <= run.Probe.SelectionPaintFrameCount + 0.5,
                $"header layer {run.Probe.HeaderLayerPaintCount} must not paint more often than the selection layer {run.Probe.SelectionPaintFrameCount}");
            Assert.IsTrue(run.Probe.SelectionPaintFrameCount >= run.Probe.ChangedCells,
                $"every changed selection must reach the selection layer ({run.Probe.SelectionPaintFrameCount} paints for "
                + $"{run.Probe.ChangedCells} changes)");
            Assert.IsTrue(run.Probe.LastHeaderLayerMs < 16,
                $"the header layer paint must stay inside a frame, was {run.Probe.LastHeaderLayerMs:F2} ms");
        }

        var evidence = new PerfEvidence(
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            BaselineTag,
            Alternations,
            ProbeSteps,
            Budget,
            MaxIqrRatio,
            baselineMedian,
            currentMedian,
            baselineQ1,
            baselineQ3,
            currentQ1,
            currentQ3,
            baselineIqr,
            currentIqr,
            noiseRatio,
            ratio,
            runs.Select(run => new PerfArmSample(run.Arm, run.Probe)).ToList());

        var directory = Path.Combine(
            RepoRoot().FullName,
            "eng",
            "release-evidence",
            $"spreadsheet-perf-{DateTime.Now:yyyyMMdd}");
        await WriteEvidenceAsync(directory, evidence);

        TestContext.WriteLine(
            "[spreadsheet-perf] median baseline {0:F1} ms (IQR {1:F1}) vs current {2:F1} ms (IQR {3:F1}) — ratio {4:P1}, noise {5:P1}",
            baselineMedian, baselineIqr, currentMedian, currentIqr, ratio, noiseRatio);

        if (noiseRatio > MaxIqrRatio)
        {
            Assert.Inconclusive(
                $"IQR above {MaxIqrRatio:P0} of the median on this machine (baseline median {baselineMedian:F1} ms, IQR {baselineIqr:F1}; "
                + $"current median {currentMedian:F1} ms, IQR {currentIqr:F1}; ratio {ratio:P1}). Evidence: {directory}. "
                + "Re-run on an idle machine or the dedicated perf lane — the wall-clock budget cannot be confirmed from this sample.");
            return;
        }

        Assert.IsTrue(ratio <= Budget,
            $"the current canvas must stay inside +{Budget - 1:P0} of the v2.9.0 selection drag "
            + $"(baseline median {baselineMedian:F1} ms, current {currentMedian:F1} ms, ratio {ratio:P1}, noise {noiseRatio:P1}). Evidence: {directory}");
    }

    /// <summary>
    /// Writes one JSON + one markdown per RUN, suffixed with the run's HHmmss, so a second run on the
    /// same day never overwrites the first (the phase-7b escalation depends on keeping an unfavourable
    /// run visible next to a favourable one).
    /// </summary>
    private static async Task WriteEvidenceAsync(string directory, PerfEvidence evidence)
    {
        Directory.CreateDirectory(directory);
        var suffix = DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
        var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(directory, $"selection-drag-ab-{suffix}.json"), json);

        var markdown = new StringBuilder();
        markdown.AppendLine("# Spreadsheet selection-drag A/B (phase 7b)");
        markdown.AppendLine();
        markdown.AppendLine($"- Run: {evidence.Timestamp}");
        markdown.AppendLine($"- Baseline arm: `{evidence.BaselineTag}` spreadsheet-canvas.js served through Playwright route interception");
        markdown.AppendLine($"- Alternations: {evidence.Alternations} per arm, {evidence.Steps} selection drags per probe");
        markdown.AppendLine($"- Budget: median(current) <= {evidence.Budget:P0} x median(baseline); Inconclusive when IQR > {evidence.MaxIqrRatio:P0} of the median");
        markdown.AppendLine();
        markdown.AppendLine("| Arm | Median (ms) | Q1 | Q3 | IQR |");
        markdown.AppendLine("|---|---|---|---|---|");
        markdown.AppendLine($"| baseline (v2.9.0) | {evidence.BaselineMedianMs:F1} | {evidence.BaselineQ1Ms:F1} | {evidence.BaselineQ3Ms:F1} | {evidence.BaselineIqrMs:F1} |");
        markdown.AppendLine($"| current (HEAD) | {evidence.CurrentMedianMs:F1} | {evidence.CurrentQ1Ms:F1} | {evidence.CurrentQ3Ms:F1} | {evidence.CurrentIqrMs:F1} |");
        markdown.AppendLine();
        markdown.AppendLine($"- Ratio current/baseline: **{evidence.Ratio:P1}**");
        markdown.AppendLine($"- Noise (max IQR/median): {evidence.NoiseRatio:P1} (Inconclusive above {evidence.MaxIqrRatio:P0})");
        markdown.AppendLine();
        markdown.AppendLine("## Deterministic counters (current arm, per probe)");
        markdown.AppendLine();
        markdown.AppendLine("| # | Selection paints | Header paints | Content paints | Changed cells | Segment ms | lastHeaderLayerMs |");
        markdown.AppendLine("|---|---|---|---|---|---|---|");
        var index = 0;
        foreach (var sample in evidence.Samples.Where(sample => sample.Arm == "current"))
        {
            index++;
            markdown.AppendLine(
                $"| {index} | {sample.Probe.SelectionPaintFrameCount} | {sample.Probe.HeaderLayerPaintCount} | "
                + $"{sample.Probe.ContentLayerPaintCount} | {sample.Probe.ChangedCells} | {sample.Probe.DurationMs:F1} | "
                + $"{sample.Probe.LastHeaderLayerMs:F2} |");
        }

        markdown.AppendLine();
        markdown.AppendLine("## Raw samples");
        markdown.AppendLine();
        markdown.AppendLine("| Round | Arm | Duration (ms) | Selection paints | Header paints | Content paints | Changed cells |");
        markdown.AppendLine("|---|---|---|---|---|---|---|");
        index = 0;
        foreach (var sample in evidence.Samples)
        {
            index++;
            markdown.AppendLine(
                $"| {index} | {sample.Arm} | {sample.Probe.DurationMs:F1} | {sample.Probe.SelectionPaintFrameCount} | "
                + $"{sample.Probe.HeaderLayerPaintCount} | {sample.Probe.ContentLayerPaintCount} | {sample.Probe.ChangedCells} |");
        }

        await File.WriteAllTextAsync(Path.Combine(directory, $"summary-{suffix}.md"), markdown.ToString());
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Where(value => double.IsFinite(value)).OrderBy(value => value).ToList();
        if (sorted.Count == 0) return 0;
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>Quartiles with the median-of-halves rule (an even sample splits cleanly, no interpolation drift).</summary>
    private static (double Q1, double Q3) Quartiles(IEnumerable<double> values)
    {
        var sorted = values.Where(value => double.IsFinite(value)).OrderBy(value => value).ToList();
        if (sorted.Count == 0) return (0, 0);
        if (sorted.Count == 1) return (sorted[0], sorted[0]);
        var middle = sorted.Count / 2;
        var lower = sorted.Take(middle).ToList();
        var upper = sorted.Skip(sorted.Count % 2 == 0 ? middle : middle + 1).ToList();
        return (Median(lower), Median(upper));
    }

    private static DirectoryInfo RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            directory = directory.Parent;
        Assert.IsNotNull(directory, "the repository root (TempoBlazor.slnx) must be reachable from the test binary");
        return directory!;
    }

    public sealed record PerfArmSample(string Arm, SelectionDragProbeResult Probe);

    public sealed record PerfEvidence(
        string Timestamp,
        string BaselineTag,
        int Alternations,
        int Steps,
        double Budget,
        double MaxIqrRatio,
        double BaselineMedianMs,
        double CurrentMedianMs,
        double BaselineQ1Ms,
        double BaselineQ3Ms,
        double CurrentQ1Ms,
        double CurrentQ3Ms,
        double BaselineIqrMs,
        double CurrentIqrMs,
        double NoiseRatio,
        double Ratio,
        List<PerfArmSample> Samples);

    public sealed class SelectionDragProbeResult
    {
        public double DurationMs { get; set; }
        public int Steps { get; set; }
        public bool Usable { get; set; }
        public int ChangedCells { get; set; }
        public double SelectionPaintFrameCount { get; set; }
        public double HeaderLayerPaintCount { get; set; }
        public double ContentLayerPaintCount { get; set; }
        public double RedrawCount { get; set; }
        public double LastHeaderLayerMs { get; set; }
        public double LastSelectionLayerMs { get; set; }
    }

    private sealed record ArmRun(string Arm, SelectionDragProbeResult Probe);
}
