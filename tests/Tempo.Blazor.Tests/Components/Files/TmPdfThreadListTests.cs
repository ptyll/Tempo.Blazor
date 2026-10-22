using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>
/// bUnit tests for the shared <see cref="TmPdfThreadList"/> (PdfViewer redesign phase 4, PD4):
/// both annotation panels delegate their thread cards to this component, so the panel testid
/// contract (<c>{base}-thread</c>, <c>{base}-detail</c>, <c>{base}-reply-*</c>, …), the resolved
/// filter and the HideCommentKind behaviour live here.
/// </summary>
public class TmPdfThreadListTests : LocalizationTestBase
{
    private const string Base = "pdf-annotation";

    private static DocumentCommentThread Thread(
        string id,
        DocumentAnnotationKind kind = DocumentAnnotationKind.Comment,
        DocumentCommentThreadStatus status = DocumentCommentThreadStatus.Open)
        => new()
        {
            Id = id,
            Kind = kind,
            Anchor = DocumentCommentAnchor.Point(1, 0.1, 0.1),
            Comments = [new DocumentComment { Id = id + "-c", AuthorId = "a", AuthorName = "Alice", Body = "note" }]
        };

    private IRenderedComponent<TmPdfThreadList> Render(
        IReadOnlyList<DocumentCommentThread> threads,
        Action<Bunit.ComponentParameterCollectionBuilder<TmPdfThreadList>>? configure = null)
    {
        return Render<TmPdfThreadList>(p =>
        {
            p.Add(x => x.Threads, threads);
            p.Add(x => x.TestIdBase, Base);
            p.Add(x => x.ReplyPlaceholder, "Reply…");
            p.Add(x => x.ReplySubmitLabel, "Reply");
            p.Add(x => x.ResolveLabel, "Resolve");
            p.Add(x => x.ReopenLabel, "Reopen");
            configure?.Invoke(p);
        });
    }

    [Fact]
    public void RendersCardsWithThePanelTestIdContract()
    {
        string? selected = null;
        var cut = Render([Thread("t1"), Thread("t2")], p => p
            .Add(x => x.SelectedThreadIdChanged, EventCallback.Factory.Create<string?>(this, v => selected = v)));

        cut.FindAll($"[data-testid='{Base}-thread']").Should().HaveCount(2);
        cut.FindAll($"[data-testid='{Base}-reply-input']").Should().BeEmpty("collapsed cards hide comments and composer");

        cut.Find($"[data-testid='{Base}-thread']").Click();
        selected.Should().Be("t1", "the card header toggles selection through SelectedThreadIdChanged");

        // Selection is controlled: the host echoes the id back through the parameter.
        cut.InvokeAsync(async () => await cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object> { [nameof(TmPdfThreadList.SelectedThreadId)] = "t1" })
        )).GetAwaiter().GetResult();

        cut.FindAll($"[data-testid='{Base}-detail']").Should().HaveCount(1);
        cut.FindAll($"[data-testid='{Base}-comment']").Should().HaveCount(1);
        cut.FindAll($"[data-testid='{Base}-reply-input']").Should().HaveCount(1);
        cut.FindAll($"[data-testid='{Base}-resolve']").Should().HaveCount(1);
    }

    [Fact]
    public void ResolvedThreads_HideUnlessShowResolved()
    {
        var resolved = Thread("t1");
        resolved.Status = DocumentCommentThreadStatus.Resolved;

        var cut = Render([resolved]);
        cut.FindAll($"[data-testid='{Base}-thread']").Should().BeEmpty();

        cut.InvokeAsync(async () => await cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object> { [nameof(TmPdfThreadList.ShowResolved)] = true })
        )).GetAwaiter().GetResult();
        cut.FindAll($"[data-testid='{Base}-thread']").Should().HaveCount(1);
    }

    [Fact]
    public void EmptyState_RendersTheGivenText()
    {
        var cut = Render([], p => p.Add(x => x.EmptyText, "No comments yet"));

        cut.Find($"[data-testid='{Base}-empty']").TextContent.Should().Be("No comments yet");
        cut.FindAll(".tm-pdf-thread-list").Should().BeEmpty();
    }

    [Fact]
    public void HideCommentKind_DropsOnlyThePlainCommentCaption()
    {
        var comment = Render([Thread("t1", DocumentAnnotationKind.Comment)], p => p.Add(x => x.HideCommentKind, true));
        comment.FindAll(".tm-pdf-thread-card__kind").Should().BeEmpty("plain comments carry no kind caption in the viewer panel");

        var highlight = Render([Thread("t2", DocumentAnnotationKind.Highlight)], p => p.Add(x => x.HideCommentKind, true));
        highlight.Find(".tm-pdf-thread-card__kind").TextContent.Should().Be("Highlight");

        // The annotator keeps the caption for every kind, including plain comments.
        var annotator = Render([Thread("t3", DocumentAnnotationKind.Comment)]);
        annotator.Find(".tm-pdf-thread-card__kind").TextContent.Should().Be("Comment");
    }

    [Fact]
    public void ColorResolver_OverridesTheDefaultBadgeColor()
    {
        var cut = Render([Thread("t1")], p => p.Add(x => x.ColorResolver, _ => "#ff0066"));

        cut.Find(".tm-pdf-thread-card__badge").GetAttribute("style").Should().Contain("#ff0066");
    }

    // ── FZE-12: the shared thread filter ("All | Unresolved" segmented radio) ──

    [Fact]
    public void ThreadFilter_RendersAllAndUnresolvedSegments_WithCounts()
    {
        var threads = new[] { Thread("t1"), Thread("t2"), Resolved("t3") };

        var cut = Render<TmPdfThreadFilter>(p => p
            .Add(x => x.Threads, threads)
            .Add(x => x.TestIdBase, Base)
            .Add(x => x.ShowResolved, true));

        cut.Find("[data-testid='pdf-annotation-filter']").GetAttribute("role").Should().Be("radiogroup");
        cut.Find("[data-testid='pdf-annotation-filter-all']").GetAttribute("type").Should().Be("radio");
        cut.Find("[data-testid='pdf-annotation-filter-unresolved']").GetAttribute("type").Should().Be("radio");

        var filterText = cut.Find("[data-testid='pdf-annotation-filter']").TextContent;
        filterText.Should().Contain("All (3)", "the All segment counts every thread");
        filterText.Should().Contain("Unresolved (2)", "the Unresolved segment counts the open threads");
        cut.Find("[data-testid='pdf-annotation-filter-all']").HasAttribute("checked").Should().BeTrue(
            "ShowResolved=true is the All segment (the pre-FZE-12 checkbox's checked state)");
    }

    [Fact]
    public void ThreadFilter_IsRadioGroup_ArrowKeysMoveSelection()
    {
        // Keyboard convention: the two segments are NATIVE radios sharing one group name, so the
        // browser owns the arrow keys and moves the selection itself — the component adds no
        // emulation (exactly the TmRadioGroup Appearance=Segmented model the filter mirrors).
        var cut = Render<TmPdfThreadFilter>(p => p
            .Add(x => x.Threads, new[] { Thread("t1") })
            .Add(x => x.TestIdBase, Base));

        var all = cut.Find("[data-testid='pdf-annotation-filter-all']");
        var unresolved = cut.Find("[data-testid='pdf-annotation-filter-unresolved']");

        all.GetAttribute("name").Should().NotBeNullOrEmpty();
        unresolved.GetAttribute("name").Should().Be(all.GetAttribute("name"),
            "native radios sharing a name give the arrow keys to the browser (no emulation)");
        cut.Find("[data-testid='pdf-annotation-filter']").GetAttribute("aria-label")
            .Should().NotBeEmpty("the group is named for assistive tech");
    }

    [Fact]
    public void ThreadFilter_CountsFollowThreadMutations()
    {
        var threads = new List<DocumentCommentThread> { Thread("t1"), Thread("t2") };
        var cut = Render<TmPdfThreadFilter>(p => p
            .Add(x => x.Threads, threads)
            .Add(x => x.TestIdBase, Base));

        cut.Find("[data-testid='pdf-annotation-filter']").TextContent.Should().Contain("Unresolved (2)");

        // A resolve/reopen mutation hands a new thread set back: the counts recompute live.
        threads[1].Status = DocumentCommentThreadStatus.Resolved;
        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object> { [nameof(TmPdfThreadFilter.Threads)] = threads })
        )).GetAwaiter().GetResult();

        var filterText = cut.Find("[data-testid='pdf-annotation-filter']").TextContent;
        filterText.Should().Contain("All (2)");
        filterText.Should().Contain("Unresolved (1)");
    }

    private static DocumentCommentThread Resolved(string id)
    {
        var thread = Thread(id);
        thread.Status = DocumentCommentThreadStatus.Resolved;
        return thread;
    }
}
