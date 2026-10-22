using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.DocumentLibrary;
using Tempo.Blazor.Tests.Fixtures;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Files;

/// <summary>
/// WF4 (wireframe redesign plan ce19aa8c, phase 8): the desktop / tablet redesign of
/// <see cref="TmDocumentOpenDialog"/> — an XLarge modal, always-rendered (disabled) actions, a collapsible
/// WAI-ARIA tree, breadcrumb in the right pane, sort indicator, radio-card mode choice, an opt-in "Recent" node
/// and a testable <c>LayoutMode</c>. Every <c>.tm-dod-*</c> selector the older suites use stays.
/// </summary>
public class TmDocumentOpenDialogRedesignTests : LocalizationTestBase
{
    private const string GuardModule = "./_content/Tempo.Blazor/Components/Files/TmDocumentOpenDialog.razor.js";

    private static InMemoryDocumentLibraryProvider Seeded(
        DocumentLibraryCapabilities caps = DocumentLibraryCapabilities.All)
    {
        var p = new InMemoryDocumentLibraryProvider(caps);
        p.AddFolder(TempoDocumentKind.Wireframe, "/Designs");
        p.AddFolder(TempoDocumentKind.Wireframe, "/Designs/Mobile");
        p.AddFolder(TempoDocumentKind.Wireframe, "/Archive");
        p.AddDocument(TempoDocumentKind.Wireframe, "Home page", "/Designs",
            modifiedAt: new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        p.AddDocument(TempoDocumentKind.Wireframe, "Checkout", "/Designs",
            modifiedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        p.AddDocument(TempoDocumentKind.Wireframe, "Login screen", "/Designs/Mobile",
            modifiedAt: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        return p;
    }

    private IRenderedComponent<TmDocumentOpenDialog> RenderOpen(
        ITempoDocumentLibraryProvider provider,
        Action<ComponentParameterCollectionBuilder<TmDocumentOpenDialog>>? extra = null)
        => Render<TmDocumentOpenDialog>(p =>
        {
            p.Add(c => c.Provider, provider);
            p.Add(c => c.Kind, TempoDocumentKind.Wireframe);
            p.Add(c => c.Open, true);
            p.Add(c => c.SearchDebounceMs, 0);
            extra?.Invoke(p);
        });

    private static AngleSharp.Dom.IElement Node(IRenderedComponent<TmDocumentOpenDialog> cut, string text)
        => cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains(text));

    private static void OpenDesigns(IRenderedComponent<TmDocumentOpenDialog> cut)
    {
        Node(cut, "Designs").Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);
    }

    private static KeyboardEventArgs Key(string key) => new() { Key = key };

    // ── Modal size ──────────────────────────────────────────────────────────

    [Fact]
    public void UsesXLargeModal()
    {
        var cut = RenderOpen(Seeded());

        cut.Markup.Should().Contain("tm-modal--xl");
        cut.Markup.Should().NotContain("tm-modal--lg");
    }

    // ── Actions: always rendered, disabled without a selection ─────────────

    [Fact]
    public void Actions_AlwaysRendered_DisabledWithoutSelection()
    {
        var cut = RenderOpen(Seeded());
        OpenDesigns(cut);

        cut.Find(".tm-dod-rename").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".tm-dod-delete").HasAttribute("disabled").Should().BeTrue();

        cut.FindAll(".tm-dod-row").First().Click();

        cut.Find(".tm-dod-rename").HasAttribute("disabled").Should().BeFalse();
        cut.Find(".tm-dod-delete").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Actions_StillGatedByCapability_EvenWhenAlwaysRendered()
    {
        var cut = RenderOpen(Seeded(DocumentLibraryCapabilities.Search | DocumentLibraryCapabilities.Rename));
        OpenDesigns(cut);

        cut.FindAll(".tm-dod-rename").Should().ContainSingle();
        cut.FindAll(".tm-dod-delete").Should().BeEmpty();
        cut.FindAll(".tm-dod-new-folder").Should().BeEmpty();
    }

    [Fact]
    public void Actions_ShowAnIconAndTheLabel()
    {
        var cut = RenderOpen(Seeded());

        foreach (var selector in new[] { ".tm-dod-new-folder", ".tm-dod-rename", ".tm-dod-delete" })
        {
            var button = cut.Find(selector);
            button.QuerySelector("svg").Should().NotBeNull($"{selector} carries an icon");
            button.TextContent.Trim().Should().NotBeEmpty($"{selector} carries its label");
        }
    }

    // ── Tree: collapse / expand (WAI-ARIA tree pattern) ───────────────────

    [Fact]
    public void Tree_CollapseExpand_TogglesAriaExpanded()
    {
        var cut = RenderOpen(Seeded());

        Node(cut, "Designs").GetAttribute("aria-expanded").Should().Be("true");
        Node(cut, "Archive").HasAttribute("aria-expanded").Should().BeFalse("a leaf has no aria-expanded");
        cut.FindAll(".tm-dod-tree-node").Any(n => n.TextContent.Contains("Mobile")).Should().BeTrue();

        Node(cut, "Designs").QuerySelector(".tm-dod-tree-toggle")!.Click();

        Node(cut, "Designs").GetAttribute("aria-expanded").Should().Be("false");
        cut.FindAll(".tm-dod-tree-node").Any(n => n.TextContent.Contains("Mobile")).Should().BeFalse(
            "a collapsed folder hides its children");
        Node(cut, "All documents").GetAttribute("aria-selected").Should().Be("true",
            "the toggle expands/collapses only — it never navigates");

        Node(cut, "Designs").QuerySelector(".tm-dod-tree-toggle")!.Click();

        Node(cut, "Designs").GetAttribute("aria-expanded").Should().Be("true");
        cut.FindAll(".tm-dod-tree-node").Any(n => n.TextContent.Contains("Mobile")).Should().BeTrue();
    }

    [Fact]
    public void Tree_Toggle_IsAMouseAffordance_HiddenFromAssistiveTech_WithALocalisedTitle()
    {
        var cut = RenderOpen(Seeded());

        var toggle = Node(cut, "Designs").QuerySelector(".tm-dod-tree-toggle")!;
        toggle.GetAttribute("aria-hidden").Should().Be("true", "the keyboard owns expand/collapse (Left/Right); the chevron is mouse-only");
        toggle.HasAttribute("tabindex").Should().BeFalse("a span is not focusable");
        toggle.GetAttribute("title").Should().Be("Collapse");
        toggle.Click();
        Node(cut, "Designs").QuerySelector(".tm-dod-tree-toggle")!.GetAttribute("title").Should().Be("Expand");
    }

    [Fact]
    public void TreeItem_IsNamedByItsLabel_NotByTheChevron()
    {
        var cut = RenderOpen(Seeded());

        var item = Node(cut, "Designs");
        var labelledBy = item.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();
        cut.Find("#" + labelledBy).TextContent.Trim().Should().Be("Designs", "the name must not include 'Collapse'");
    }

    [Fact]
    public void TreeItems_ExposeSetSizeAndPosition()
    {
        var cut = RenderOpen(Seeded());

        // Level-2 siblings: Archive (1 of 2) and Designs (2 of 2).
        Node(cut, "Archive").GetAttribute("aria-setsize").Should().Be("2");
        Node(cut, "Archive").GetAttribute("aria-posinset").Should().Be("1");
        Node(cut, "Designs").GetAttribute("aria-posinset").Should().Be("2");
        Node(cut, "Mobile").GetAttribute("aria-setsize").Should().Be("1");
    }

    [Fact]
    public void Tree_ToggleClick_ReturnsFocusToTheTreeItem()
    {
        var guard = JSInterop.SetupModule(GuardModule);
        guard.Mode = JSRuntimeMode.Loose;
        var cut = RenderOpen(Seeded());

        Node(cut, "Designs").QuerySelector(".tm-dod-tree-toggle")!.Click();

        cut.WaitForAssertion(() => guard.Invocations["focusRoving"].Should().Contain(i =>
            i.Arguments.Count == 3 && (string)i.Arguments[1]! == "tree" && (string)i.Arguments[2]! == "/Designs"));
    }

    [Fact]
    public void Tree_ToggleButton_KeyUpIsFencedToo()
    {
        var cut = RenderOpen(Seeded());
        var toggle = Node(cut, "Designs").QuerySelector(".tm-dod-tree-toggle")!;

        var act = () => toggle.KeyUp(Key(" "));

        act.Should().Throw<MissingEventHandlerException>("a Space keyup from the chevron must not activate the tree item on top of the click");
    }

    [Fact]
    public void ListRows_AreGridRows_WithGridCells_AndExposeSelection()
    {
        var cut = RenderOpen(Seeded());
        OpenDesigns(cut);

        var row = cut.FindAll(".tm-dod-row").First();
        row.QuerySelectorAll("[role=gridcell]").Should().HaveCount(3);
        row.GetAttribute("aria-selected").Should().Be("false");

        row.Click();

        cut.FindAll(".tm-dod-row").First().GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void HomeAndEnd_OnAnEmptyList_DoNothing()
    {
        var cut = RenderOpen(Seeded());
        Node(cut, "Archive").Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-empty").Count > 0);

        // No row exists to raise a key from; the dialog root handles tree keys only. Pressing Home on the tree must not throw.
        var act = () => Node(cut, "Archive").KeyDown(Key("Home"));

        act.Should().NotThrow();
    }

    [Fact]
    public void Recent_ThenSearch_LeavesTheRecentNode()
    {
        var cut = RenderOpen(Seeded(), p => p.Add(c => c.ShowRecent, true));
        cut.Find(".tm-dod-tree-node--recent").Click();
        cut.WaitForState(() => cut.Find(".tm-dod-tree-node--recent").GetAttribute("aria-selected") == "true");

        cut.Find(".tm-dod-search").Input("screen");

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-dod-tree-node--recent").GetAttribute("aria-selected").Should().Be("false");
            cut.Find(".tm-dod-col-name").HasAttribute("disabled").Should().BeFalse("the search replaced the fixed Recent order");
        });
        cut.FindAll(".tm-dod-row").Select(r => r.GetAttribute("data-name")).Should().Contain("Login screen");
    }

    [Fact]
    public void ShowRecent_TurnedOffWhileActive_LeavesTheNode()
    {
        var cut = RenderOpen(Seeded(), p => p.Add(c => c.ShowRecent, true));
        cut.Find(".tm-dod-tree-node--recent").Click();
        cut.WaitForState(() => cut.Find(".tm-dod-tree-node--recent").GetAttribute("aria-selected") == "true");

        cut.Render(p => p.Add(c => c.ShowRecent, false));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-dod-tree-node--recent").Should().BeEmpty();
            cut.Find(".tm-dod-tree-node--active").TextContent.Should().Contain("All documents");
        });
    }

    [Fact]
    public void NewFolder_IsDisabled_OnTheRecentNode()
    {
        var cut = RenderOpen(Seeded(), p => p.Add(c => c.ShowRecent, true));
        cut.Find(".tm-dod-new-folder").HasAttribute("disabled").Should().BeFalse();

        cut.Find(".tm-dod-tree-node--recent").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-dod-new-folder").HasAttribute("disabled").Should().BeTrue(
            "a folder cannot be created 'under Recent'"));
    }

    [Fact]
    public void RenamingTheSelectedDocument_OpensWithTheNewName()
    {
        DocumentOpenResult? result = null;
        var cut = RenderOpen(Seeded(), p => p.Add(c => c.OnSelected, (DocumentOpenResult r) => result = r));
        OpenDesigns(cut);
        cut.FindAll(".tm-dod-row").First(r => r.GetAttribute("data-name") == "Checkout").Click();

        cut.Find(".tm-dod-rename").Click();
        cut.Find(".tm-dod-rename-input").Change("Cart");
        cut.Find(".tm-dod-rename-confirm").Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Any(r => r.GetAttribute("data-name") == "Cart"));
        cut.Find(".tm-dod-open").Click();

        result!.Name.Should().Be("Cart");
    }

    [Fact]
    public async Task AutoLayout_FollowsTheMeasuredWidth_FooterAndTree()
    {
        var observer = JSInterop.SetupModule("./_content/Tempo.Blazor/js/layout-observer.js");
        observer.Mode = JSRuntimeMode.Loose;
        var cut = RenderOpen(Seeded());
        cut.Find(".tm-dod").GetAttribute("data-layout").Should().Be("desktop", "the first frame is Desktop");

        var layout = cut.FindComponent<Tempo.Blazor.Components.Layout.TmLayoutObserver>();
        await cut.InvokeAsync(() => layout.Instance.OnLayoutModeChanged("tablet"));

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-dod").GetAttribute("data-layout").Should().Be("tablet");
            cut.Find(".tm-dod-tree").ClassList.Should().Contain("tm-dod-tree--narrow");
            cut.Find(".tm-dod-footer").ClassList.Should().Contain("tm-dod-footer--own-row");
        });
    }

    [Fact]
    public void Tree_IsATree_WithAnAccessibleName_AndOneRovingTabStop()
    {
        var cut = RenderOpen(Seeded());

        var tree = cut.Find(".tm-dod-tree");
        tree.GetAttribute("role").Should().Be("tree");
        tree.GetAttribute("aria-label").Should().Be("Folders");

        var stops = cut.FindAll(".tm-dod-tree-node").Where(n => n.GetAttribute("tabindex") == "0").ToList();
        stops.Should().ContainSingle("a tree is ONE tab stop with roving focus inside");
        stops[0].TextContent.Should().Contain("All documents", "the current folder is the entry point");
        cut.FindAll(".tm-dod-tree-node").Where(n => n != stops[0])
            .Should().OnlyContain(n => n.GetAttribute("tabindex") == "-1");
    }

    [Fact]
    public void Tree_ArrowKeys_FollowWaiAriaTreePattern()
    {
        var guard = JSInterop.SetupModule(GuardModule);
        guard.Mode = JSRuntimeMode.Loose;
        var cut = RenderOpen(Seeded());

        string Stop() => cut.FindAll(".tm-dod-tree-node").Single(n => n.GetAttribute("tabindex") == "0").TextContent;

        // Down / Up move through the VISIBLE items (All documents, Archive, Designs, Mobile — the provider orders by path).
        Node(cut, "All documents").KeyDown(Key("ArrowDown"));
        Stop().Should().Contain("Archive");
        Node(cut, "Archive").KeyDown(Key("ArrowDown"));
        Stop().Should().Contain("Designs");
        Node(cut, "Designs").KeyDown(Key("ArrowDown"));
        Stop().Should().Contain("Mobile");
        Node(cut, "Mobile").KeyDown(Key("ArrowDown"));
        Stop().Should().Contain("Mobile", "Down on the last item stays put");
        Node(cut, "Mobile").KeyDown(Key("ArrowUp"));
        Stop().Should().Contain("Designs");
        Node(cut, "Designs").KeyDown(Key("ArrowDown"));
        // Left on a leaf moves to its parent; Left on an open parent collapses it; Right on a closed parent opens it;
        // Right on an open parent moves to its first child.
        Node(cut, "Mobile").KeyDown(Key("ArrowLeft"));
        Stop().Should().Contain("Designs");
        Node(cut, "Designs").KeyDown(Key("ArrowLeft"));
        Node(cut, "Designs").GetAttribute("aria-expanded").Should().Be("false");
        Node(cut, "Designs").KeyDown(Key("ArrowRight"));
        Node(cut, "Designs").GetAttribute("aria-expanded").Should().Be("true");
        Node(cut, "Designs").KeyDown(Key("ArrowRight"));
        Stop().Should().Contain("Mobile");
        Node(cut, "Mobile").KeyDown(Key("ArrowRight"));
        Stop().Should().Contain("Mobile", "Right on a leaf does nothing");

        // Home / End.
        Node(cut, "Mobile").KeyDown(Key("Home"));
        Stop().Should().Contain("All documents");
        Node(cut, "All documents").KeyDown(Key("End"));
        Stop().Should().Contain("Mobile");

        // Moving focus actually moves DOM focus (a roving tabindex alone does not).
        guard.Invocations["focusRoving"].Should().NotBeEmpty();
    }

    [Fact]
    public void Tree_Enter_NavigatesToTheFolder()
    {
        var cut = RenderOpen(Seeded());

        Node(cut, "Designs").KeyDown(Key("Enter"));

        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);
        Node(cut, "Designs").GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void Tree_Space_ActivatesOnKeyUp_ExactlyOnce()
    {
        var provider = new RecordingLibraryProvider(Seeded());
        var cut = RenderOpen(provider);
        var before = provider.Calls.Count;

        Node(cut, "Designs").KeyDown(Key(" "));
        provider.Calls.Count.Should().Be(before, "Space activates on keyup, where a native button clicks");

        Node(cut, "Designs").KeyUp(Key(" "));
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);
        provider.Calls.Count.Should().Be(before + 1);
    }

    [Fact]
    public void Tree_ToggleButton_KeysAreFencedFromTheTreeItem()
    {
        // The chevron is a native button inside an emulating container: its keys must not bubble to the item.
        var cut = RenderOpen(Seeded());
        var toggle = Node(cut, "Designs").QuerySelector(".tm-dod-tree-toggle")!;

        var act = () => toggle.KeyDown(Key("Enter"));

        act.Should().Throw<MissingEventHandlerException>(
            "@onkeydown:stopPropagation on the toggle leaves no keydown handler for the event to reach");
    }

    // ── Breadcrumb in the right pane ────────────────────────────────────────

    [Fact]
    public void Breadcrumb_InRightPane_WithHomeIcon()
    {
        var cut = RenderOpen(Seeded());
        Node(cut, "Mobile").Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Any(r => r.TextContent.Contains("Login screen")));

        var crumb = cut.Find(".tm-dod-main .tm-dod-breadcrumb");
        cut.Find(".tm-dod-tree").QuerySelector(".tm-dod-breadcrumb").Should().BeNull("the breadcrumb moved out of the toolbar area into the right pane");

        var home = crumb.QuerySelector(".tm-dod-crumb")!;
        home.QuerySelector("svg").Should().NotBeNull("the root crumb is a home icon");
        home.GetAttribute("aria-label").Should().Be("Home");

        crumb.QuerySelectorAll(".tm-dod-crumb-sep").Select(s => s.TextContent.Trim())
            .Should().OnlyContain(t => t == "›");
        crumb.QuerySelectorAll(".tm-dod-crumb").Select(c => c.TextContent.Trim())
            .Should().ContainInOrder("", "Designs", "Mobile");
        crumb.QuerySelector("[aria-current='page']")!.TextContent.Should().Contain("Mobile");
    }

    // ── Sort indicator ──────────────────────────────────────────────────────

    [Fact]
    public void SortHeader_ShowsDirectionIndicator()
    {
        var cut = RenderOpen(Seeded());
        OpenDesigns(cut);

        cut.Find(".tm-dod-col-name .tm-dod-sort-indicator").GetAttribute("data-direction").Should().Be("ascending");
        cut.Find(".tm-dod-col-name .tm-dod-sort-indicator").GetAttribute("title").Should().Be("Sorted ascending");
        cut.FindAll(".tm-dod-col-modified .tm-dod-sort-indicator").Should().BeEmpty("only the sorted column shows an indicator");

        cut.Find(".tm-dod-col-name").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-dod-col-name .tm-dod-sort-indicator").GetAttribute("data-direction").Should().Be("descending");
            cut.Find(".tm-dod-col-name .tm-dod-sort-indicator").GetAttribute("title").Should().Be("Sorted descending");
        });

        cut.Find(".tm-dod-col-modified").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-dod-col-modified .tm-dod-sort-indicator").GetAttribute("data-direction").Should().Be("ascending");
            cut.FindAll(".tm-dod-col-name .tm-dod-sort-indicator").Should().BeEmpty();
        });
    }

    // ── Mode: radio cards ───────────────────────────────────────────────────

    [Fact]
    public void ModeOptions_RenderAsRadioCards_InRadioGroup()
    {
        var cut = RenderOpen(Seeded());

        var group = cut.Find(".tm-dod-mode");
        group.GetAttribute("role").Should().Be("radiogroup");
        group.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();

        var cards = group.QuerySelectorAll(".tm-dod-mode-card").ToList();
        cards.Should().HaveCount(2);
        foreach (var card in cards)
        {
            card.QuerySelector("input[type=radio]").Should().NotBeNull();
            card.QuerySelector(".tm-dod-mode-title")!.TextContent.Trim().Should().NotBeEmpty();
            card.QuerySelector(".tm-dod-mode-hint")!.TextContent.Trim().Should().NotBeEmpty(
                "each card carries its own hint under the title");
        }

        cards[0].TextContent.Should().Contain("Link to file").And.Contain("Stays in sync");
        cards[1].TextContent.Should().Contain("Insert a copy").And.Contain("independent copy");
        cards[0].ClassList.Should().Contain("tm-dod-mode-card--selected", "Link is the default mode");

        cut.Find(".tm-dod-mode-copy").Change(true);

        var after = cut.FindAll(".tm-dod-mode-card").ToList();
        after[1].ClassList.Should().Contain("tm-dod-mode-card--selected");
        after[0].ClassList.Should().NotContain("tm-dod-mode-card--selected");
    }

    [Fact]
    public void ModeOptions_KeepTheirNativeRadioInputs()
    {
        var cut = RenderOpen(Seeded());

        cut.Find("input.tm-dod-mode-link").GetAttribute("type").Should().Be("radio");
        cut.Find("input.tm-dod-mode-copy").GetAttribute("type").Should().Be("radio");
        cut.Find("input.tm-dod-mode-link").GetAttribute("name").Should().Be(cut.Find("input.tm-dod-mode-copy").GetAttribute("name"));
    }

    // ── Footer ──────────────────────────────────────────────────────────────

    [Fact]
    public void Footer_GroupsTheActions_InTheirOwnContainer()
    {
        var cut = RenderOpen(Seeded());

        var actions = cut.Find(".tm-dod-footer .tm-dod-footer-actions");
        actions.QuerySelector(".tm-dod-cancel").Should().NotBeNull();
        actions.QuerySelector(".tm-dod-open").Should().NotBeNull();
    }

    // ── LayoutMode ──────────────────────────────────────────────────────────

    [Fact]
    public void Desktop_RendersTheFullWidthTree_AndFooterOnOneRow()
    {
        var cut = RenderOpen(Seeded());

        cut.Find(".tm-dod").GetAttribute("data-layout").Should().Be("desktop");
        cut.FindAll(".tm-dod-tree--narrow").Should().BeEmpty();
        cut.FindAll(".tm-dod-footer--own-row").Should().BeEmpty();
    }

    [Fact]
    public void Tablet_LayoutMode_RendersNarrowTreeAndFooterOnOwnRow()
    {
        var cut = RenderOpen(Seeded(), p => p.Add(c => c.LayoutMode, TmLayoutMode.Tablet));

        cut.Find(".tm-dod").GetAttribute("data-layout").Should().Be("tablet");
        cut.Find(".tm-dod-tree").ClassList.Should().Contain("tm-dod-tree--narrow");
        var footer = cut.Find(".tm-dod-footer");
        footer.ClassList.Should().Contain("tm-dod-footer--own-row");
        footer.GetAttribute("data-layout").Should().Be("tablet");
        footer.QuerySelector(".tm-dod-footer-actions .tm-dod-open").Should().NotBeNull();
    }

    [Fact]
    public void Mobile_LayoutMode_UsesTheCompactRules_TooUntilPhase9()
    {
        var cut = RenderOpen(Seeded(), p => p.Add(c => c.LayoutMode, TmLayoutMode.Mobile));

        cut.Find(".tm-dod-tree").ClassList.Should().Contain("tm-dod-tree--narrow");
        cut.Find(".tm-dod-footer").ClassList.Should().Contain("tm-dod-footer--own-row");
    }

    // ── Recent (opt-in, provider unchanged) ─────────────────────────────────

    [Fact]
    public void Recent_Hidden_ByDefault()
    {
        var cut = RenderOpen(Seeded());

        cut.FindAll(".tm-dod-tree-node--recent").Should().BeEmpty();
        cut.Find(".tm-dod-tree").TextContent.Should().NotContain("Recent");
    }

    [Fact]
    public void Recent_Enabled_BrowsesByModifiedDescending()
    {
        var inner = Seeded();
        inner.AddDocument(TempoDocumentKind.Wireframe, "Older root doc", "/",
            modifiedAt: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        inner.AddDocument(TempoDocumentKind.Wireframe, "Newer root doc", "/",
            modifiedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        var provider = new RecordingLibraryProvider(inner);
        var cut = RenderOpen(provider, p => p.Add(c => c.ShowRecent, true).Add(c => c.PageSize, 25));

        var recent = cut.Find(".tm-dod-tree-node--recent");
        recent.TextContent.Should().Contain("Recent");
        recent.GetAttribute("aria-level").Should().Be("1");

        recent.Click();

        cut.WaitForState(() => provider.Calls.Any(c => c.FolderPath is null && c.Search is null));
        var call = provider.Calls.Last(c => c.FolderPath is null && c.Search is null);
        call.SortField.Should().Be(DocumentLibrarySortField.Modified);
        call.Descending.Should().BeTrue();
        call.Skip.Should().Be(0);
        call.Take.Should().Be(25, "Take is PageSize");

        cut.WaitForAssertion(() => cut.FindAll(".tm-dod-row").Select(r => r.GetAttribute("data-name"))
            .Should().ContainInOrder("Newer root doc", "Older root doc"));
        cut.Find(".tm-dod-tree-node--recent").GetAttribute("aria-selected").Should().Be("true");
        cut.Find(".tm-dod-tree-node--active").TextContent.Should().Contain("Recent");
    }

    [Fact]
    public void Recent_Breadcrumb_ShowsTheRecentNode()
    {
        var cut = RenderOpen(Seeded(), p => p.Add(c => c.ShowRecent, true));
        cut.Find(".tm-dod-tree-node--recent").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-dod-breadcrumb [aria-current='page']").TextContent.Should().Contain("Recent"));
    }

    [Fact]
    public void Recent_SortIsFixed_SoTheHeaderButtonsAreDisabled_AndReflectTheRecentOrder()
    {
        // Seeded has no root documents: the empty state replaces the header, so seed one.
        var inner = Seeded();
        inner.AddDocument(TempoDocumentKind.Wireframe, "Root doc", "/");
        var cut = RenderOpen(inner, p => p.Add(c => c.ShowRecent, true));
        cut.Find(".tm-dod-tree-node--recent").Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 1);

        cut.Find(".tm-dod-col-name").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".tm-dod-col-modified").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".tm-dod-col-modified").ParentElement!.GetAttribute("aria-sort").Should().Be("descending");
    }

    [Fact]
    public void Recent_ThenAFolder_RestoresTheNormalFolderQuery()
    {
        var provider = new RecordingLibraryProvider(Seeded());
        var cut = RenderOpen(provider, p => p.Add(c => c.ShowRecent, true));
        cut.Find(".tm-dod-tree-node--recent").Click();
        cut.WaitForState(() => provider.Calls.Any(c => c.FolderPath is null));

        Node(cut, "Designs").Click();

        cut.WaitForAssertion(() => provider.Calls.Last().FolderPath.Should().Be("/Designs"));
        provider.Calls.Last().SortField.Should().Be(DocumentLibrarySortField.Name);
        provider.Calls.Last().Descending.Should().BeFalse();
    }

    // ── Keyboard: roving focus in the list and the grid ─────────────────────

    [Fact]
    public void Rows_AreOneRovingTabStop_AndArrowsMoveFocus()
    {
        var guard = JSInterop.SetupModule(GuardModule);
        guard.Mode = JSRuntimeMode.Loose;
        var cut = RenderOpen(Seeded());
        OpenDesigns(cut);

        string Stop() => cut.FindAll(".tm-dod-row").Single(r => r.GetAttribute("tabindex") == "0").GetAttribute("data-name")!;

        Stop().Should().Be("Checkout", "the first row is the entry point when nothing is selected");
        cut.FindAll(".tm-dod-row").Count(r => r.GetAttribute("tabindex") == "-1").Should().Be(1);

        cut.FindAll(".tm-dod-row").First().KeyDown(Key("ArrowDown"));
        Stop().Should().Be("Home page");
        cut.FindAll(".tm-dod-row").Last().KeyDown(Key("ArrowDown"));
        Stop().Should().Be("Home page", "Down on the last row stays put");
        cut.FindAll(".tm-dod-row").Last().KeyDown(Key("ArrowUp"));
        Stop().Should().Be("Checkout");
        cut.FindAll(".tm-dod-row").First().KeyDown(Key("End"));
        Stop().Should().Be("Home page");
        cut.FindAll(".tm-dod-row").First().KeyDown(Key("Home"));
        Stop().Should().Be("Checkout");

        guard.Invocations["focusRoving"].Should().NotBeEmpty();
        cut.FindAll(".tm-dod-row--selected").Should().BeEmpty("arrows move focus, they do not select");
    }

    [Fact]
    public void Rows_TheSelectedRowIsTheTabStop()
    {
        var cut = RenderOpen(Seeded());
        OpenDesigns(cut);

        cut.FindAll(".tm-dod-row").Last().Click();

        cut.FindAll(".tm-dod-row").Single(r => r.GetAttribute("tabindex") == "0")
            .GetAttribute("data-name").Should().Be("Home page");
    }

    [Fact]
    public void GridCards_AreOneRovingTabStop_AndArrowsMoveFocus()
    {
        var cut = RenderOpen(Seeded());
        OpenDesigns(cut);
        cut.Find(".tm-dod-view-grid").Click();

        string Stop() => cut.FindAll(".tm-dod-card").Single(c => c.GetAttribute("tabindex") == "0").GetAttribute("data-name")!;

        Stop().Should().Be("Checkout");
        cut.FindAll(".tm-dod-card").First().KeyDown(Key("ArrowRight"));
        Stop().Should().Be("Home page");
        cut.FindAll(".tm-dod-card").Last().KeyDown(Key("ArrowLeft"));
        Stop().Should().Be("Checkout");
        cut.FindAll(".tm-dod-card").First().KeyDown(Key("End"));
        Stop().Should().Be("Home page");
    }

    [Fact]
    public void GridCards_UpDown_StepByTheMeasuredColumnCount()
    {
        var inner = Seeded();
        for (var i = 0; i < 6; i++)
        {
            inner.AddDocument(TempoDocumentKind.Wireframe, $"Extra {i}", "/Designs",
                modifiedAt: new DateTime(2026, 1, 10 + i, 0, 0, 0, DateTimeKind.Utc));
        }

        var guard = JSInterop.SetupModule(GuardModule);
        guard.Mode = JSRuntimeMode.Loose;
        guard.Setup<int>("getGridColumns", _ => true).SetResult(3);
        var cut = RenderOpen(inner);
        Node(cut, "Designs").Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 8);
        cut.Find(".tm-dod-view-grid").Click();

        string Stop() => cut.FindAll(".tm-dod-card").Single(c => c.GetAttribute("tabindex") == "0").GetAttribute("data-name")!;
        var names = cut.FindAll(".tm-dod-card").Select(c => c.GetAttribute("data-name")!).ToList();

        cut.FindAll(".tm-dod-card").First().KeyDown(Key("ArrowDown"));
        cut.WaitForAssertion(() => Stop().Should().Be(names[3], "Down moves one grid row = 3 columns"));
        cut.FindAll(".tm-dod-card")[3].KeyDown(Key("ArrowUp"));
        cut.WaitForAssertion(() => Stop().Should().Be(names[0]));
    }

    [Fact]
    public void GridCards_FallBackToOneStep_WhenTheColumnCountIsUnknown()
    {
        var cut = RenderOpen(Seeded());
        OpenDesigns(cut);
        cut.Find(".tm-dod-view-grid").Click();

        cut.FindAll(".tm-dod-card").First().KeyDown(Key("ArrowDown"));

        cut.WaitForAssertion(() => cut.FindAll(".tm-dod-card").Single(c => c.GetAttribute("tabindex") == "0")
            .GetAttribute("data-name").Should().Be("Home page"));
    }

    [Fact]
    public void Listbox_HasItsOwnLabel_NotTheDialogTitle()
    {
        var cut = RenderOpen(Seeded());
        OpenDesigns(cut);
        cut.Find(".tm-dod-view-grid").Click();

        var label = cut.Find(".tm-dod-grid").GetAttribute("aria-label");
        label.Should().Be("Documents");
        label.Should().NotBe("Open document");
    }

    [Fact]
    public void Listbox_AndListGrid_BothHaveTheDocumentsLabel()
    {
        var cut = RenderOpen(Seeded());
        OpenDesigns(cut);

        cut.Find(".tm-dod-list").GetAttribute("aria-label").Should().Be("Documents");
        cut.FindAll(".tm-dod-sort-indicator").Should().OnlyContain(i => i.GetAttribute("aria-hidden") == "true");
    }

    [Fact]
    public void Tree_Toggle_IsANonFocusableSpan()
    {
        var cut = RenderOpen(Seeded());

        var toggle = Node(cut, "Designs").QuerySelector(".tm-dod-tree-toggle")!;
        toggle.TagName.Should().Be("SPAN", "an aria-hidden control must not be able to take DOM focus");
        toggle.HasAttribute("tabindex").Should().BeFalse();
    }

    [Fact]
    public void GridCards_UpDown_AtTheEdges_DoNotClamp()
    {
        var inner = Seeded();
        for (var i = 0; i < 4; i++)
        {
            inner.AddDocument(TempoDocumentKind.Wireframe, $"Extra {i}", "/Designs",
                modifiedAt: new DateTime(2026, 1, 10 + i, 0, 0, 0, DateTimeKind.Utc));
        }

        var guard = JSInterop.SetupModule(GuardModule);
        guard.Mode = JSRuntimeMode.Loose;
        guard.Setup<int>("getGridColumns", _ => true).SetResult(3);
        var cut = RenderOpen(inner);
        Node(cut, "Designs").Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 6);
        cut.Find(".tm-dod-view-grid").Click();

        string Stop() => cut.FindAll(".tm-dod-card").Single(c => c.GetAttribute("tabindex") == "0").GetAttribute("data-name")!;
        var names = cut.FindAll(".tm-dod-card").Select(c => c.GetAttribute("data-name")!).ToList();

        // Up from the first row: nothing (no clamp to the first card).
        cut.FindAll(".tm-dod-card")[1].KeyDown(Key("ArrowUp"));
        cut.WaitForAssertion(() => Stop().Should().Be(names[0], "the stop is unchanged"));

        // Down from a card of the last (partial) row, where +3 is beyond the end: nothing.
        cut.FindAll(".tm-dod-card")[0].KeyDown(Key("End"));
        cut.WaitForAssertion(() => Stop().Should().Be(names[5]));
        cut.FindAll(".tm-dod-card")[4].KeyDown(Key("ArrowDown"));
        cut.WaitForAssertion(() => Stop().Should().Be(names[5], "Down past the last row does not clamp to the last card"));
    }

    [Fact]
    public void Tree_CollapsingTheFocusedNodesAncestor_MovesTheFocusStopToTheAncestor()
    {
        var cut = RenderOpen(Seeded());

        Node(cut, "Mobile").KeyDown(Key("ArrowUp")); // focus moves to Designs
        Node(cut, "Designs").KeyDown(Key("ArrowDown")); // focus on Mobile
        cut.FindAll(".tm-dod-tree-node").Single(n => n.GetAttribute("tabindex") == "0").TextContent.Should().Contain("Mobile");

        Node(cut, "Designs").QuerySelector(".tm-dod-tree-toggle")!.Click();

        var stops = cut.FindAll(".tm-dod-tree-node").Where(n => n.GetAttribute("tabindex") == "0").ToList();
        stops.Should().ContainSingle().Which.TextContent.Should().Contain("Designs");
    }

    [Fact]
    public void Tree_CollapsedAncestorOfTheCurrentFolder_StaysAriaSelected()
    {
        var cut = RenderOpen(Seeded());
        Node(cut, "Mobile").Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Any(r => r.TextContent.Contains("Login screen")));
        Node(cut, "Mobile").GetAttribute("aria-selected").Should().Be("true");

        Node(cut, "Designs").QuerySelector(".tm-dod-tree-toggle")!.Click();

        cut.FindAll(".tm-dod-tree-node").Where(n => n.GetAttribute("aria-selected") == "true")
            .Should().ContainSingle().Which.TextContent.Should().Contain("Designs",
                "the current folder is hidden inside the collapsed node, which stands in for it");
    }

    // ── Reload race ─────────────────────────────────────────────────────────

    private sealed class GatedProvider(ITempoDocumentLibraryProvider inner) : ITempoDocumentLibraryProvider
    {
        public Dictionary<string, TaskCompletionSource<DocumentLibraryPage>> Gates { get; } = new();

        public DocumentLibraryCapabilities Capabilities => inner.Capabilities;

        public Task<DocumentLibraryFolder> GetFolderTreeAsync(TempoDocumentKind kind, CancellationToken cancellationToken = default)
            => inner.GetFolderTreeAsync(kind, cancellationToken);

        public Task<DocumentLibraryPage> BrowseAsync(DocumentLibraryQuery query, CancellationToken cancellationToken = default)
        {
            if (query.FolderPath is { } path && Gates.TryGetValue(path, out var gate))
            {
                return gate.Task;
            }

            return inner.BrowseAsync(query, cancellationToken);
        }

        public Task<DocumentLibraryEntry?> GetEntryAsync(TempoDocumentKind kind, Guid documentId, CancellationToken cancellationToken = default)
            => inner.GetEntryAsync(kind, documentId, cancellationToken);

        public Task<DocumentLibraryFolder> CreateFolderAsync(TempoDocumentKind kind, string parentPath, string name, CancellationToken cancellationToken = default)
            => inner.CreateFolderAsync(kind, parentPath, name, cancellationToken);

        public Task RenameDocumentAsync(TempoDocumentKind kind, Guid documentId, string newName, CancellationToken cancellationToken = default)
            => inner.RenameDocumentAsync(kind, documentId, newName, cancellationToken);

        public Task RenameFolderAsync(TempoDocumentKind kind, string folderPath, string newName, CancellationToken cancellationToken = default)
            => inner.RenameFolderAsync(kind, folderPath, newName, cancellationToken);

        public Task DeleteDocumentsAsync(TempoDocumentKind kind, IReadOnlyList<Guid> documentIds, CancellationToken cancellationToken = default)
            => inner.DeleteDocumentsAsync(kind, documentIds, cancellationToken);

        public Task DeleteFolderAsync(TempoDocumentKind kind, string folderPath, CancellationToken cancellationToken = default)
            => inner.DeleteFolderAsync(kind, folderPath, cancellationToken);
    }

    private static DocumentLibraryPage PageOf(string name, int total = 1) => new()
    {
        Items = [new DocumentLibraryEntry { Id = Guid.NewGuid(), Name = name, Kind = TempoDocumentKind.Wireframe }],
        TotalCount = total
    };

    [Fact]
    public void Reload_StaleFolderResponseCompletingLast_DoesNotOverwriteTheNewerFolder()
    {
        var provider = new GatedProvider(Seeded());
        var cut = RenderOpen(provider);
        var gateA = provider.Gates["/Archive"] = new TaskCompletionSource<DocumentLibraryPage>();
        var gateB = provider.Gates["/Designs"] = new TaskCompletionSource<DocumentLibraryPage>();

        Node(cut, "Archive").Click();
        Node(cut, "Designs").Click();

        gateB.SetResult(PageOf("From B"));
        cut.WaitForAssertion(() => cut.FindAll(".tm-dod-row").Select(r => r.GetAttribute("data-name")).Should().Equal("From B"));

        gateA.SetResult(PageOf("From A", total: 7));

        cut.WaitForAssertion(() => cut.FindAll(".tm-dod-row").Select(r => r.GetAttribute("data-name")).Should().Equal("From B"));
        cut.FindAll(".tm-dod-load-more").Should().BeEmpty("the stale total (7) must not apply");
    }

    [Fact]
    public void Reload_StaleFailure_DoesNotFlagAnErrorOnTheNewerFolder()
    {
        var provider = new GatedProvider(Seeded());
        var cut = RenderOpen(provider);
        var gateA = provider.Gates["/Archive"] = new TaskCompletionSource<DocumentLibraryPage>();
        var gateB = provider.Gates["/Designs"] = new TaskCompletionSource<DocumentLibraryPage>();

        Node(cut, "Archive").Click();
        Node(cut, "Designs").Click();
        gateB.SetResult(PageOf("From B"));
        cut.WaitForAssertion(() => cut.FindAll(".tm-dod-row").Should().ContainSingle());

        gateA.SetException(new InvalidOperationException("boom"));

        cut.WaitForAssertion(() => cut.FindAll(".tm-dod-error").Should().BeEmpty());
        cut.FindAll(".tm-dod-row").Select(r => r.GetAttribute("data-name")).Should().Equal("From B");
    }}
