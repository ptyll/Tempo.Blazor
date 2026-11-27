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
            () => cut.Find("[data-testid='document-find-count']").TextContent.Should().Contain("2"));
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
        // Dispatch the keystrokes inside one renderer turn: while the delegate occupies the
        // dispatcher, the Task.Delay debounce continuations cannot interleave between keystrokes.
        // Without this, a saturated dispatcher (>200 ms per dispatched event on a loaded CI
        // runner) let the debounce fire after "h" and the assertion below went red as a flake.
        cut.InvokeAsync(() =>
        {
            input.Input("h");
            input.Input("he");
            input.Input("hel");
        }).Wait();

        // The full-document search must NOT run synchronously per keystroke...
        searches.Should().BeEmpty("the find fulltext is debounced, not per-keystroke");

        // ...and after the debounce settles, only the LAST query ran. No per-call timeout: the wait
        // runs on the suite's budget (TestAssemblyInit, TEMPO_BUNIT_WAIT_SECONDS — 10 s in CI). A fixed
        // 3 s here overrode that budget and was the CI red of run 35924021523 (WaitForFailedException,
        // check count 1: no render arrived within 3 s on a loaded runner). Reproduced by holding the
        // renderer's dispatcher for 3.5 s after the keystrokes: red with the fixed 3 s, green on the
        // 10 s budget.
        cut.WaitForAssertion(() => searches.Should().Equal("hel"));
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
            () => cut.Find("[data-testid='document-find-count']").TextContent.Should().Contain("2 of 2"));
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
        // The search is debounced (200 ms): clicking "next" before it ran would step through zero results, which is
        // exactly what a loaded machine did. Wait for the result count, then step.
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-find-count']").TextContent.Should().Contain("1 of 2"));
        cut.Find("[data-testid='document-find-next']").Click();

        // After clicking next once with 2 results, active index should be 1 → "2 of 2"
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-find-count']").TextContent.Should().Contain("2 of 2"));
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
            () => cut.Find("[data-testid='document-find-count']").TextContent.Should().Contain("1 of 3"));

        var next = cut.Find("[data-testid='document-find-next']");
        next.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find("[data-testid='document-find-next']").Click();

        cut.WaitForAssertion(
            () => navigations.Should().Equal(1));
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
            () => cut.Find("[data-testid='document-find-count']").TextContent.Should().Contain("1 of 2"));

        var close = cut.Find("[data-testid='document-find-close']");
        close.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find("[data-testid='document-find-close']").Click();

        cut.WaitForAssertion(() => closes.Should().Be(1));
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

        cut.WaitForAssertion(() => closes.Should().Be(1));
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

        cut.WaitForAssertion(() => closes.Should().Be(1));
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
            () => cut.FindAll("[data-testid='document-find-result']").Should().HaveCount(2));
        cut.FindAll("[data-testid='document-find-result']")[1].Click();

        // Load-sensitive: under full-suite CPU contention the click's callback can land a beat after
        // Click() returns — wait for the final state instead of asserting the instantaneous one.
        cut.WaitForAssertion(
            () =>
            {
                active.Should().NotBeNull();
                active!.Index.Should().Be(1);
            });
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
    // ─── Redesign 146c7e1b, phase 6 (DE-2.3) ──────────────────────────────────

    private static DocumentEditorDocument DocumentWith(params string[] paragraphs) => new()
    {
        Blocks =
        [
            .. paragraphs.Select((text, index) => new DocumentBlock
            {
                Id = $"b{index + 1}",
                Content = new ParagraphBlockContent { Inlines = [new TextRun { Text = text }] }
            })
        ]
    };

    private IRenderedComponent<TmDocumentFindPanel> RenderSearched(string query, DocumentEditorDocument document, Action<ComponentParameterCollectionBuilder<TmDocumentFindPanel>>? configure = null)
    {
        var cut = Render<TmDocumentFindPanel>(p =>
        {
            p.Add(x => x.Document, document);
            configure?.Invoke(p);
        });
        cut.Find("[data-testid='document-find-input']").Input(query);
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-find-result']").Should().NotBeEmpty());
        return cut;
    }

    [Fact]
    public void Result_RendersTheMatchInAMark_WithItsContext()
    {
        var cut = RenderSearched("Aurora", DocumentWith("Project Aurora si klade za cil vybudovat moderni platformu"));

        var result = cut.Find("[data-testid='document-find-result']");
        var mark = result.QuerySelector("mark")!;
        mark.TextContent.Should().Be("Aurora");
        result.QuerySelector(".tm-document-find-panel__context-before")!.TextContent.Should().Be("Project ");
        result.QuerySelector(".tm-document-find-panel__context-after")!.TextContent.Should().Be(" si klade za cil vybudovat…", "the broken last word of a cut context is dropped");
    }

    [Fact]
    public void Result_DropsTheBrokenWordOfACutContext_AndShowsAnEllipsis()
    {
        var text = "alpha beta gamma delta epsilon zeta eta theta iota kappa NEEDLE lambda mu nu xi omicron pi rho sigma tau upsilon";
        var cut = RenderSearched("NEEDLE", DocumentWith(text));

        var before = cut.Find(".tm-document-find-panel__context-before").TextContent;
        var after = cut.Find(".tm-document-find-panel__context-after").TextContent;
        before.Should().StartWith("…").And.NotStartWith("… ").And.EndWith(" ");
        after.Should().EndWith("…");
        before.TrimStart('…').Split(' ').First().Should().BeOneOf("eta", "theta", "iota", "kappa", "zeta", "epsilon", "delta", "gamma", "beta");
        text.Should().Contain(before.TrimStart('…').TrimEnd(), "what is shown is a real stretch of the text");
        cut.Find("[data-testid='document-find-result'] mark").TextContent.Should().Be("NEEDLE");
    }

    [Fact]
    public void ThousandHits_RenderAWindowOfOneHundred_TheHeadingKeepsTheFullCount_AndShowMoreGrowsIt()
    {
        var cut = RenderSearched("x", DocumentWith(Enumerable.Repeat("x", 1000).ToArray()));

        cut.Find("[data-testid='document-find-results-heading']").TextContent.Should().Be("Results (1000)", "the heading counts every hit");
        cut.FindAll("[data-testid='document-find-result']").Should().HaveCount(100, "a thousand cards would stall the inspector");
        var more = cut.Find("[data-testid='document-find-show-more']");
        more.TextContent.Trim().Should().Be("Show 100 more");

        more.Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-find-result']").Should().HaveCount(200));
    }

    [Fact]
    public void TheActiveResult_IsAlwaysInTheWindow_EvenWhenItIsPastIt()
    {
        var cut = RenderSearched("x", DocumentWith(Enumerable.Repeat("x", 300).ToArray()));

        // Previous from the first hit wraps to the LAST one (300th), far outside the first window.
        cut.Find("[data-testid='document-find-prev']").Click();

        cut.WaitForAssertion(() =>
        {
            var active = cut.FindAll("[data-testid='document-find-result'][aria-current='true']");
            active.Should().ContainSingle("the hit the reader stands on is rendered, however far down it is");
            active[0].QuerySelector(".tm-document-find-panel__result-index")!.TextContent.Should().Be("300");
        });
        cut.FindAll("[data-testid='document-find-result']").Count.Should().BeLessThanOrEqualTo(101);
    }

    [Fact]
    public void ResultsHeading_CountsEveryResult_EvenASingleOne()
    {
        var one = RenderSearched("platformu", DocumentWith("moderni platformu"));
        one.Find("[data-testid='document-find-results-heading']").TextContent.Should().Be("Results (1)");
        one.FindAll("[data-testid='document-find-result']").Should().ContainSingle("a single result is listed too");

        var three = RenderSearched("a", DocumentWith("a b a", "a"));
        three.Find("[data-testid='document-find-results-heading']").TextContent.Should().Be("Results (3)");
    }

    [Fact]
    public void ReplaceToggle_ShowsTheReplaceField_WithoutCtrlH()
    {
        var cut = Render<TmDocumentFindPanel>(p => p.Add(x => x.Document, EmptyDocument()));
        cut.FindAll("[data-testid='document-replace-input']").Should().BeEmpty();

        var toggle = cut.Find("[data-testid='document-find-replace-toggle']");
        toggle.GetAttribute("aria-expanded").Should().Be("false");

        toggle.Click();

        cut.Find("[data-testid='document-replace-input']").Should().NotBeNull();
        cut.Find("[data-testid='document-find-replace-toggle']").GetAttribute("aria-expanded").Should().Be("true");

        cut.Find("[data-testid='document-find-replace-toggle']").Click();
        cut.FindAll("[data-testid='document-replace-input']").Should().BeEmpty();
    }

    [Fact]
    public void ReplaceToggle_StartsExpanded_WhenTheHostAsksForReplaceMode()
    {
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument())
            .Add(x => x.ShowReplace, true));

        cut.Find("[data-testid='document-find-replace-toggle']").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[data-testid='document-replace-input']").Should().NotBeNull();
    }

    [Fact]
    public void ReplaceActions_ReplaceIsPrimary_ReplaceAllIsSecondary()
    {
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument())
            .Add(x => x.ShowReplace, true));

        cut.Find("[data-testid='document-find-replace-one']").ClassList.Should().Contain("tm-btn-primary");
        cut.Find("[data-testid='document-find-replace-all']").ClassList.Should().Contain("tm-btn-secondary").And.NotContain("tm-btn-primary");
        cut.Find("[data-testid='document-find-replace-one']").TextContent.Trim().Should().Be("Replace");
        cut.Find("[data-testid='document-find-replace-all']").TextContent.Trim().Should().Be("Replace all");
    }

    [Fact]
    public void PreviousNext_AreIcons_NotTextArrows()
    {
        var cut = Render<TmDocumentFindPanel>(p => p.Add(x => x.Document, EmptyDocument()));

        foreach (var testId in new[] { "document-find-prev", "document-find-next" })
        {
            var button = cut.Find($"[data-testid='{testId}']");
            button.QuerySelector(".tm-icon").Should().NotBeNull();
            button.TextContent.Trim().Should().BeEmpty();
        }
    }

    [Fact]
    public void Layout_FieldsHaveLabels_TheCountSitsInsideTheInput_OptionsAreAColumn()
    {
        var cut = Render<TmDocumentFindPanel>(p => p.Add(x => x.Document, EmptyDocument()));

        var field = cut.Find("[data-testid='document-find-input']").ParentElement!;
        field.ClassList.Should().Contain("tm-document-find-panel__field");
        field.QuerySelector("[data-testid='document-find-count']").Should().NotBeNull("the count sits inside the field");

        var label = cut.Find("label[for='" + cut.Find("[data-testid='document-find-input']").Id + "']");
        label.TextContent.Should().Be("Find");

        var options = cut.Find("[data-testid='document-find-options']");
        options.GetAttribute("role").Should().Be("group");
        options.QuerySelectorAll("input[type='checkbox']").Should().HaveCount(3);
        cut.Find("[data-testid='document-find-scope']").ClassList.Should().Contain("tm-document-find-panel__scope");
        cut.Find("[data-testid='document-find-scope']").ParentElement!.TextContent.Should().Contain("Scope");
    }

    [Fact]
    public void ClearButton_AppearsWithText_AndEmptiesTheQuery()
    {
        var cut = Render<TmDocumentFindPanel>(p => p.Add(x => x.Document, DocumentWith("alpha beta alpha")));
        cut.FindAll("[data-testid='document-find-clear']").Should().BeEmpty();

        cut.Find("[data-testid='document-find-input']").Input("alpha");
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-find-result']").Should().HaveCount(2));

        cut.Find("[data-testid='document-find-clear']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-find-result']").Should().BeEmpty());
        cut.Find("[data-testid='document-find-input']").GetAttribute("value").Should().BeNullOrEmpty();
        cut.FindAll("[data-testid='document-find-clear']").Should().BeEmpty();
    }

    [Fact]
    public void Header_IsTheSharedInspectorHeader_AndCanBeSuppressedByAHostThatHasItsOwn()
    {
        var withHeader = Render<TmDocumentFindPanel>(p => p.Add(x => x.Document, EmptyDocument()));
        withHeader.Find("[data-testid='document-inspector-header'] .tm-document-inspector-header__title").TextContent.Should().Be("Find and replace");
        withHeader.Find("[data-testid='document-find-close']").ClassList.Should().Contain("tm-document-inspector-header__close");

        var without = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument())
            .Add(x => x.ShowHeader, false));
        without.FindAll("[data-testid='document-inspector-header']").Should().BeEmpty();
        without.FindAll("[data-testid='document-find-close']").Should().BeEmpty("the host's own header carries the X");
    }

    [Fact]
    public void Escape_StillClosesOnceWithoutAHeader()
    {
        var closes = 0;
        var cut = Render<TmDocumentFindPanel>(p => p
            .Add(x => x.Document, EmptyDocument())
            .Add(x => x.ShowHeader, false)
            .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => closes++)));

        cut.Find("[data-testid='document-find-input']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        cut.WaitForAssertion(() => closes.Should().Be(1));
    }

    [Fact]
    public void ShiftEnter_NavigatesBackwards_AndEnterForwards()
    {
        var navigations = new List<int>();
        var cut = RenderSearched("a", DocumentWith("a a a"), p => p
            .Add(x => x.OnNavigateRequested, EventCallback.Factory.Create<int>(this, d => navigations.Add(d))));

        cut.Find("[data-testid='document-find-input']").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find("[data-testid='document-find-input']").KeyDown(new KeyboardEventArgs { Key = "Enter", ShiftKey = true });

        cut.WaitForAssertion(() => navigations.Should().Equal(1, -1));
    }
}
