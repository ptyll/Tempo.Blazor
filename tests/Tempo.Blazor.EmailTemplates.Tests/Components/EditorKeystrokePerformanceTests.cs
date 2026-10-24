using System.Diagnostics;
using Bunit;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Components;
using Xunit.Abstractions;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Phase 7 evidence for the "&lt; 100 ms per keystroke in Server mode" target, in-process half: a 200-block
/// document with the inspector open, one coalesced text-property keystroke (<c>CommitAsync("props")</c>:
/// history snapshot + dirty check + <c>DocumentChanged</c> + auto-save scheduling + render), timed end to end
/// through bUnit's synchronous input dispatch. This is the SERVER-side cost of a keystroke (there is no
/// network in it); the browser round trip is measured by <c>EmailEditorKeystrokePerformanceE2ETests</c>.
/// <para>
/// Run in Release for meaningful numbers: <c>dotnet test -c Release --filter Category=Perf</c>. The assertion is
/// generous ceilings that catch a regression of the order of magnitude, not a benchmark: p95 &lt; 50 ms for the model
/// half (input to <c>DocumentChanged</c>) and p95 &lt; 100 ms for commit + render. The render half is inflated by
/// bUnit's test renderer, which repaints the whole tree and skips <c>ShouldRender</c> (see
/// <c>TmEmailCanvasPerformanceTests</c>); the browser spec measures the production path. The p50/p95 table is written
/// to the test output.
/// </para>
/// </summary>
[Collection(PerfCollection.Name)]
public class EditorKeystrokePerformanceTests : EmailEditorTestBase
{
    private const int BlockCount = 200;
    private const int Keystrokes = 60;
    private const int Warmup = 10;

    private readonly ITestOutputHelper _output;

    public EditorKeystrokePerformanceTests(ITestOutputHelper output) => _output = output;

    private static EmailTemplateDocument LargeDocument(out EmailButtonBlock target)
    {
        var document = new EmailTemplateDocument { Subject = "Perf" };
        target = new EmailButtonBlock { Text = "Go", Href = "https://example.com" };
        for (var s = 0; s < 10; s++)
        {
            var section = new EmailSection();
            var column = new EmailColumn();
            for (var i = 0; i < BlockCount / 10; i++)
            {
                // The edited block sits in the middle of the document, so "rebuild everything" would show up.
                if (s == 5 && i == 10) column.Blocks.Add(target);
                else column.Blocks.Add(new EmailTextBlock { Content = $"<p>Row {s}-{i} of the body copy</p>" });
            }

            section.Columns.Add(column);
            document.Sections.Add(section);
        }

        return document;
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void A_coalesced_text_keystroke_on_200_blocks_commits_and_renders_within_the_ceiling()
    {
        var document = LargeDocument(out var target);
        long commitDone = 0;
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved()))
            .Add(c => c.DocumentChanged, (EmailTemplateDocument _) => commitDone = Stopwatch.GetTimestamp()));
        cut.FindAll("[data-tm-block-id]").Should().HaveCount(BlockCount);

        cut.Find($"[data-tm-block-id=\"{target.Id}\"]").Click();
        // The field's own callback is invoked on the renderer's dispatcher, exactly what a browser `input` event does.
        // Going through IElement.Input() would add bUnit's per-event DOM re-parse of the whole 200-block markup
        // (measured: ~70 ms of AngleSharp work that no browser or circuit pays).
        var field = cut.FindComponents<Tempo.Blazor.Components.Inputs.TmTextInput>()
            .Single(c => c.Instance.Value == "Go"); // the button's Text field (Href is a URL)

        var text = "Go";
        var samples = new List<double>(Keystrokes);
        var commits = new List<double>(Keystrokes);
        for (var i = 0; i < Warmup + Keystrokes; i++)
        {
            text += (char)('a' + i % 26);
            var start = Stopwatch.GetTimestamp();
            cut.InvokeAsync(() => field.Instance.ValueChanged.InvokeAsync(text)).GetAwaiter().GetResult();
            var end = Stopwatch.GetTimestamp();
            if (i < Warmup) continue;
            samples.Add(Stopwatch.GetElapsedTime(start, end).TotalMilliseconds);
            // CommitAsync up to the DocumentChanged emission: history snapshot + dirty check, no render.
            commits.Add(Stopwatch.GetElapsedTime(start, commitDone).TotalMilliseconds);
        }

        target.Text.Should().Be(text, "every keystroke reached the model");
        samples.Sort();
        commits.Sort();
        var c50 = Percentile(commits, 0.50);
        var c95 = Percentile(commits, 0.95);
        var p50 = Percentile(samples, 0.50);
        var p95 = Percentile(samples, 0.95);
        var max = samples[^1];
        var build = typeof(EditorKeystrokePerformanceTests).Assembly
            .GetCustomAttributes(typeof(DebuggableAttribute), false)
            .Cast<DebuggableAttribute>().Any(a => a.IsJITOptimizerDisabled) ? "Debug" : "Release";
        _output.WriteLine(
            $"KEYSTROKE-PERF bUnit {build} blocks={BlockCount} n={samples.Count} p50={p50:F2} ms p95={p95:F2} ms max={max:F2} ms | commit-only(input->DocumentChanged) p50={c50:F2} ms p95={c95:F2} ms");

        // Debug code is several times slower and says nothing about the target: the numbers are reported, the
        // ceilings are asserted for the optimized build only (dotnet test -c Release --filter Category=Perf).
        if (build == "Debug") return;

        c95.Should().BeLessThan(50,
            $"the model half of a keystroke (history snapshot + dirty check + emission) on {BlockCount} blocks "
            + $"(measured {build}: p50 {c50:F2} ms, p95 {c95:F2} ms)");
        p95.Should().BeLessThan(100,
            $"commit + render of one coalesced keystroke on {BlockCount} blocks must meet the 100 ms target even in "
            + $"process under a test renderer that repaints the whole tree (measured {build}: p50 {p50:F2} ms, p95 {p95:F2} ms)");
    }

    private static double Percentile(List<double> sorted, double fraction)
        => sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(fraction * sorted.Count) - 1)];
}

/// <summary>Runs the wall-clock specs alone (xUnit runs a non-parallel collection after the parallel ones finish).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PerfCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "Perf";
}
