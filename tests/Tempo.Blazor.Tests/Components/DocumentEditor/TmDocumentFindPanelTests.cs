using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public sealed class TmDocumentFindPanelTests : LocalizationTestBase
{
    private static DocumentEditorDocument EmptyDocument() => new();

    // ─── Rendering ───────────────────────────────────────────────────────────

    [Fact]
    public void Panel_RendersWithTestId()
    {
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument()));

        cut.Find("[data-testid='document-find-panel']").Should().NotBeNull();
    }

    [Fact]
    public void Panel_HasSearchInput()
    {
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument()));

        cut.Find("[data-testid='document-find-input']").Should().NotBeNull();
    }

    [Fact]
    public void Panel_HasNextAndPreviousButtons()
    {
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument()));

        cut.Find("[data-testid='document-find-next']").Should().NotBeNull();
        cut.Find("[data-testid='document-find-prev']").Should().NotBeNull();
    }

    [Fact]
    public void Panel_HasCloseButton()
    {
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument()));

        cut.Find("[data-testid='document-find-close']").Should().NotBeNull();
    }

    // ─── Show/Hide replace ───────────────────────────────────────────────────

    [Fact]
    public void Panel_ReplaceRowHiddenByDefault()
    {
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument()));

        cut.FindAll("[data-testid='document-replace-input']").Should().BeEmpty();
    }

    [Fact]
    public void Panel_ShowReplace_ShowsReplaceRow()
    {
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument())
            .Add(x => x.ShowReplace, true));

        cut.Find("[data-testid='document-replace-input']").Should().NotBeNull();
    }

    // ─── Result count ────────────────────────────────────────────────────────

    [Fact]
    public void Panel_WithNoDocument_ShowsNoResultsText()
    {
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument()));

        var count = cut.Find("[data-testid='document-find-count']");
        count.TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Panel_Search_ShowsResultCount()
    {
        var doc = new DocumentEditorDocument
        {
            Blocks =
            [
                new DocumentBlock
                {
                    Id = "b1",
                    Content = new ParagraphBlockContent
                    {
                        Inlines = [new TextRun { Text = "Hello world, hello!" }]
                    }
                }
            ]
        };

        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, doc));

        cut.Find("[data-testid='document-find-input']").Input("hello");

        // N3.4: the search is debounced, so the count updates shortly after typing settles.
        cut.WaitForAssertion(
            () => cut.Find("[data-testid='document-find-count']").TextContent.Should().Contain("2"),
            TimeSpan.FromSeconds(3));
    }

    // ─── Debounce (perf plan N3.4) ────────────────────────────────────────────

    [Fact]
    public void Panel_SearchInput_IsDebounced_RapidTypingRunsSingleSearch()
    {
        var doc = new DocumentEditorDocument
        {
            Blocks =
            [
                new DocumentBlock
                {
                    Id = "b1",
                    Content = new ParagraphBlockContent
                    {
                        Inlines = [new TextRun { Text = "hello helper hell" }]
                    }
                }
            ]
        };
        var searches = new List<string>();
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.OnSearchRequested, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<DocumentSearchQuery>(
                this, query => searches.Add(query.Text))));

        var input = cut.Find("[data-testid='document-find-input']");
        input.Input("h");
        input.Input("he");
        input.Input("hel");

        // The full-document search must NOT run synchronously per keystroke...
        searches.Should().BeEmpty("the find fulltext is debounced, not per-keystroke");

        // ...and after the debounce settles, only the LAST query ran.
        cut.WaitForAssertion(
            () => searches.Should().Equal("hel"),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Panel_EnterFlushesPendingDebouncedSearchAndNavigates()
    {
        var doc = new DocumentEditorDocument
        {
            Blocks =
            [
                new DocumentBlock
                {
                    Id = "b1",
                    Content = new ParagraphBlockContent
                    {
                        Inlines = [new TextRun { Text = "cat and cat" }]
                    }
                }
            ]
        };
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, doc));

        cut.Find("[data-testid='document-find-input']").Input("cat");
        // Enter right after typing: the pending debounced search must flush immediately so
        // navigation works without waiting — active result advances to 2 of 2.
        // Enter navigation is scoped to the text inputs (the panel's buttons are
        // native <button>s — a container-level Enter would double-fire with
        // their native keydown -> click).
        cut.Find("[data-testid='document-find-input']")
            .KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(
            () => cut.Find("[data-testid='document-find-count']").TextContent.Should().Contain("2 of 2"),
            TimeSpan.FromSeconds(3));
    }

    // ─── Close callback ───────────────────────────────────────────────────────

    [Fact]
    public void Panel_CloseButton_InvokesOnClose()
    {
        var closed = false;
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument())
            .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        cut.Find("[data-testid='document-find-close']").Click();

        closed.Should().BeTrue();
    }

    [Fact]
    public void Panel_EscapeKey_InvokesOnClose()
    {
        var closed = false;
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument())
            .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        cut.Find("[data-testid='document-find-panel']")
            .KeyDown(new KeyboardEventArgs { Key = "Escape" });

        closed.Should().BeTrue();
    }

    // ─── Navigation ──────────────────────────────────────────────────────────

    [Fact]
    public void Panel_NextButton_AdvancesActiveIndex()
    {
        var doc = new DocumentEditorDocument
        {
            Blocks =
            [
                new DocumentBlock
                {
                    Id = "b1",
                    Content = new ParagraphBlockContent
                    {
                        Inlines = [new TextRun { Text = "cat and cat" }]
                    }
                }
            ]
        };

        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, doc));

        cut.Find("[data-testid='document-find-input']").Input("cat");
        cut.Find("[data-testid='document-find-next']").Click();

        // After clicking next once with 2 results, active index should be 1 → "2 of 2"
        var count = cut.Find("[data-testid='document-find-count']");
        count.TextContent.Should().Contain("2 of 2");
    }

    [Fact]
    public void Panel_EnterOnNextButton_NavigatesExactlyOnce()
    {
        // Native <button> sequence: keydown -> click. A root-level Enter case
        // would navigate on the bubbled keydown too, advancing two results for
        // one key press.
        var doc = new DocumentEditorDocument
        {
            Blocks =
            [
                new DocumentBlock
                {
                    Id = "b1",
                    Content = new ParagraphBlockContent
                    {
                        Inlines = [new TextRun { Text = "cat and cat and cat" }]
                    }
                }
            ]
        };
        var navigations = new List<int>();
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.OnNavigateRequested, EventCallback.Factory.Create<int>(this, d => navigations.Add(d))));

        cut.Find("[data-testid='document-find-input']").Input("cat");
        cut.WaitForAssertion(
            () => cut.Find("[data-testid='document-find-count']").TextContent.Should().Contain("1 of 3"),
            TimeSpan.FromSeconds(3));

        var next = cut.Find("[data-testid='document-find-next']");
        next.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find("[data-testid='document-find-next']").Click();

        cut.WaitForAssertion(
            () => navigations.Should().Equal(1),
            TimeSpan.FromSeconds(3));
        cut.Find("[data-testid='document-find-count']").TextContent.Should().Contain("2 of 3");
    }

    [Fact]
    public void Panel_EnterOnCloseButton_DoesNotNavigate()
    {
        // Enter on the close button must only close — a bubbled Enter that
        // also ran GoToNext would emit OnNavigateRequested for a gesture the
        // user meant as "close".
        var doc = new DocumentEditorDocument
        {
            Blocks =
            [
                new DocumentBlock
                {
                    Id = "b1",
                    Content = new ParagraphBlockContent
                    {
                        Inlines = [new TextRun { Text = "cat and cat" }]
                    }
                }
            ]
        };
        var closes = 0;
        var navigations = 0;
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => closes++))
            .Add(x => x.OnNavigateRequested, EventCallback.Factory.Create<int>(this, _ => navigations++)));

        cut.Find("[data-testid='document-find-input']").Input("cat");
        cut.WaitForAssertion(
            () => cut.Find("[data-testid='document-find-count']").TextContent.Should().Contain("1 of 2"),
            TimeSpan.FromSeconds(3));

        var close = cut.Find("[data-testid='document-find-close']");
        close.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find("[data-testid='document-find-close']").Click();

        cut.WaitForAssertion(() => closes.Should().Be(1), TimeSpan.FromSeconds(3));
        navigations.Should().Be(0);
    }

    [Fact]
    public void Panel_EscapeOnButton_ClosesOnce()
    {
        // Escape stays on the root so it works from any focused control, but
        // the inputs stop their own keydowns — one Escape gesture, one OnClose.
        var closes = 0;
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument())
            .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => closes++)));

        cut.Find("[data-testid='document-find-close']")
            .KeyDown(new KeyboardEventArgs { Key = "Escape" });

        cut.WaitForAssertion(() => closes.Should().Be(1), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Panel_EscapeInSearchInput_ClosesExactlyOnce()
    {
        // The input handles Escape itself and stops the keydown from also
        // reaching the root's Escape case — one gesture, one OnClose.
        var closes = 0;
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument())
            .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => closes++)));

        cut.Find("[data-testid='document-find-input']")
            .KeyDown(new KeyboardEventArgs { Key = "Escape" });

        cut.WaitForAssertion(() => closes.Should().Be(1), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Panel_RendersSearchScopeSelector()
    {
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument()));

        var select = cut.Find("[data-testid='document-find-scope']");
        select.TextContent.Should().Contain("Body");
        select.TextContent.Should().Contain("Headers and footers");
        select.TextContent.Should().Contain("Comments");
    }

    [Fact]
    public void Panel_ScopeSelector_SearchesHeaderFooter()
    {
        var doc = new DocumentEditorDocument
        {
            Blocks =
            [
                new DocumentBlock
                {
                    Id = "b1",
                    Content = new ParagraphBlockContent { Inlines = [new TextRun { Text = "body" }] }
                }
            ],
            HeadersFooters =
            [
                new DocumentHeaderFooter
                {
                    Blocks =
                    [
                        new DocumentBlock
                        {
                            Id = "h1",
                            Content = new ParagraphBlockContent { Inlines = [new TextRun { Text = "header secret" }] }
                        }
                    ]
                }
            ]
        };

        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, doc));

        cut.Find("[data-testid='document-find-input']").Input("secret");
        cut.Find("[data-testid='document-find-scope']").Change(DocumentSearchScope.HeadersFooters.ToString());

        cut.Find("[data-testid='document-find-count']").TextContent.Should().Contain("1 of 1");
    }

    [Fact]
    public void Panel_MultipleResults_RendersClickableResultList()
    {
        DocumentSearchResult? active = null;
        var doc = new DocumentEditorDocument
        {
            Blocks =
            [
                new DocumentBlock
                {
                    Id = "b1",
                    Content = new ParagraphBlockContent
                    {
                        Inlines = [new TextRun { Text = "cat dog cat" }]
                    }
                }
            ]
        };

        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.OnActiveResultChanged, EventCallback.Factory.Create<DocumentSearchResult>(this, r => active = r)));

        cut.Find("[data-testid='document-find-input']").Input("cat");
        // N3.4: the search is debounced, so the result list appears shortly after typing settles.
        cut.WaitForAssertion(
            () => cut.FindAll("[data-testid='document-find-result']").Should().HaveCount(2),
            TimeSpan.FromSeconds(3));
        cut.FindAll("[data-testid='document-find-result']")[1].Click();

        // Load-sensitive: under full-suite CPU contention the click's callback can land a beat after
        // Click() returns — wait for the final state instead of asserting the instantaneous one.
        cut.WaitForAssertion(
            () =>
            {
                active.Should().NotBeNull();
                active!.Index.Should().Be(1);
            },
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Panel_ReplaceOne_RequestsRuntimeBridgeAndDoesNotMutateDocumentDirectly()
    {
        DocumentFindReplaceRequest? request = null;
        var doc = new DocumentEditorDocument
        {
            Blocks =
            [
                new DocumentBlock
                {
                    Id = "b1",
                    Content = new ParagraphBlockContent
                    {
                        Inlines = [new TextRun { Text = "hello world" }]
                    }
                }
            ]
        };

        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ShowReplace, true)
            .Add(x => x.OnReplaceOneRequested, EventCallback.Factory.Create<DocumentFindReplaceRequest>(this, r => request = r)));

        cut.Find("[data-testid='document-find-input']").Input("world");
        cut.Find("[data-testid='document-replace-input']").Change("Tempo");
        cut.Find("[data-testid='document-find-replace-one']").Click();

        request.Should().NotBeNull();
        request!.Replacement.Should().Be("Tempo");
        request.ActiveResult!.BlockId.Should().Be("b1");
        ((ParagraphBlockContent)doc.Blocks[0].Content).Inlines.OfType<TextRun>().Single().Text.Should().Be("hello world");
    }

    // ─── ARIA / accessibility ─────────────────────────────────────────────────

    [Fact]
    public void Panel_HasRoleAndAriaLabel()
    {
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument()));

        var panel = cut.Find("[data-testid='document-find-panel']");
        panel.GetAttribute("role").Should().Be("search");
    }

    [Fact]
    public void Panel_NextButton_DisabledWhenNoResults()
    {
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument()));

        var nextBtn = cut.Find("[data-testid='document-find-next']");
        nextBtn.HasAttribute("disabled").Should().BeTrue();
    }
}
