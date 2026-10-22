using System.Diagnostics;
using System.Globalization;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Components;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 9A performance — the WYSIWYG canvas paints the WHOLE document, so a keystroke in the inspector
/// must not rebuild every block. The block renderer owns its own decision (<see cref="EmailCanvasRenderGate"/>,
/// driven by <see cref="EmailBlockSignature"/>): with 200 blocks, one property change lets exactly ONE
/// renderer rebuild.
/// <para>
/// The decision is measured directly instead of through <c>ShouldRender</c>: bUnit's test renderer
/// re-renders the whole tree on <c>Render()</c> and never calls <c>ShouldRender</c> again (measured while
/// writing this phase), so a DOM- or counter-based assertion in bUnit would prove nothing about the
/// production path. The production path itself is pinned in the browser by
/// <c>EmailEditorCanvasWysiwygF9AE2ETests</c>, which observes the canvas with a MutationObserver while
/// typing into the inspector.
/// </para>
/// </summary>
public class TmEmailCanvasPerformanceTests : EmailCanvasTestBase
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>A document with <paramref name="count"/> text blocks spread over 10 sections.</summary>
    private static EmailTemplateDocument LargeDocument(int count, out List<EmailTextBlock> blocks)
    {
        var document = new EmailTemplateDocument();
        blocks = new List<EmailTextBlock>();
        var perSection = Math.Max(1, count / 10);
        for (var s = 0; s < 10; s++)
        {
            var section = new EmailSection();
            var column = new EmailColumn();
            for (var i = 0; i < perSection; i++)
            {
                var text = new EmailTextBlock { Content = $"<p>Row {s}-{i} of the body copy</p>" };
                blocks.Add(text);
                column.Blocks.Add(text);
            }

            section.Columns.Add(column);
            document.Sections.Add(section);
        }

        return document;
    }

    [Fact]
    public void The_first_decision_of_every_block_is_to_render()
    {
        LargeDocument(200, out var blocks);

        var gates = blocks.Select(_ => new EmailCanvasRenderGate()).ToList();

        // Materialised on purpose: a lazy Select would be re-enumerated by an assertion and every gate
        // would answer "unchanged" the second time round.
        var decisions = new List<bool>(blocks.Count);
        for (var i = 0; i < blocks.Count; i++)
        {
            decisions.Add(gates[i].ShouldRender(blocks[i], CanvasImageLoading.Load, nested: false, Culture));
        }

        decisions.Should().AllBeEquivalentTo(true, "a renderer must paint itself at least once");
    }

    [Fact]
    public void Changing_one_block_lets_exactly_that_block_rebuild()
    {
        LargeDocument(200, out var blocks);
        var gates = blocks.Select(_ => new EmailCanvasRenderGate()).ToList();
        for (var i = 0; i < blocks.Count; i++)
        {
            gates[i].ShouldRender(blocks[i], CanvasImageLoading.Load, nested: false, Culture);
        }

        var target = blocks[123];
        target.Content = "<p>Edited row</p>";

        var rebuilt = new List<Guid>();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (gates[i].ShouldRender(blocks[i], CanvasImageLoading.Load, false, Culture)) rebuilt.Add(blocks[i].Id);
        }

        rebuilt.Should().Equal(new[] { target.Id },
            "the canvas re-renders, but only the renderer whose block data changed may rebuild");
    }

    [Fact]
    public void An_unchanged_document_lets_no_block_rebuild()
    {
        LargeDocument(200, out var blocks);
        var gates = blocks.Select(_ => new EmailCanvasRenderGate()).ToList();
        for (var i = 0; i < blocks.Count; i++)
        {
            gates[i].ShouldRender(blocks[i], CanvasImageLoading.Load, nested: false, Culture);
        }

        var rebuilt = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (gates[i].ShouldRender(blocks[i], CanvasImageLoading.Load, false, Culture)) rebuilt++;
        }

        rebuilt.Should().Be(0, "an unrelated parent re-render must not rebuild a single block");
    }

    [Fact]
    public void Every_painted_property_of_a_block_invalidates_its_own_renderer()
    {
        // A property the signature does not cover would be a silent stale canvas (the block looks unchanged
        // although the author changed it). Each case mutates ONE rendered value of one block.
        static EmailButtonBlock Button() => new()
        {
            Text = "Go",
            BackgroundColor = "#414141",
            Href = "https://example.com",
            BorderRadius = "3px",
            Align = "center",
        };

        var mutations = new (string Name, Action<EmailButtonBlock> Mutate)[]
        {
            ("Text", b => b.Text = "Changed"),
            ("BackgroundColor", b => b.BackgroundColor = "#000000"),
            ("Color", b => b.Color = "#111111"),
            ("BorderRadius", b => b.BorderRadius = "10px"),
            ("InnerPadding", b => b.InnerPadding = "2px 4px"),
            ("Align", b => b.Align = "left"),
            ("Padding", b => b.Padding = "1px"),
            ("ContainerBackgroundColor", b => b.ContainerBackgroundColor = "#eeeeee"),
            ("FontSize", b => b.FontSize = "20px"),
            ("Href", b => b.Href = "https://example.com/other"),
        };

        foreach (var (name, mutate) in mutations)
        {
            var block = Button();
            var gate = new EmailCanvasRenderGate();
            gate.ShouldRender(block, CanvasImageLoading.Load, false, Culture).Should().BeTrue();
            gate.ShouldRender(block, CanvasImageLoading.Load, false, Culture)
                .Should().BeFalse($"{name} did not change yet");

            mutate(block);

            gate.ShouldRender(block, CanvasImageLoading.Load, false, Culture)
                .Should().BeTrue($"changing {name} must repaint the block");
        }
    }

    [Fact]
    public void The_image_loading_mode_and_the_nested_flag_invalidate_every_block()
    {
        LargeDocument(3, out var blocks);
        var gate = new EmailCanvasRenderGate();
        gate.ShouldRender(blocks[0], CanvasImageLoading.Load, false, Culture).Should().BeTrue();

        gate.ShouldRender(blocks[0], CanvasImageLoading.Placeholder, false, Culture)
            .Should().BeTrue("switching to placeholder mode must repaint the block");
        gate.ShouldRender(blocks[0], CanvasImageLoading.Placeholder, true, Culture)
            .Should().BeTrue("a block drawn inside a frame paints differently");
        gate.ShouldRender(blocks[0], CanvasImageLoading.Placeholder, true, Culture).Should().BeFalse();
    }

    [Fact]
    public void A_change_of_the_ui_culture_repaints_the_block()
    {
        var block = new EmailRawBlock { Content = "<p>x</p>" };
        var gate = new EmailCanvasRenderGate();
        gate.ShouldRender(block, CanvasImageLoading.Load, false, CultureInfo.GetCultureInfo("en-US")).Should().BeTrue();

        gate.ShouldRender(block, CanvasImageLoading.Load, false, CultureInfo.GetCultureInfo("cs-CZ"))
            .Should().BeTrue("the placeholder labels are localized");
    }

    [Fact]
    public void Deciding_about_200_blocks_after_one_edit_costs_far_less_than_a_full_repaint()
    {
        LargeDocument(200, out var blocks);
        var gates = blocks.Select(_ => new EmailCanvasRenderGate()).ToList();
        for (var i = 0; i < blocks.Count; i++)
        {
            gates[i].ShouldRender(blocks[i], CanvasImageLoading.Load, false, Culture);
        }

        blocks[77].Content = "<p>Measured edit</p>";

        var stopwatch = Stopwatch.StartNew();
        var rebuilt = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (gates[i].ShouldRender(blocks[i], CanvasImageLoading.Load, false, Culture)) rebuilt++;
        }

        stopwatch.Stop();

        rebuilt.Should().Be(1);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(50),
            $"the per-keystroke change detection on 200 blocks must be negligible (measured "
            + $"{stopwatch.Elapsed.TotalMilliseconds:F2} ms)");
    }

    [Fact]
    public void The_document_renderer_consults_the_gate_for_every_block()
    {
        // End-to-end wiring in bUnit: the component's decision method must answer "paint" once and then
        // "nothing changed" for the same data, and "paint" again for the block that changed. (ShouldRender
        // forwards to exactly this method; bUnit's test renderer re-renders the whole tree and skips
        // ShouldRender, so the DOM cannot be the witness — the browser spec observes the real one.)
        LargeDocument(20, out var blocks);
        var cut = RenderCanvas(DocumentOf(blocks));
        var components = cut.FindComponents<TmEmailCanvasBlock>();
        components.Should().HaveCount(20);

        foreach (var component in components)
        {
            // Review 5: the gate is primed in OnInitialized, so the initial paint is already accounted for —
            // the first parent re-render must NOT rebuild every block (that was the measured defect).
            component.Instance.RenderCount.Should().Be(1, "the initial render is the one recorded paint");
            component.Instance.ShouldRenderNow().Should().BeFalse("the initial render already painted this data");
        }

        blocks[5].Content = "<p>Changed</p>";

        var repaint = components.Where(c => c.Instance.ShouldRenderNow()).ToList();
        repaint.Should().ContainSingle("only the block whose data changed repaints");
        repaint[0].Instance.Block.Id.Should().Be(blocks[5].Id);
    }

    [Fact]
    public void The_initial_render_of_a_200_block_document_is_under_five_seconds()
    {
        LargeDocument(200, out var blocks);
        var stopwatch = Stopwatch.StartNew();
        var cut = RenderCanvas(DocumentOf(blocks));
        stopwatch.Stop();

        cut.FindAll("[data-tm-block-id]").Should().HaveCount(200);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            $"a large document still has to open (measured {stopwatch.Elapsed.TotalMilliseconds:F1} ms)");
    }

    private static EmailTemplateDocument DocumentOf(List<EmailTextBlock> blocks)
    {
        var document = new EmailTemplateDocument();
        var column = new EmailColumn();
        foreach (var block in blocks) column.Blocks.Add(block);
        var section = new EmailSection();
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }
}
