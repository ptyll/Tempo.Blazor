using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.DocumentLibrary;
using Tempo.Blazor.Tests.Fixtures;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Files;

/// <summary>
/// WF4 phase 9 (wireframe redesign plan ce19aa8c): the mobile layout of <see cref="TmDocumentOpenDialog"/>. In a
/// dialog body narrower than 640 px (or with <c>LayoutMode=Mobile</c>) the modal is a fullscreen panel whose footer
/// never leaves the screen, the folder tree is replaced by drill-down folder rows, documents are two-line rows with a
/// preview, the row menu (⋯) renames / deletes, a <c>TmMobileActionBar</c> carries New folder / Rename / Delete next to
/// the primary Open, and the Link / Copy choice stays as compact radio cards. <see cref="DocumentOpenResult"/> and the
/// provider contract are unchanged.
/// </summary>
public class TmDocumentOpenDialogMobileTests : LocalizationTestBase
{
    private const string GuardModule = "./_content/Tempo.Blazor/Components/Files/TmDocumentOpenDialog.razor.js";
    private const string PreviewSvg = "<svg id=\"home-preview\" viewBox=\"0 0 10 10\"><rect width=\"10\" height=\"10\"/></svg>";

    private static InMemoryDocumentLibraryProvider Seeded(
        DocumentLibraryCapabilities caps = DocumentLibraryCapabilities.All)
    {
        var p = new InMemoryDocumentLibraryProvider(caps);
        p.AddFolder(TempoDocumentKind.Wireframe, "/Designs");
        p.AddFolder(TempoDocumentKind.Wireframe, "/Designs/Mobile");
        p.AddFolder(TempoDocumentKind.Wireframe, "/Archive");
        p.AddDocument(TempoDocumentKind.Wireframe, "Home page", "/Designs",
            modifiedAt: new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), previewSvg: PreviewSvg, author: "Jana Novotna");
        p.AddDocument(TempoDocumentKind.Wireframe, "Checkout", "/Designs",
            modifiedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        p.AddDocument(TempoDocumentKind.Wireframe, "Login screen", "/Designs/Mobile",
            modifiedAt: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), author: "Petr Svoboda");
        p.AddDocument(TempoDocumentKind.Wireframe, "Root note", "/",
            modifiedAt: new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc));
        return p;
    }

    private IRenderedComponent<TmDocumentOpenDialog> RenderMobile(
        ITempoDocumentLibraryProvider provider,
        Action<ComponentParameterCollectionBuilder<TmDocumentOpenDialog>>? extra = null,
        TmLayoutMode layout = TmLayoutMode.Mobile)
        => Render<TmDocumentOpenDialog>(p =>
        {
            p.Add(c => c.Provider, provider);
            p.Add(c => c.Kind, TempoDocumentKind.Wireframe);
            p.Add(c => c.Open, true);
            p.Add(c => c.SearchDebounceMs, 0);
            p.Add(c => c.LayoutMode, layout);
            extra?.Invoke(p);
        });

    private static IElement FolderRow(IRenderedComponent<TmDocumentOpenDialog> cut, string name)
        => cut.FindAll(".tm-dod-folder-row").First(r => r.GetAttribute("data-path")!.EndsWith("/" + name, StringComparison.Ordinal));

    private static IElement DocRow(IRenderedComponent<TmDocumentOpenDialog> cut, string name)
        => cut.FindAll(".tm-dod-row").First(r => r.GetAttribute("data-name") == name);

    private static string[] DocNames(IRenderedComponent<TmDocumentOpenDialog> cut)
        => cut.FindAll(".tm-dod-row").Select(r => r.GetAttribute("data-name")!).ToArray();

    private static void Drill(IRenderedComponent<TmDocumentOpenDialog> cut, string folder, string expectedFirstDoc)
    {
        FolderRow(cut, folder).Click();
        cut.WaitForAssertion(() => DocNames(cut).Should().Contain(expectedFirstDoc));
    }

    private static KeyboardEventArgs Key(string key) => new() { Key = key };

    private static void OpenRowMenu(IRenderedComponent<TmDocumentOpenDialog> cut, string docName)
    {
        DocRow(cut, docName).QuerySelector(".tm-dod-row-menu button.tm-dropdown-trigger")!.Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-dropdown-item").Should().NotBeEmpty());
    }

    // ── Presentation ─────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_UsesFullscreenPresentation_WithStickyFooter()
    {
        var cut = RenderMobile(Seeded());

        var overlay = cut.Find(".tm-modal-overlay");
        overlay.ClassList.Should().Contain("tm-modal--fullscreen-presentation");
        overlay.ClassList.Should().NotContain("tm-modal--sheet").And.NotContain("tm-modal--dialog");
        cut.Find(".tm-dod").GetAttribute("data-layout").Should().Be("mobile");

        // The footer is the modal's own footer slot: a flex sibling AFTER the scrolling body, so it stays on screen
        // while the body scrolls (the fullscreen panel is height: 100%).
        var kids = cut.Find(".tm-modal").Children.ToList();
        var bodyAt = kids.FindIndex(c => c.ClassList.Contains("tm-modal-body"));
        var footerAt = kids.FindIndex(c => c.ClassList.Contains("tm-modal-footer"));
        bodyAt.Should().BeGreaterThanOrEqualTo(0);
        footerAt.Should().BeGreaterThan(bodyAt);
        var footer = cut.Find(".tm-modal-footer .tm-dod-footer");
        footer.ClassList.Should().Contain("tm-dod-footer--mobile");
        footer.GetAttribute("data-layout").Should().Be("mobile");
        footer.QuerySelector(".tm-dod-open").Should().NotBeNull("the primary Open action lives in the sticky footer");
    }

    [Theory]
    [InlineData(TmLayoutMode.Desktop)]
    [InlineData(TmLayoutMode.Tablet)]
    public void DesktopAndTablet_StayAnXLargeDialog(TmLayoutMode mode)
    {
        var cut = RenderMobile(Seeded(), layout: mode);

        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--dialog",
            "only the mobile layout is fullscreen; a forced Tablet / Desktop layout is a dialog even on a phone viewport");
        cut.Find(".tm-modal").ClassList.Should().Contain("tm-modal--xl");
        cut.FindAll(".tm-dod-tree").Should().ContainSingle();
        cut.FindAll(".tm-dod-folder-row").Should().BeEmpty();
        cut.FindAll(".tm-dod-footer--mobile").Should().BeEmpty();
    }

    // ── Folders: no tree, drill-down rows ───────────────────────────────────

    [Fact]
    public void Mobile_HidesTree_ListsSubfoldersAsRows()
    {
        var cut = RenderMobile(Seeded());

        cut.FindAll(".tm-dod-tree").Should().BeEmpty();
        cut.FindAll("[role=tree]").Should().BeEmpty();
        cut.FindAll(".tm-dod-tree-node").Should().BeEmpty();

        // The subfolders of the CURRENT folder (taken from the existing folder tree - no provider change) are rows.
        var rows = cut.FindAll(".tm-dod-folder-row");
        rows.Select(r => r.GetAttribute("data-path")).Should().Equal("/Archive", "/Designs");
        rows[1].TextContent.Should().Contain("Designs");
        rows[1].QuerySelector(".tm-dod-folder-chevron svg").Should().NotBeNull("a drill-down row ends with a chevron");
        rows[1].GetAttribute("aria-label").Should().Be("Open folder Designs");
        rows[1].TagName.Should().Be("BUTTON", "a native button activates natively - no keyboard emulation");

        // The documents of the root are below the folders.
        DocNames(cut).Should().Equal("Root note");

        Drill(cut, "Designs", "Home page");
        cut.FindAll(".tm-dod-folder-row").Select(r => r.GetAttribute("data-path")).Should().Equal("/Designs/Mobile");
    }

    [Fact]
    public void Mobile_FolderRowTap_DrillsDown_AndBreadcrumbGoesBack()
    {
        var provider = new RecordingLibraryProvider(Seeded());
        var cut = RenderMobile(provider);

        Drill(cut, "Designs", "Home page");
        provider.Calls.Last().FolderPath.Should().Be("/Designs");
        Drill(cut, "Mobile", "Login screen");
        DocNames(cut).Should().Equal("Login screen");
        cut.Find(".tm-dod-crumb--current").TextContent.Should().Contain("Mobile");

        // The back button steps ONE level up ...
        cut.Find(".tm-dod-back").GetAttribute("aria-label").Should().Be("Back");
        cut.Find(".tm-dod-back").Click();
        cut.WaitForAssertion(() => DocNames(cut).Should().BeEquivalentTo("Home page", "Checkout"));
        cut.Find(".tm-dod-crumb--current").TextContent.Should().Contain("Designs");

        // ... and a breadcrumb crumb jumps to that level (here: the home crumb, the library root).
        Drill(cut, "Mobile", "Login screen");
        cut.FindAll(".tm-dod-breadcrumb .tm-dod-crumb").First(c => c.GetAttribute("aria-label") == "Home").Click();
        cut.WaitForAssertion(() => DocNames(cut).Should().Equal("Root note"));
        cut.FindAll(".tm-dod-back").Should().BeEmpty("the library root has nowhere to go back to");
    }

    [Fact]
    public void Mobile_Search_ReplacesFoldersWithResults_AndKeepsFullWidth()
    {
        var cut = RenderMobile(Seeded());
        cut.FindAll(".tm-dod-folder-row").Should().NotBeEmpty();

        cut.Find(".tm-dod-search").Input("Login");

        cut.WaitForAssertion(() => DocNames(cut).Should().Equal("Login screen"));
        cut.FindAll(".tm-dod-folder-row").Should().BeEmpty("a search is a library-wide query of documents");
    }

    [Fact]
    public void Mobile_Recent_IsARowAtTheRoot_WhenShowRecent()
    {
        var provider = new RecordingLibraryProvider(Seeded());
        var cut = RenderMobile(provider, p => p.Add(c => c.ShowRecent, true));

        var recent = cut.Find(".tm-dod-folder-row--recent");
        recent.TextContent.Should().Contain("Recent");
        recent.Click();

        cut.WaitForAssertion(() =>
        {
            provider.Calls.Last().Should().Match<RecordingLibraryProvider.BrowseCall>(c =>
                c.FolderPath == null && c.SortField == DocumentLibrarySortField.Modified && c.Descending);
            cut.Find(".tm-dod-crumb--current").TextContent.Should().Contain("Recent");
        });
        cut.FindAll(".tm-dod-folder-row").Should().BeEmpty("Recent lists documents only");
        cut.Find(".tm-dod-back").Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-dod-folder-row--recent").Should().ContainSingle());
    }

    // ── Documents: two-line rows with a preview ──────────────────────────────

    [Fact]
    public void Mobile_DocumentRow_TwoLinesWithPreview()
    {
        var cut = RenderMobile(Seeded());
        Drill(cut, "Designs", "Home page");

        var row = DocRow(cut, "Home page");
        row.QuerySelector(".tm-dod-row-name")!.TextContent.Trim().Should().Be("Home page");
        row.QuerySelector(".tm-dod-row-meta")!.TextContent.Trim().Should().Be("2026-01-03 · Jana Novotna");
        row.QuerySelector(".tm-dod-row-preview svg#home-preview").Should().NotBeNull("the preview is the document's PreviewSvg, the same way the grid renders it");

        var noPreview = DocRow(cut, "Checkout");
        noPreview.QuerySelector(".tm-dod-row-preview svg").Should().NotBeNull("a document without a preview shows the file icon");
        noPreview.QuerySelector("#home-preview").Should().BeNull();
        noPreview.QuerySelector(".tm-dod-row-meta")!.TextContent.Trim().Should().Be("2026-01-01", "no author, no separator");
    }

    [Fact]
    public void Mobile_DocumentRows_AreAGridOfRows_WithOneRovingTabStop_AndSelectOnTap()
    {
        var cut = RenderMobile(Seeded());
        Drill(cut, "Designs", "Home page");

        cut.Find(".tm-dod-list").GetAttribute("role").Should().Be("grid");
        cut.Find(".tm-dod-list").GetAttribute("aria-label").Should().Be("Documents");
        cut.FindAll(".tm-dod-row").Count(r => r.GetAttribute("tabindex") == "0").Should().Be(1);

        DocRow(cut, "Checkout").Click();
        DocRow(cut, "Checkout").GetAttribute("aria-selected").Should().Be("true");
        DocRow(cut, "Checkout").ClassList.Should().Contain("tm-dod-row--selected");
        DocRow(cut, "Home page").GetAttribute("aria-selected").Should().Be("false");
    }

    [Fact]
    public void Mobile_GridView_KeepsTheCards_AndFolderRowsStayAbove()
    {
        var cut = RenderMobile(Seeded());
        Drill(cut, "Designs", "Home page");

        cut.Find(".tm-dod-view-grid").Click();

        cut.FindAll(".tm-dod-card").Should().HaveCount(2);
        cut.FindAll(".tm-dod-folder-row").Should().ContainSingle("the Mobile subfolder");
        cut.FindAll(".tm-dod-row").Should().BeEmpty();
        cut.Find(".tm-dod-view-grid").GetAttribute("aria-pressed").Should().Be("true");
    }

    // ── Row menu ───────────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_RowMenu_RenameDelete()
    {
        var provider = Seeded();
        var cut = RenderMobile(provider);
        Drill(cut, "Designs", "Home page");

        var trigger = DocRow(cut, "Home page").QuerySelector(".tm-dod-row-menu button.tm-dropdown-trigger")!;
        trigger.TextContent.Trim().Should().Be("More actions for Home page", "the trigger is named by the row it acts on");

        // Rename through the menu: the row becomes the selection and the inline rename bar opens with its name.
        OpenRowMenu(cut, "Home page");
        cut.FindAll(".tm-dropdown-item").Select(i => i.TextContent.Trim()).Should().Equal("Rename", "Delete");
        cut.FindAll(".tm-dropdown-item").First(i => i.TextContent.Contains("Rename")).Click();

        cut.WaitForAssertion(() => cut.Find(".tm-dod-rename-input").GetAttribute("value").Should().Be("Home page"));
        DocRow(cut, "Home page").GetAttribute("aria-selected").Should().Be("true");
        cut.Find(".tm-dod-rename-input").Change("Landing");
        cut.Find(".tm-dod-rename-confirm").Click();
        cut.WaitForAssertion(() => DocNames(cut).Should().Contain("Landing").And.NotContain("Home page"));

        // Delete through the menu of ANOTHER row targets that row, not the previous selection.
        OpenRowMenu(cut, "Checkout");
        cut.FindAll(".tm-dropdown-item").First(i => i.TextContent.Contains("Delete")).Click();
        cut.WaitForAssertion(() => cut.Find(".tm-dod-delete-confirm").TextContent.Should().Contain("Checkout"));
        cut.Find(".tm-dod-delete-confirm-ok").Click();
        cut.WaitForAssertion(() => DocNames(cut).Should().Equal("Landing"));
    }

    [Fact]
    public void Mobile_RowMenu_FollowsTheProviderCapabilities()
    {
        var renameOnly = RenderMobile(Seeded(DocumentLibraryCapabilities.Search | DocumentLibraryCapabilities.Rename));
        Drill(renameOnly, "Designs", "Home page");
        OpenRowMenu(renameOnly, "Home page");
        renameOnly.FindAll(".tm-dropdown-item").Select(i => i.TextContent.Trim()).Should().Equal("Rename");

        var readOnly = RenderMobile(Seeded(DocumentLibraryCapabilities.Search));
        Drill(readOnly, "Designs", "Home page");
        readOnly.FindAll(".tm-dod-row-menu").Should().BeEmpty("nothing to offer, no ⋯");
        readOnly.FindAll("[data-action-id=rename]").Should().BeEmpty();
    }

    [Fact]
    public void Mobile_RowMenu_IsFencedFromTheRowsKeyboardActivation()
    {
        DocumentOpenResult? result = null;
        var cut = RenderMobile(Seeded(), p => p.Add(c => c.OnSelected, (DocumentOpenResult r) => result = r));
        Drill(cut, "Designs", "Home page");
        var trigger = DocRow(cut, "Home page").QuerySelector(".tm-dod-row-menu button.tm-dropdown-trigger")!;

        var enter = () => trigger.KeyDown(Key("Enter"));
        var space = () => trigger.KeyUp(Key(" "));

        enter.Should().Throw<MissingEventHandlerException>("Enter on the ⋯ must not open the document on top of the button's own click");
        space.Should().Throw<MissingEventHandlerException>("Space on the ⋯ must not select the row on top of the button's own click");
        result.Should().BeNull();
    }

    [Fact]
    public void Mobile_RowKeyboard_EnterOpens_SpaceSelects_ArrowsMoveFocus()
    {
        var guard = JSInterop.SetupModule(GuardModule);
        guard.Mode = JSRuntimeMode.Loose;
        DocumentOpenResult? result = null;
        var cut = RenderMobile(Seeded(), p => p.Add(c => c.OnSelected, (DocumentOpenResult r) => result = r));
        Drill(cut, "Designs", "Home page");

        DocRow(cut, "Checkout").KeyDown(Key("ArrowDown"));
        cut.WaitForAssertion(() => guard.Invocations["focusRoving"].Should().NotBeEmpty());

        DocRow(cut, "Checkout").KeyDown(Key(" "));
        DocRow(cut, "Checkout").KeyUp(Key(" "));
        result.Should().BeNull();
        DocRow(cut, "Checkout").GetAttribute("aria-selected").Should().Be("true");

        DocRow(cut, "Checkout").KeyDown(Key("Enter"));
        result!.Name.Should().Be("Checkout");
    }

    // ── Action bar ─────────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_ActionBar_NewFolderRenameDeleteAndPrimaryOpen()
    {
        DocumentOpenResult? result = null;
        var provider = Seeded();
        var cut = RenderMobile(provider, p => p.Add(c => c.OnSelected, (DocumentOpenResult r) => result = r));
        Drill(cut, "Designs", "Home page");

        var bar = cut.Find(".tm-modal-footer .tm-mobile-action-bar__bar");
        bar.GetAttribute("role").Should().Be("group");
        bar.GetAttribute("aria-label").Should().Be("Document actions");
        bar.QuerySelectorAll(".tm-mobile-action-bar__action").Select(a => a.GetAttribute("data-action-id"))
            .Should().Equal("new-folder", "rename", "delete");
        bar.QuerySelectorAll(".tm-mobile-action-bar__action").Select(a => a.TextContent.Trim())
            .Should().Equal("New folder", "Rename", "Delete");
        cut.FindAll(".tm-mobile-action-bar__more").Should().BeEmpty("three actions fit; nothing overflows");

        // Without a selection Rename / Delete / Open are present but disabled; New folder is available.
        cut.Find("[data-action-id=new-folder]").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-action-id=rename]").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-action-id=delete]").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".tm-dod-open").HasAttribute("disabled").Should().BeTrue();

        // New folder.
        cut.Find("[data-action-id=new-folder]").Click();
        cut.Find(".tm-dod-new-folder-input").Change("Wireframes");
        cut.Find(".tm-dod-new-folder-confirm").Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-dod-folder-row").Select(r => r.GetAttribute("data-path"))
            .Should().Contain("/Designs/Wireframes"));

        // Select -> Rename.
        DocRow(cut, "Checkout").Click();
        cut.Find("[data-action-id=rename]").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-action-id=rename]").Click();
        cut.Find(".tm-dod-rename-input").Change("Cart");
        cut.Find(".tm-dod-rename-confirm").Click();
        cut.WaitForAssertion(() => DocNames(cut).Should().Contain("Cart"));

        // Select -> Delete.
        DocRow(cut, "Cart").Click();
        cut.Find("[data-action-id=delete]").Click();
        cut.Find(".tm-dod-delete-confirm-ok").Click();
        cut.WaitForAssertion(() => DocNames(cut).Should().NotContain("Cart"));

        // Select -> the primary Open.
        DocRow(cut, "Home page").Click();
        cut.Find(".tm-dod-open").HasAttribute("disabled").Should().BeFalse();
        cut.Find(".tm-dod-open").Click();
        result!.Name.Should().Be("Home page");
    }

    [Fact]
    public void Mobile_ActionBar_FollowsTheProviderCapabilities()
    {
        var cut = RenderMobile(Seeded(DocumentLibraryCapabilities.Search | DocumentLibraryCapabilities.CreateFolder));

        cut.FindAll(".tm-mobile-action-bar__action").Select(a => a.GetAttribute("data-action-id")).Should().Equal("new-folder");

        var none = RenderMobile(Seeded(DocumentLibraryCapabilities.Search));
        none.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty("a read-only library has no actions to bar");
        none.Find(".tm-dod-open").Should().NotBeNull();
    }

    [Fact]
    public void Mobile_NewFolderIsUnavailableOnTheRecentNode()
    {
        var cut = RenderMobile(Seeded(), p => p.Add(c => c.ShowRecent, true));

        cut.Find(".tm-dod-folder-row--recent").Click();

        cut.WaitForAssertion(() => cut.Find("[data-action-id=new-folder]").HasAttribute("disabled").Should().BeTrue());
    }

    // ── Inline bars ───────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_InlineBars_SitAboveTheList_AndCanBeCancelled()
    {
        var cut = RenderMobile(Seeded());
        Drill(cut, "Designs", "Home page");

        // The bars are in the sticky head, ABOVE the scrolling list, so they stay reachable with a long list and a keyboard.
        cut.Find("[data-action-id=new-folder]").Click();
        var head = cut.Find(".tm-dod-mobile-head");
        head.QuerySelector(".tm-dod-new-folder-bar").Should().NotBeNull();
        cut.Find(".tm-dod-new-folder-bar .tm-dod-inline-cancel").Click();
        cut.FindAll(".tm-dod-new-folder-bar").Should().BeEmpty();

        DocRow(cut, "Checkout").Click();
        cut.Find("[data-action-id=rename]").Click();
        cut.Find(".tm-dod-mobile-head .tm-dod-rename-bar").Should().NotBeNull();
        cut.Find(".tm-dod-rename-bar .tm-dod-inline-cancel").Click();
        cut.FindAll(".tm-dod-rename-bar").Should().BeEmpty();

        cut.Find("[data-action-id=delete]").Click();
        cut.Find(".tm-dod-mobile-head .tm-dod-delete-confirm").TextContent.Should().Contain("Checkout");
        cut.Find(".tm-dod-delete-confirm-cancel").Click();
        cut.FindAll(".tm-dod-delete-confirm").Should().BeEmpty();
        DocNames(cut).Should().Contain("Checkout");
    }

    [Fact]
    public void Mobile_Rename_EmptyName_ShowsTheValidationMessage()
    {
        var cut = RenderMobile(Seeded());
        Drill(cut, "Designs", "Home page");
        DocRow(cut, "Checkout").Click();
        cut.Find("[data-action-id=rename]").Click();

        cut.Find(".tm-dod-rename-input").Change("  ");
        cut.Find(".tm-dod-rename-confirm").Click();

        cut.Find(".tm-dod-rename-error").TextContent.Should().Contain("Name is required.");
    }

    // ── Mode choice ───────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_ModeToggle_StillAvailable_WhenShowModeToggle()
    {
        DocumentOpenResult? result = null;
        var cut = RenderMobile(Seeded(), p => p.Add(c => c.OnSelected, (DocumentOpenResult r) => result = r));
        Drill(cut, "Designs", "Home page");

        var mode = cut.Find(".tm-modal-footer .tm-dod-mode");
        mode.GetAttribute("role").Should().Be("radiogroup");
        mode.ClassList.Should().Contain("tm-dod-mode--compact");
        mode.QuerySelectorAll(".tm-dod-mode-card").Should().HaveCount(2);
        cut.Find(".tm-dod-mode-link").HasAttribute("checked").Should().BeTrue("Link is the default");

        // The cards sit ABOVE the action bar and the Open button.
        var footerChildren = cut.Find(".tm-dod-footer").Children.Select(c => c.ClassList.First()).ToList();
        footerChildren.IndexOf("tm-dod-mode").Should().BeLessThan(footerChildren.IndexOf("tm-dod-footer-mobile-row"));

        cut.Find(".tm-dod-mode-copy").Change(true);
        DocRow(cut, "Home page").Click();
        cut.Find(".tm-dod-open").Click();
        result!.Mode.Should().Be(DocumentOpenMode.Copy);
    }

    [Fact]
    public void Mobile_ModeToggle_Hidden_WhenShowModeToggleIsFalse()
    {
        var cut = RenderMobile(Seeded(), p => p.Add(c => c.ShowModeToggle, false));

        cut.FindAll(".tm-dod-mode").Should().BeEmpty();
        cut.Find(".tm-dod-open").Should().NotBeNull();
    }

    // ── Result contract ───────────────────────────────────────────────────────

    [Fact]
    public void Mobile_SelectionAndOpen_ReturnsSameDocumentOpenResult()
    {
        var provider = Seeded();
        DocumentOpenResult? desktop = null;
        var d = Render<TmDocumentOpenDialog>(p => p
            .Add(c => c.Provider, provider).Add(c => c.Kind, TempoDocumentKind.Wireframe).Add(c => c.Open, true)
            .Add(c => c.SearchDebounceMs, 0).Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.DefaultMode, DocumentOpenMode.Copy)
            .Add(c => c.OnSelected, (DocumentOpenResult r) => desktop = r));
        d.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        d.WaitForState(() => d.FindAll(".tm-dod-row").Count == 2);
        d.FindAll(".tm-dod-row").First(r => r.GetAttribute("data-name") == "Home page").Click();
        d.Find(".tm-dod-open").Click();

        DocumentOpenResult? mobile = null;
        var cut = RenderMobile(provider, p => p
            .Add(c => c.DefaultMode, DocumentOpenMode.Copy)
            .Add(c => c.OnSelected, (DocumentOpenResult r) => mobile = r));
        Drill(cut, "Designs", "Home page");
        DocRow(cut, "Home page").Click();
        cut.Find(".tm-dod-open").Click();

        desktop.Should().NotBeNull();
        mobile.Should().NotBeNull();
        mobile.Should().BeEquivalentTo(desktop, "the layout never changes what the dialog returns");
        mobile!.Mode.Should().Be(DocumentOpenMode.Copy);
    }

    [Fact]
    public void Mobile_DoubleTap_SelectsOnly_OpenIsTheExplicitButton()
    {
        // A tap on a folder row followed by a quick tap on the row that now sits at the same spot is a double-tap: it must not open it.
        DocumentOpenResult? result = null;
        var cut = RenderMobile(Seeded(), p => p.Add(c => c.OnSelected, (DocumentOpenResult r) => result = r));
        Drill(cut, "Designs", "Home page");

        DocRow(cut, "Checkout").Click();
        DocRow(cut, "Checkout").DoubleClick();

        result.Should().BeNull();
        DocRow(cut, "Checkout").GetAttribute("aria-selected").Should().Be("true");
        cut.Find(".tm-dod-open").Click();
        result!.Name.Should().Be("Checkout");
    }

    [Theory]
    [InlineData(TmLayoutMode.Desktop)]
    [InlineData(TmLayoutMode.Tablet)]
    public void DesktopAndTablet_DoubleClickOnARow_StillOpens(TmLayoutMode mode)
    {
        DocumentOpenResult? result = null;
        var cut = RenderMobile(Seeded(), p => p.Add(c => c.OnSelected, (DocumentOpenResult r) => result = r), mode);
        cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains("Designs")).Click();
        cut.WaitForAssertion(() => DocNames(cut).Should().Contain("Checkout"));

        DocRow(cut, "Checkout").DoubleClick();

        result!.Name.Should().Be("Checkout");
    }
    [Fact]
    public void Mobile_Open_ClosesTheDialog_AndCloseButtonCancels()
    {
        var open = true;
        var cancelled = false;
        var cut = RenderMobile(Seeded(), p => p
            .Add(c => c.OpenChanged, (bool v) => open = v)
            .Add(c => c.OnCancelled, () => cancelled = true));

        cut.Find(".tm-modal-close").Click();

        cancelled.Should().BeTrue("the header close button is the mobile Cancel");
        open.Should().BeFalse();
        cut.FindAll(".tm-dod-cancel").Should().BeEmpty("the fullscreen panel has no second Cancel button");
    }

    // ── Auto: measured, with a hysteresis against the presentation flap ─────────

    [Fact]
    public async Task Auto_MeasuredMobile_SwitchesToTheFullscreenPanel_AndBackToTheDialog()
    {
        var observer = JSInterop.SetupModule("./_content/Tempo.Blazor/js/layout-observer.js");
        observer.Mode = JSRuntimeMode.Loose;
        var cut = Render<TmDocumentOpenDialog>(p => p
            .Add(c => c.Provider, Seeded()).Add(c => c.Kind, TempoDocumentKind.Wireframe)
            .Add(c => c.Open, true).Add(c => c.SearchDebounceMs, 0));
        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--dialog", "the first frame is Desktop");

        var layout = cut.FindComponent<TmLayoutObserver>();
        await cut.InvokeAsync(() => layout.Instance.OnLayoutModeChanged("mobile"));

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--fullscreen-presentation");
            cut.Find(".tm-dod").GetAttribute("data-layout").Should().Be("mobile");
            cut.FindAll(".tm-dod-tree").Should().BeEmpty();
            cut.FindAll(".tm-dod-folder-row").Should().NotBeEmpty();
        });

        // Leaving the fullscreen panel (a wider window) restores the dialog and its tree.
        await cut.InvokeAsync(() => layout.Instance.OnLayoutModeChanged("desktop"));
        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--dialog");
            cut.FindAll(".tm-dod-tree").Should().ContainSingle();
        });
    }

    [Fact]
    public async Task Auto_Hysteresis_FullscreenBody_LeavesMobileOnlyAboveTheWiderThreshold()
    {
        // Entering fullscreen widens the measured body by the chrome the dialog had (margin + padding), so the very
        // width that just measured "mobile" in the dialog would measure "tablet" in the panel and flap forever. The
        // observer therefore classifies with a wider mobile threshold while the panel is up.
        var observer = JSInterop.SetupModule("./_content/Tempo.Blazor/js/layout-observer.js");
        observer.Mode = JSRuntimeMode.Loose;
        var cut = Render<TmDocumentOpenDialog>(p => p
            .Add(c => c.Provider, Seeded()).Add(c => c.Kind, TempoDocumentKind.Wireframe)
            .Add(c => c.Open, true).Add(c => c.SearchDebounceMs, 0));
        var layout = cut.FindComponent<TmLayoutObserver>();
        layout.Instance.Breakpoints!.Sm.Should().Be(640, "the dialog classifies with the documented container threshold");

        await cut.InvokeAsync(() => layout.Instance.OnLayoutModeChanged("mobile"));

        cut.WaitForAssertion(() => cut.FindComponent<TmLayoutObserver>().Instance.Breakpoints!.Sm.Should().BeGreaterThan(640));
        var wider = cut.FindComponent<TmLayoutObserver>().Instance.Breakpoints!.Sm;

        await cut.InvokeAsync(() => cut.FindComponent<TmLayoutObserver>().Instance.OnLayoutModeChanged("tablet"));
        cut.WaitForAssertion(() => cut.FindComponent<TmLayoutObserver>().Instance.Breakpoints!.Sm.Should().Be(640));
        wider.Should().Be(752, "the dialog chrome is 2 * (16 + 16 + 24) = 112 px wider than the edge-to-edge panel").And.BeLessThan(768, "the tablet band must stay reachable");
    }

    [Fact]
    public void Auto_WithAMobileViewportScope_OpensStraightIntoTheMobileLayout_NoDialogFlash()
    {
        var observer = JSInterop.SetupModule("./_content/Tempo.Blazor/js/layout-observer.js");
        observer.Mode = JSRuntimeMode.Loose;
        var provider = Seeded();

        var cut = Render<MobileViewportHost>(p => p.Add(h => h.Provider, provider));

        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--fullscreen-presentation");
        cut.Find(".tm-dod").GetAttribute("data-layout").Should().Be("mobile");
        cut.FindAll(".tm-dod-tree").Should().BeEmpty();
    }

    private sealed class MobileViewportHost : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter] public ITempoDocumentLibraryProvider Provider { get; set; } = default!;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<Microsoft.AspNetCore.Components.CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Value", new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Mobile));
            builder.AddAttribute(2, "Name", TmLayoutScopes.Viewport);
            builder.AddAttribute(3, "ChildContent", (Microsoft.AspNetCore.Components.RenderFragment)(inner =>
            {
                inner.OpenComponent<TmDocumentOpenDialog>(0);
                inner.AddAttribute(1, "Provider", Provider);
                inner.AddAttribute(2, "Kind", TempoDocumentKind.Wireframe);
                inner.AddAttribute(3, "Open", true);
                inner.AddAttribute(4, "SearchDebounceMs", 0);
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    // ── States ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_EmptyFolder_ShowsTheEmptyState_NotAnEmptyList()
    {
        var cut = RenderMobile(Seeded());

        FolderRow(cut, "Archive").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-dod-empty").TextContent.Should().Contain("This folder is empty."));
        cut.FindAll(".tm-dod-list").Should().BeEmpty();
    }

    [Fact]
    public void Mobile_FolderWithOnlySubfolders_IsNotReportedEmpty()
    {
        var cut = RenderMobile(Seeded());
        Drill(cut, "Designs", "Home page");
        Drill(cut, "Mobile", "Login screen");
        cut.Find(".tm-dod-back").Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-dod-folder-row").Should().ContainSingle());

        // The root holds folders and one document: never the empty state while there is anything to show.
        cut.Find(".tm-dod-back").Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-dod-folder-row").Should().HaveCount(2));
        cut.FindAll(".tm-dod-empty").Should().BeEmpty();
    }

    [Fact]
    public void Mobile_LoadMore_StaysAvailable()
    {
        var provider = Seeded();
        for (var i = 0; i < 4; i++)
        {
            provider.AddDocument(TempoDocumentKind.Wireframe, $"Extra {i}", "/");
        }

        var cut = RenderMobile(provider, p => p.Add(c => c.PageSize, 2));

        cut.Find(".tm-dod-load-more").Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-dod-row").Count.Should().BeGreaterThan(2));
    }

    // ── Focus management ─────────────────────────────────────────────────────

    [Fact]
    public void Mobile_OpeningAnInlineBar_MovesFocusIntoIt_DeleteLandsOnTheSafeButton()
    {
        var guard = JSInterop.SetupModule(GuardModule);
        guard.Mode = JSRuntimeMode.Loose;
        var cut = RenderMobile(Seeded());
        Drill(cut, "Designs", "Home page");

        bool Focused(string control) => guard.Invocations["focusRoving"].Any(i =>
            (string)i.Arguments[1]! == "control" && (string)i.Arguments[2]! == control);

        cut.Find("[data-action-id=new-folder]").Click();
        cut.WaitForAssertion(() => Focused("new-folder-input").Should().BeTrue("a keyboard user lands in the field that just appeared"));
        cut.Find(".tm-dod-new-folder-input").GetAttribute("data-dod-control").Should().Be("new-folder-input");

        DocRow(cut, "Checkout").Click();
        cut.Find("[data-action-id=rename]").Click();
        cut.WaitForAssertion(() => Focused("rename-input").Should().BeTrue());

        cut.Find("[data-action-id=delete]").Click();
        cut.WaitForAssertion(() => Focused("delete-cancel").Should().BeTrue("a destructive confirmation never autofocuses the destructive button"));
    }

    [Fact]
    public void Mobile_RowMenuRename_MovesFocusIntoTheRenameInput()
    {
        var guard = JSInterop.SetupModule(GuardModule);
        guard.Mode = JSRuntimeMode.Loose;
        var cut = RenderMobile(Seeded());
        Drill(cut, "Designs", "Home page");

        OpenRowMenu(cut, "Checkout");
        cut.FindAll(".tm-dropdown-item").First(i => i.TextContent.Contains("Rename")).Click();

        cut.WaitForAssertion(() => guard.Invocations["focusRoving"].Should().Contain(i =>
            (string)i.Arguments[1]! == "control" && (string)i.Arguments[2]! == "rename-input"));
    }

    [Fact]
    public void Desktop_BeginningAnInlineBar_DoesNotMoveFocus()
    {
        var guard = JSInterop.SetupModule(GuardModule);
        guard.Mode = JSRuntimeMode.Loose;
        var cut = RenderMobile(Seeded(), layout: TmLayoutMode.Desktop);

        cut.Find(".tm-dod-new-folder").Click();

        guard.Invocations.Where(i => i.Identifier == "focusRoving" && (string)i.Arguments[1]! == "control").Should().BeEmpty(
            "the desktop toolbar keeps its own behaviour: nothing is focused automatically");
    }

    [Fact]
    public void Mobile_Drill_MovesFocusToTheBackButton_AndBackReturnsItToTheFolderWeLeft()
    {
        var guard = JSInterop.SetupModule(GuardModule);
        guard.Mode = JSRuntimeMode.Loose;
        var cut = RenderMobile(Seeded());

        FolderRow(cut, "Designs").Click();
        cut.WaitForAssertion(() => guard.Invocations["focusRoving"].Should().Contain(i =>
            (string)i.Arguments[1]! == "control" && (string)i.Arguments[2]! == "back"));

        cut.Find(".tm-dod-back").Click();
        cut.WaitForAssertion(() => guard.Invocations["focusRoving"].Should().Contain(i =>
            (string)i.Arguments[1]! == "folder" && (string)i.Arguments[2]! == "/Designs"));
    }

    // ── Review fixes: listener lifecycle, hysteresis at the real widths, announcement, Back during a search ──

    [Fact]
    public void Mobile_AttachesTheKeyboardLift_WithAStableKey_AndSpaceGuardUsesTheSameKey()
    {
        var guard = JSInterop.SetupModule(GuardModule);
        guard.Mode = JSRuntimeMode.Loose;
        guard.Setup<bool>("attachKeyboardLift", _ => true).SetResult(true);

        RenderMobile(Seeded());

        var lift = guard.Invocations["attachKeyboardLift"].Last();
        lift.Arguments.Should().HaveCount(2, "the dialog root and the stable registration key");
        var key = (string)lift.Arguments[1]!;
        key.Should().NotBeNullOrEmpty();
        guard.Invocations["attachSpaceGuard"].Last().Arguments[1].Should().Be(key,
            "detach cannot depend on the (replaceable) element reference: both registrations use the per-instance key");
    }

    [Fact]
    public void Mobile_Dispose_ReleasesTheKeyboardLiftAndTheGuard_ByKey()
    {
        var guard = JSInterop.SetupModule(GuardModule);
        guard.Mode = JSRuntimeMode.Loose;
        guard.Setup<bool>("attachKeyboardLift", _ => true).SetResult(true);
        var cut = RenderMobile(Seeded());
        var key = (string)guard.Invocations["attachKeyboardLift"].Last().Arguments[1]!;
        guard.Invocations["detachKeyboardLift"].Should().BeEmpty();

        cut.Instance.DisposeAsync().AsTask().GetAwaiter().GetResult();

        guard.Invocations["detachKeyboardLift"].Should().ContainSingle(i => (string)i.Arguments[0]! == key,
            "the window / visualViewport listeners outlive the component: dispose must release them before the module goes");
        guard.Invocations["detachSpaceGuard"].Should().ContainSingle(i => (string)i.Arguments[0]! == key);
    }

    [Fact]
    public void Mobile_Dispose_WithAnAttachThatHasNotConfirmedYet_StillDetaches()
    {
        // The module answers false while the overlay is not in the DOM yet (it keeps retrying on animation frames): a dispose in that
        // window must still cancel the retry, so the dialog asks for the detach whatever the attach answered.
        var guard = JSInterop.SetupModule(GuardModule);
        guard.Mode = JSRuntimeMode.Loose;
        guard.Setup<bool>("attachKeyboardLift", _ => true).SetResult(false);
        var cut = RenderMobile(Seeded());

        cut.Instance.DisposeAsync().AsTask().GetAwaiter().GetResult();

        guard.Invocations["detachKeyboardLift"].Should().ContainSingle();
    }

    [Fact]
    public void Mobile_Close_DetachesTheKeyboardLift_AndReopenAttachesAgain()
    {
        var guard = JSInterop.SetupModule(GuardModule);
        guard.Mode = JSRuntimeMode.Loose;
        guard.Setup<bool>("attachKeyboardLift", _ => true).SetResult(true);
        var cut = RenderMobile(Seeded());
        guard.Invocations["attachKeyboardLift"].Should().NotBeEmpty();

        cut.Render(p => p.Add(c => c.Open, false));

        cut.WaitForAssertion(() => guard.Invocations["detachKeyboardLift"].Should().ContainSingle());
        guard.Invocations["detachSpaceGuard"].Should().ContainSingle();
    }

    [Fact]
    public void Desktop_NeverAttachesTheKeyboardLift_NorDetachesAtDispose()
    {
        var guard = JSInterop.SetupModule(GuardModule);
        guard.Mode = JSRuntimeMode.Loose;
        var cut = RenderMobile(Seeded(), layout: TmLayoutMode.Desktop);

        cut.Instance.DisposeAsync().AsTask().GetAwaiter().GetResult();

        guard.Invocations["attachKeyboardLift"].Should().BeEmpty();
        guard.Invocations["detachKeyboardLift"].Should().BeEmpty();
    }

    [Fact]
    public async Task Auto_Hysteresis_RealWidths_700StaysMobileWhilePanelIsUp_760BecomesTablet()
    {
        var observer = JSInterop.SetupModule("./_content/Tempo.Blazor/js/layout-observer.js");
        observer.Mode = JSRuntimeMode.Loose;
        var cut = Render<TmDocumentOpenDialog>(p => p
            .Add(c => c.Provider, Seeded()).Add(c => c.Kind, TempoDocumentKind.Wireframe)
            .Add(c => c.Open, true).Add(c => c.SearchDebounceMs, 0));

        // Dialog up (not mobile yet): the plain container thresholds apply, 600 px of body is mobile.
        cut.FindComponent<TmLayoutObserver>().Instance.Breakpoints!.Classify(600).Should().Be(TmLayoutMode.Mobile);
        cut.FindComponent<TmLayoutObserver>().Instance.Breakpoints!.Classify(700).Should().Be(TmLayoutMode.Tablet);

        await cut.InvokeAsync(() => cut.FindComponent<TmLayoutObserver>().Instance.OnLayoutModeChanged("mobile"));

        cut.WaitForAssertion(() =>
        {
            var breakpoints = cut.FindComponent<TmLayoutObserver>().Instance.Breakpoints!;
            breakpoints.Classify(700).Should().Be(TmLayoutMode.Mobile, "the same viewport measures 112 px wider in the edge-to-edge panel");
            breakpoints.Classify(760).Should().Be(TmLayoutMode.Tablet, "above 752 the panel hands back to the dialog");
        });
        // The module is told the widened pair too: it classifies with what it was given.
        observer.Invocations["observe"].Last().Arguments[3].Should().BeEquivalentTo(new { breakpoints = new { sm = 752, md = 768, lg = 768 } });
    }

    // ── Announcement, labels, Back during a search ──

    [Fact]
    public void Mobile_DrillDown_AnnouncesTheFolderWeArrivedIn_ThroughAPoliteStatus()
    {
        var cut = RenderMobile(Seeded());

        var status = cut.Find(".tm-dod-mobile-head [role=status]");
        status.GetAttribute("aria-live").Should().Be("polite");
        status.ClassList.Should().Contain("tm-sr-only", "visually hidden through the shared utility");
        status.TextContent.Trim().Should().NotBeEmpty("the root has a name too");

        Drill(cut, "Designs", "Home page");
        cut.Find(".tm-dod-mobile-head [role=status]").TextContent.Trim().Should().Be("Designs");
        Drill(cut, "Mobile", "Login screen");
        cut.Find(".tm-dod-mobile-head [role=status]").TextContent.Trim().Should().Be("Mobile");
    }

    [Fact]
    public void Mobile_RecentFolderRow_HasTheSameAriaLabelPatternAsTheOtherFolderRows()
    {
        var cut = RenderMobile(Seeded(), p => p.Add(c => c.ShowRecent, true));

        cut.Find(".tm-dod-folder-row--recent").GetAttribute("aria-label").Should().Be("Open folder Recent");
        FolderRow(cut, "Designs").GetAttribute("aria-label").Should().Be("Open folder Designs");
    }

    [Fact]
    public void Mobile_CompactModeHints_UseTheSharedSrOnlyUtility()
    {
        var cut = RenderMobile(Seeded());

        cut.FindAll(".tm-dod-mode-hint").Should().NotBeEmpty().And.OnlyContain(h => h.ClassList.Contains("tm-sr-only"));

        var desktop = RenderMobile(Seeded(), layout: TmLayoutMode.Desktop);
        desktop.FindAll(".tm-dod-mode-hint").Should().NotBeEmpty().And.OnlyContain(h => !h.ClassList.Contains("tm-sr-only"));
    }

    [Fact]
    public void Mobile_BackDuringASearch_ClearsTheSearch_AndStaysInTheFolder()
    {
        var cut = RenderMobile(Seeded());
        Drill(cut, "Designs", "Home page");

        cut.Find(".tm-dod-search").Input("Login");
        cut.WaitForAssertion(() => DocNames(cut).Should().Equal("Login screen"));

        cut.Find(".tm-dod-back").Click();

        cut.WaitForAssertion(() => DocNames(cut).Should().BeEquivalentTo("Home page", "Checkout"));
        cut.Find(".tm-dod-search").GetAttribute("value").Should().BeNullOrEmpty();
        cut.Find(".tm-dod-crumb--current").TextContent.Should().Contain("Designs", "Back only cleared the search: the folder is unchanged");
        cut.FindAll(".tm-dod-folder-row").Select(r => r.GetAttribute("data-path")).Should().Equal("/Designs/Mobile");

        // The NEXT Back steps up a level.
        cut.Find(".tm-dod-back").Click();
        cut.WaitForAssertion(() => DocNames(cut).Should().Equal("Root note"));
    }
}