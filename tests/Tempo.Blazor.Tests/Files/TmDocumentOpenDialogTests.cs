using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.DocumentLibrary;
using Tempo.Blazor.Tests.Fixtures;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Files;

/// <summary>
/// bUnit tests for <see cref="TmDocumentOpenDialog"/> — the Word-like open dialog over the
/// document library. Built up test-first across phase 1 of the document-library plan.
/// </summary>
public class TmDocumentOpenDialogTests : LocalizationTestBase
{
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

    // ── 1.2 Skeleton & open/closed ────────────────────────────────────────────

    [Fact]
    public void Closed_RendersNothing()
    {
        var cut = Render<TmDocumentOpenDialog>(p => p
            .Add(c => c.Provider, Seeded())
            .Add(c => c.Kind, TempoDocumentKind.Wireframe)
            .Add(c => c.Open, false));

        cut.FindAll(".tm-document-open-dialog").Should().BeEmpty();
    }

    [Fact]
    public void Open_RendersModalWithLocalisedTitle()
    {
        var cut = Render<TmDocumentOpenDialog>(p => p
            .Add(c => c.Provider, Seeded())
            .Add(c => c.Kind, TempoDocumentKind.Wireframe)
            .Add(c => c.Open, true));

        cut.FindAll(".tm-document-open-dialog").Should().NotBeEmpty();
        cut.Markup.Should().Contain("Open document");
    }

    private IRenderedComponent<TmDocumentOpenDialog> RenderOpen(
        InMemoryDocumentLibraryProvider provider,
        Action<ComponentParameterCollectionBuilder<TmDocumentOpenDialog>>? extra = null)
        => Render<TmDocumentOpenDialog>(p =>
        {
            p.Add(c => c.Provider, provider);
            p.Add(c => c.Kind, TempoDocumentKind.Wireframe);
            p.Add(c => c.Open, true);
            p.Add(c => c.SearchDebounceMs, 0);
            extra?.Invoke(p);
        });

    // ── 1.3 Folder tree + loading ─────────────────────────────────────────────

    [Fact]
    public void Open_LoadsFolderTree()
    {
        var cut = RenderOpen(Seeded());

        var tree = cut.Find(".tm-dod-tree");
        tree.TextContent.Should().Contain("All documents");
        tree.TextContent.Should().Contain("Designs");
        tree.TextContent.Should().Contain("Archive");
    }

    [Fact]
    public void Loading_ShowsSpinner_UntilProviderCompletes()
    {
        var gate = new TaskCompletionSource();
        var provider = new GatedLibraryProvider(Seeded(), gate.Task);

        var cut = Render<TmDocumentOpenDialog>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.Kind, TempoDocumentKind.Wireframe)
            .Add(c => c.Open, true));

        cut.FindAll(".tm-dod-loading").Should().NotBeEmpty();

        gate.SetResult();
        cut.WaitForState(() => cut.FindAll(".tm-dod-loading").Count == 0);
        cut.FindAll(".tm-dod-loading").Should().BeEmpty();
    }

    // ── 1.4 Folder navigation ─────────────────────────────────────────────────

    [Fact]
    public void ClickingFolder_BrowsesIntoIt()
    {
        var cut = RenderOpen(Seeded());

        // Root shows nothing (documents live in subfolders); navigate into Designs.
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs"))
            .Click();

        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);
        cut.Markup.Should().Contain("Home page").And.Contain("Checkout");
    }

    // ── 1.5 Breadcrumb ────────────────────────────────────────────────────────

    [Fact]
    public void Breadcrumb_ReflectsCurrentFolder_AndNavigatesUp()
    {
        var cut = RenderOpen(Seeded());

        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Mobile"))
            .Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Any(r => r.TextContent.Contains("Login screen")));

        var crumbs = cut.FindAll(".tm-dod-crumb");
        crumbs.Should().HaveCountGreaterThanOrEqualTo(2);

        // Click the "Designs" crumb to go up one level.
        crumbs.First(c => c.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);
        cut.Markup.Should().Contain("Home page");
    }

    // ── 1.6 List view + sorting ───────────────────────────────────────────────

    [Fact]
    public void ListView_SortsByName_ThenTogglesDescending()
    {
        var cut = RenderOpen(Seeded());
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);

        cut.FindAll(".tm-dod-row").Select(r => r.GetAttribute("data-name"))
            .Should().ContainInOrder("Checkout", "Home page");

        cut.Find(".tm-dod-col-name").Click();
        cut.WaitForState(() =>
            cut.FindAll(".tm-dod-row").First().GetAttribute("data-name") == "Home page");
        cut.FindAll(".tm-dod-row").Select(r => r.GetAttribute("data-name"))
            .Should().ContainInOrder("Home page", "Checkout");
    }

    // ── 1.7 Grid view ─────────────────────────────────────────────────────────

    [Fact]
    public void GridView_Toggle_RendersCards()
    {
        var cut = RenderOpen(Seeded());
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);

        cut.Find(".tm-dod-view-grid").Click();

        cut.FindAll(".tm-dod-card").Should().HaveCount(2);
        cut.FindAll(".tm-dod-row").Should().BeEmpty();
    }

    // ── 1.8 Search ────────────────────────────────────────────────────────────

    [Fact]
    public void Search_FiltersAcrossFolders()
    {
        var cut = RenderOpen(Seeded());

        cut.Find(".tm-dod-search").Input("screen");

        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 1);
        cut.Markup.Should().Contain("Login screen");
    }

    [Fact]
    public void Search_Hidden_WhenCapabilityAbsent()
    {
        var caps = DocumentLibraryCapabilities.CreateFolder | DocumentLibraryCapabilities.Rename
                   | DocumentLibraryCapabilities.Delete;
        var cut = RenderOpen(Seeded(caps));

        cut.FindAll(".tm-dod-search").Should().BeEmpty();
    }

    // ── 1.9 Paging ────────────────────────────────────────────────────────────

    [Fact]
    public void Paging_LoadMore_AppendsNextPage()
    {
        var provider = Seeded();
        var cut = Render<TmDocumentOpenDialog>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.Kind, TempoDocumentKind.Wireframe)
            .Add(c => c.Open, true)
            .Add(c => c.SearchDebounceMs, 0)
            .Add(c => c.PageSize, 1));

        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 1);

        cut.Find(".tm-dod-load-more").Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);
    }

    // ── 1.10 Selection + double-click ─────────────────────────────────────────

    [Fact]
    public void SelectingRow_EnablesOpen_AndConfirms()
    {
        DocumentOpenResult? result = null;
        var cut = RenderOpen(Seeded(), p => p.Add(c => c.OnSelected,
            (DocumentOpenResult r) => result = r));
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);

        cut.Find(".tm-dod-open").HasAttribute("disabled").Should().BeTrue();

        cut.FindAll(".tm-dod-row").First(r => r.GetAttribute("data-name") == "Home page").Click();
        cut.Find(".tm-dod-open").HasAttribute("disabled").Should().BeFalse();

        cut.Find(".tm-dod-open").Click();
        result.Should().NotBeNull();
        result!.Name.Should().Be("Home page");
        result.Kind.Should().Be(TempoDocumentKind.Wireframe);
    }

    [Fact]
    public void DoubleClickingRow_ConfirmsImmediately()
    {
        DocumentOpenResult? result = null;
        var cut = RenderOpen(Seeded(), p => p.Add(c => c.OnSelected,
            (DocumentOpenResult r) => result = r));
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);

        cut.FindAll(".tm-dod-row").First(r => r.GetAttribute("data-name") == "Checkout")
            .DoubleClick();

        result.Should().NotBeNull();
        result!.Name.Should().Be("Checkout");
    }

    // ── 1.11 Link/Copy toggle ─────────────────────────────────────────────────

    [Fact]
    public void ModeToggle_DefaultsToLink_AndReportsChosenMode()
    {
        DocumentOpenResult? result = null;
        var cut = RenderOpen(Seeded(), p => p.Add(c => c.OnSelected,
            (DocumentOpenResult r) => result = r));
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);

        cut.Find(".tm-dod-mode-copy").Change(true);
        cut.FindAll(".tm-dod-row").First().Click();
        cut.Find(".tm-dod-open").Click();

        result!.Mode.Should().Be(DocumentOpenMode.Copy);
    }

    [Fact]
    public void ModeToggle_Hidden_WhenShowModeToggleFalse()
    {
        var cut = RenderOpen(Seeded(), p => p.Add(c => c.ShowModeToggle, false));

        cut.FindAll(".tm-dod-mode").Should().BeEmpty();
    }

    // ── 1.12 Cancel ───────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_RaisesOnCancelled_AndClosesDialog()
    {
        var cancelled = false;
        var openChanged = true;
        var cut = RenderOpen(Seeded(), p =>
        {
            p.Add(c => c.OnCancelled, () => cancelled = true);
            p.Add(c => c.OpenChanged, (bool v) => openChanged = v);
        });

        cut.Find(".tm-dod-cancel").Click();

        cancelled.Should().BeTrue();
        openChanged.Should().BeFalse();
    }

    // ── 1.13 New folder ───────────────────────────────────────────────────────

    [Fact]
    public void NewFolder_CreatesFolderViaProvider()
    {
        var provider = Seeded();
        var cut = RenderOpen(provider);
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();

        cut.Find(".tm-dod-new-folder").Click();
        cut.Find(".tm-dod-new-folder-input").Change("Desktop");
        cut.Find(".tm-dod-new-folder-confirm").Click();

        cut.WaitForState(() => cut.Find(".tm-dod-tree").TextContent.Contains("Desktop"));
    }

    [Fact]
    public void NewFolder_Hidden_WhenCapabilityAbsent()
    {
        var cut = RenderOpen(Seeded(DocumentLibraryCapabilities.Search));

        cut.FindAll(".tm-dod-new-folder").Should().BeEmpty();
    }

    // ── 1.14 Rename ───────────────────────────────────────────────────────────

    [Fact]
    public void Rename_Document_UpdatesName()
    {
        var provider = Seeded();
        var cut = RenderOpen(provider);
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);

        cut.FindAll(".tm-dod-row").First(r => r.GetAttribute("data-name") == "Checkout").Click();
        cut.Find(".tm-dod-rename").Click();
        cut.Find(".tm-dod-rename-input").Change("Cart");
        cut.Find(".tm-dod-rename-confirm").Click();

        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Any(r => r.GetAttribute("data-name") == "Cart"));
        cut.Markup.Should().NotContain("Checkout");
    }

    [Fact]
    public void Rename_EmptyName_ShowsValidationMessage()
    {
        var cut = RenderOpen(Seeded());
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);

        cut.FindAll(".tm-dod-row").First().Click();
        cut.Find(".tm-dod-rename").Click();
        cut.Find(".tm-dod-rename-input").Change("   ");
        cut.Find(".tm-dod-rename-confirm").Click();

        cut.Markup.Should().Contain("Name is required.");
    }

    // ── 1.15 Delete ───────────────────────────────────────────────────────────

    [Fact]
    public void Delete_Document_RemovesItAfterConfirm()
    {
        var provider = Seeded();
        var cut = RenderOpen(provider);
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);

        cut.FindAll(".tm-dod-row").First(r => r.GetAttribute("data-name") == "Checkout").Click();
        cut.Find(".tm-dod-delete").Click();
        cut.Find(".tm-dod-delete-confirm-ok").Click();

        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 1);
        cut.Markup.Should().NotContain("Checkout");
    }

    [Fact]
    public void Delete_Hidden_WhenCapabilityAbsent()
    {
        var cut = RenderOpen(Seeded(DocumentLibraryCapabilities.Search));
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();

        cut.FindAll(".tm-dod-delete").Should().BeEmpty();
    }

    // ── 1.16 Error + empty ────────────────────────────────────────────────────

    [Fact]
    public void Error_ShowsMessageAndRetry()
    {
        var provider = new ThrowingLibraryProvider();
        var cut = Render<TmDocumentOpenDialog>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.Kind, TempoDocumentKind.Wireframe)
            .Add(c => c.Open, true));

        cut.WaitForState(() => cut.FindAll(".tm-dod-error").Count > 0);
        cut.Markup.Should().Contain("Could not load documents.");
        cut.FindAll(".tm-dod-retry").Should().NotBeEmpty();
    }

    [Fact]
    public void Empty_Folder_ShowsEmptyState()
    {
        var cut = RenderOpen(Seeded());

        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Archive")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-empty").Count > 0);
        cut.Markup.Should().Contain("This folder is empty.");
    }

    // ── 1.17 Accessibility ────────────────────────────────────────────────────

    [Fact]
    public void OpenButton_HasAccessibleName()
    {
        var cut = RenderOpen(Seeded());

        cut.Find(".tm-dod-open").GetAttribute("aria-label").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Rows_AreKeyboardFocusable_AndEnterConfirms()
    {
        DocumentOpenResult? result = null;
        var cut = RenderOpen(Seeded(), p => p.Add(c => c.OnSelected,
            (DocumentOpenResult r) => result = r));
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);

        // Roving tabindex (phase 8): every row is focusable, exactly one is in the tab order (the first, with no selection).
        var row = cut.FindAll(".tm-dod-row").First(r => r.GetAttribute("data-name") == "Home page");
        row.HasAttribute("tabindex").Should().BeTrue();
        cut.FindAll(".tm-dod-row").Count(r => r.GetAttribute("tabindex") == "0").Should().Be(1);

        row.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        result.Should().NotBeNull();
        result!.Name.Should().Be("Home page");
    }

    // ── Phase 1 (wireframe redesign plan): keyboard + ARIA additions ──────────

    private IRenderedComponent<TmDocumentOpenDialog> RenderGrid(
        Action<ComponentParameterCollectionBuilder<TmDocumentOpenDialog>>? extra = null)
    {
        var cut = RenderOpen(Seeded(), extra);
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);
        cut.Find(".tm-dod-view-grid").Click();
        return cut;
    }

    [Fact]
    public void GridCard_IsFocusable_EnterOpens_SpaceSelects()
    {
        DocumentOpenResult? result = null;
        var cut = RenderGrid(p => p.Add(c => c.OnSelected, (DocumentOpenResult r) => result = r));

        AngleSharp.Dom.IElement Card() => cut.FindAll(".tm-dod-card").First(c => c.GetAttribute("data-name") == "Home page");
        // Roving tabindex (phase 8): every card is focusable, exactly one is in the tab order.
        Card().HasAttribute("tabindex").Should().BeTrue();
        cut.FindAll(".tm-dod-card").Count(c => c.GetAttribute("tabindex") == "0").Should().Be(1);
        Card().GetAttribute("role").Should().NotBeNullOrEmpty();

        // Space selects (on keyup, like a native button) without opening.
        Card().KeyDown(new KeyboardEventArgs { Key = " " });
        Card().KeyUp(new KeyboardEventArgs { Key = " " });
        result.Should().BeNull();
        cut.FindAll(".tm-dod-card--selected").Should().ContainSingle();
        cut.Find(".tm-dod-card--selected").GetAttribute("data-name").Should().Be("Home page");

        // Enter opens.
        Card().KeyDown(new KeyboardEventArgs { Key = "Enter" });
        result.Should().NotBeNull();
        result!.Name.Should().Be("Home page");
    }

    [Fact]
    public void GridCard_Enter_OpensExactlyOnce_EvenWhenHeld()
    {
        var results = new List<DocumentOpenResult>();
        var cut = RenderGrid(p => p.Add(c => c.OnSelected, (DocumentOpenResult r) => results.Add(r)));
        cut.FindAll(".tm-dod-card").First().KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.FindAll(".tm-dod-card").First().KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });

        results.Should().HaveCount(1);
    }

    [Fact]
    public void GridCard_ExposesSelectedState()
    {
        var cut = RenderGrid();
        var card = cut.FindAll(".tm-dod-card").First(c => c.GetAttribute("data-name") == "Checkout");
        card.GetAttribute("aria-selected").Should().Be("false");

        card.Click();

        cut.FindAll(".tm-dod-card").First(c => c.GetAttribute("data-name") == "Checkout")
            .GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void ListRow_Space_SelectsExactlyOnce_AndDoesNotOpen()
    {
        DocumentOpenResult? result = null;
        var cut = RenderOpen(Seeded(), p => p.Add(c => c.OnSelected, (DocumentOpenResult r) => result = r));
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);

        cut.FindAll(".tm-dod-row").First().KeyDown(new KeyboardEventArgs { Key = " " });
        cut.FindAll(".tm-dod-row").First().KeyUp(new KeyboardEventArgs { Key = " " });

        result.Should().BeNull();
        cut.FindAll(".tm-dod-row--selected").Should().ContainSingle();
    }

    [Fact]
    public void SortHeader_HasAriaSort()
    {
        var cut = RenderOpen(Seeded());
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);

        // Default: sorted by name ascending; the other sortable column and the static one are not sorted.
        SortOf(cut, ".tm-dod-col-name").Should().Be("ascending");
        SortOf(cut, ".tm-dod-col-modified").Should().Be("none");

        cut.Find(".tm-dod-col-name").Click();
        cut.WaitForAssertion(() =>
            SortOf(cut, ".tm-dod-col-name").Should().Be("descending"));

        cut.Find(".tm-dod-col-modified").Click();
        cut.WaitForAssertion(() =>
        {
            SortOf(cut, ".tm-dod-col-modified").Should().Be("ascending");
            SortOf(cut, ".tm-dod-col-name").Should().Be("none");
        });
    }

    private static string? SortOf(IRenderedComponent<TmDocumentOpenDialog> cut, string buttonSelector)
        => cut.Find(buttonSelector).ParentElement!.GetAttribute("aria-sort");

    [Fact]
    public void SortHeader_IsAColumnHeader_SoAriaSortIsValid()
    {
        var cut = RenderOpen(Seeded());
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);

        cut.Find(".tm-dod-col-name").ParentElement!.GetAttribute("role").Should().Be("columnheader");
        cut.Find(".tm-dod-col-modified").ParentElement!.GetAttribute("role").Should().Be("columnheader");
        cut.Find(".tm-dod-list-head").GetAttribute("role").Should().Be("row");
    }

    [Fact]
    public void TreeItem_HasAriaLevelAndAriaSelected()
    {
        var cut = RenderOpen(Seeded());

        var nodes = cut.FindAll(".tm-dod-tree-node");
        var root = nodes.First(n => n.TextContent.Contains("All documents"));
        var designs = nodes.First(n => n.TextContent.Contains("Designs"));
        var mobile = nodes.First(n => n.TextContent.Contains("Mobile"));

        root.GetAttribute("aria-level").Should().Be("1");
        designs.GetAttribute("aria-level").Should().Be("2");
        mobile.GetAttribute("aria-level").Should().Be("3");
        root.GetAttribute("aria-selected").Should().Be("true");
        designs.GetAttribute("aria-selected").Should().Be("false");

        designs.Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);

        var after = cut.FindAll(".tm-dod-tree-node");
        after.First(n => n.TextContent.Contains("Designs")).GetAttribute("aria-selected").Should().Be("true");
        after.First(n => n.TextContent.Contains("All documents")).GetAttribute("aria-selected").Should().Be("false");
    }

    // ── Phase 2 carry-forward (phase-1 review): grid-item column headers + Space scroll guard ──

    [Fact]
    public void SortHeader_ColumnHeaderIsTheGridItem_NotADisplayContentsWrapper()
    {
        var cut = RenderOpen(Seeded());
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);

        foreach (var selector in new[] { ".tm-dod-col-name", ".tm-dod-col-modified" })
        {
            var header = cut.Find(selector).ParentElement!;
            header.GetAttribute("role").Should().Be("columnheader");
            header.GetAttribute("aria-sort").Should().NotBeNull();
            (header.GetAttribute("style") ?? "").Should().NotContain("display:contents",
                "display:contents drops the element from the accessibility tree in some engines — the header is the grid item itself");
            header.ParentElement!.ClassList.Should().Contain("tm-dod-list-head", "the header is a direct grid child of the head row");
        }
    }

    private const string SpaceGuardModule = "./_content/Tempo.Blazor/Components/Files/TmDocumentOpenDialog.razor.js";

    [Fact]
    public void Open_AttachesTheSpaceScrollGuard_ToTheDialogRoot()
    {
        var guard = JSInterop.SetupModule(SpaceGuardModule);
        guard.Mode = JSRuntimeMode.Loose;

        var cut = RenderOpen(Seeded());

        cut.WaitForAssertion(() => guard.Invocations["attachSpaceGuard"].Should().ContainSingle(),
            TimeSpan.FromSeconds(5));
        guard.Invocations["attachSpaceGuard"].Single().Arguments.Should().HaveCount(2,
            "only the dialog root and the per-instance registration key travel — the module knows the .tm-dod-row / .tm-dod-card targets itself");
    }

    [Fact]
    public void Closed_DoesNotImportTheSpaceScrollGuard()
    {
        var guard = JSInterop.SetupModule(SpaceGuardModule);
        guard.Mode = JSRuntimeMode.Loose;

        Render<TmDocumentOpenDialog>(p => p
            .Add(c => c.Provider, Seeded())
            .Add(c => c.Kind, TempoDocumentKind.Wireframe)
            .Add(c => c.Open, false));

        guard.Invocations["attachSpaceGuard"].Should().BeEmpty();
    }

    [Fact]
    public void RowAndCardActivation_StillExactlyOnce_AfterTheGuard()
    {
        // The guard only suppresses the browser's default scroll on Space keydown; activation stays on keyup.
        var selected = new List<DocumentOpenResult>();
        var cut = RenderOpen(Seeded(), p => p.Add(c => c.OnSelected,
            Microsoft.AspNetCore.Components.EventCallback.Factory.Create<DocumentOpenResult>(this, selected.Add)));
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForState(() => cut.FindAll(".tm-dod-row").Count == 2);

        cut.FindAll(".tm-dod-row").First().KeyDown(new KeyboardEventArgs { Key = " " });
        cut.FindAll(".tm-dod-row--selected").Should().BeEmpty("Space selects on keyup, not on keydown");
        cut.FindAll(".tm-dod-row").First().KeyUp(new KeyboardEventArgs { Key = " " });

        cut.FindAll(".tm-dod-row--selected").Should().ContainSingle();
        selected.Should().BeEmpty();
    }
}