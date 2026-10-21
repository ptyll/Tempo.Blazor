using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>bUnit tests for <see cref="TmWireframePagesPanel"/> and <see cref="WireframePageOperations"/> (wireframe redesign plan, phase 4).</summary>
public class TmWireframePagesPanelTests : LocalizationTestBase
{
    /// <summary>Three pages Home (active, 1440x900), Pricing (800x600), About (390x844).</summary>
    private static WireframeDocument NewDocument()
    {
        var doc = new WireframeDocument { Title = "Test" };
        doc.Pages.Add(new WireframePage { Id = "p1", Name = "Home", Width = 1440, Height = 900 });
        doc.Pages.Add(new WireframePage { Id = "p2", Name = "Pricing", Width = 800, Height = 600 });
        doc.Pages.Add(new WireframePage { Id = "p3", Name = "About", Width = 390, Height = 844 });
        doc.ActivePageId = "p1";
        return doc;
    }

    private static WireframeDocument SinglePageDocument()
    {
        var doc = new WireframeDocument { Title = "Test" };
        doc.Pages.Add(new WireframePage { Id = "p1", Name = "Only" });
        doc.ActivePageId = "p1";
        return doc;
    }

    private sealed class Recorder
    {
        public List<string> ActivePages { get; } = [];
        public int DocumentChanges { get; set; }
    }

    private IRenderedComponent<TmWireframePagesPanel> RenderPanel(WireframeDocument doc, Recorder? rec = null, bool readOnly = false)
    {
        rec ??= new Recorder();
        return Render<TmWireframePagesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, readOnly)
            .Add(c => c.ActivePageChanged, EventCallback.Factory.Create<string>(this, id => rec.ActivePages.Add(id)))
            .Add(c => c.DocumentChanged, EventCallback.Factory.Create<WireframeDocument>(this, _ => rec.DocumentChanges++)));
    }

    private static IElement Row(IRenderedComponent<TmWireframePagesPanel> cut, string id) => cut.Find($"[data-page-id='{id}']");

    /// <summary>Re-finds the inline rename input (handler ids change on every render, so a held reference goes stale).</summary>
    private static IElement NameInput(IRenderedComponent<TmWireframePagesPanel> cut, string id) =>
        cut.Find($"[data-page-id='{id}'] input.tm-wd-pages__name-input");

    private static IReadOnlyList<string> Names(WireframeDocument doc) => doc.Pages.Select(p => p.Name).ToList();

    private static void OpenMenu(IRenderedComponent<TmWireframePagesPanel> cut, string id) =>
        cut.Find($"[data-page-id='{id}'] .tm-wd-pages__row-menu .tm-context-menu__trigger").Click();

    private static IElement MenuItem(IRenderedComponent<TmWireframePagesPanel> cut, string id, string action) =>
        cut.Find($"[data-page-id='{id}'] .tm-wd-pages__row-menu-item[data-action='{action}']");

    // ── Rendering ─────────────────────────────────────────────────────────────

    [Fact]
    public void Renders_NumberedList_WithActivePageMarkedCurrent()
    {
        var cut = RenderPanel(NewDocument());

        cut.Find("[data-testid='wd-pages-panel']").ClassList.Should().Contain("tm-wd-pages");
        cut.Find("h3.tm-wd-pages__title").TextContent.Should().Be("Pages");
        var list = cut.Find("ol.tm-wd-pages__list");
        list.GetAttribute("aria-label").Should().Be("Page list");

        var rows = cut.FindAll("li.tm-wd-pages__item");
        rows.Select(r => r.GetAttribute("data-page-id")).Should().Equal("p1", "p2", "p3");
        rows.Select(r => r.QuerySelector(".tm-wd-pages__index")!.TextContent).Should().Equal("1", "2", "3");
        rows.Select(r => r.QuerySelector("button.tm-wd-pages__select")!.TextContent.Trim()).Should().Equal("Home", "Pricing", "About");

        cut.Find("[data-testid='wd-page-p1']").GetAttribute("aria-current").Should().Be("page");
        cut.Find("[data-testid='wd-page-p2']").HasAttribute("aria-current").Should().BeFalse();
        Row(cut, "p1").ClassList.Should().Contain("tm-wd-pages__item--active");
        Row(cut, "p2").ClassList.Should().NotContain("tm-wd-pages__item--active");
        Row(cut, "p1").GetAttribute("draggable").Should().Be("true");
        cut.Find(".tm-wd-pages__index").GetAttribute("aria-hidden").Should().Be("true", "the number is visual only");
    }

    [Fact]
    public void Renders_ClassParameter_AndNoHeaderAddButton()
    {
        var cut = Render<TmWireframePagesPanel>(p => p
            .Add(c => c.Document, NewDocument()).Add(c => c.Class, "extra"));

        cut.Find(".tm-wd-pages").ClassList.Should().Contain("extra");
        cut.FindAll(".tm-wd-pages__header button").Should().BeEmpty();
        cut.FindAll("[data-testid='wd-page-add']").Should().ContainSingle();
    }

    [Fact]
    public void NullDocument_RendersEmptyList()
    {
        var cut = Render<TmWireframePagesPanel>();

        cut.FindAll("li.tm-wd-pages__item").Should().BeEmpty();
    }

    [Fact]
    public void LiveRegion_IsAPoliteStatus()
    {
        var cut = RenderPanel(NewDocument());

        var live = cut.Find(".tm-sr-only[role='status']");
        live.GetAttribute("aria-live").Should().Be("polite");
        live.TextContent.Should().BeEmpty();
    }

    // ── Switching ─────────────────────────────────────────────────────────────

    [Fact]
    public void Click_SwitchesPage_RaisesActivePageChangedAndDocumentChanged()
    {
        var doc = NewDocument();
        var rec = new Recorder();
        var cut = RenderPanel(doc, rec);

        cut.Find("[data-testid='wd-page-p2']").Click();

        doc.ActivePageId.Should().Be("p2");
        rec.ActivePages.Should().Equal("p2");
        rec.DocumentChanges.Should().Be(1);
        cut.Find("[data-testid='wd-page-p2']").GetAttribute("aria-current").Should().Be("page");
        cut.Find("[data-testid='wd-page-p1']").HasAttribute("aria-current").Should().BeFalse();
    }

    [Fact]
    public void Click_OnTheActivePage_RaisesNothing()
    {
        var rec = new Recorder();
        var cut = RenderPanel(NewDocument(), rec);

        cut.Find("[data-testid='wd-page-p1']").Click();

        rec.ActivePages.Should().BeEmpty();
        rec.DocumentChanges.Should().Be(0);
    }

    [Fact]
    public void Enter_OnTheSelectButton_ActivatesExactlyOnce_ViaTheNativeClick()
    {
        // The select control is a native <button>: the browser turns Enter into a click, the panel never emulates it.
        var rec = new Recorder();
        var cut = RenderPanel(NewDocument(), rec);
        var button = cut.Find("[data-testid='wd-page-p2']");

        button.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        rec.ActivePages.Should().BeEmpty("keydown alone must not switch (no emulation on a native button)");
        button.Click();

        rec.ActivePages.Should().Equal("p2");
    }

    // ── Add ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_CreatesPageOfTheActivePagesSize_AndActivatesIt()
    {
        var doc = NewDocument();
        doc.ActivePageId = "p2";                                  // Pricing 800x600
        var rec = new Recorder();
        var cut = RenderPanel(doc, rec);

        cut.Find("[data-testid='wd-page-add']").Click();

        doc.Pages.Should().HaveCount(4);
        var added = doc.Pages[^1];
        added.Name.Should().Be("Page 4");
        added.Width.Should().Be(800);
        added.Height.Should().Be(600);
        doc.ActivePageId.Should().Be(added.Id);
        rec.ActivePages.Should().Equal(added.Id);
        rec.DocumentChanges.Should().Be(1);
        cut.FindAll("li.tm-wd-pages__item").Should().HaveCount(4);
        cut.Find("[data-testid='wd-pages-panel'] [role='status']").TextContent.Should().Be("Page Page 4 added");
    }

    [Fact]
    public void AddButton_HasIconAndText()
    {
        var cut = RenderPanel(NewDocument());

        var add = cut.Find("button.tm-wd-pages__add");
        add.GetAttribute("type").Should().Be("button");
        add.QuerySelector("svg.tm-icon").Should().NotBeNull();
        add.TextContent.Should().Contain("Add page");
    }

    // ── Rename ────────────────────────────────────────────────────────────────

    [Fact]
    public void Rename_ViaMenu_Enter_CommitsAndRaisesDocumentChanged()
    {
        var doc = NewDocument();
        var rec = new Recorder();
        var cut = RenderPanel(doc, rec);

        OpenMenu(cut, "p2");
        MenuItem(cut, "p2", "rename").Click();
        cut.WaitForAssertion(() => Row(cut, "p2").QuerySelector("input.tm-wd-pages__name-input").Should().NotBeNull());
        var input = Row(cut, "p2").QuerySelector("input.tm-wd-pages__name-input")!;
        input.GetAttribute("value").Should().Be("Pricing");
        input.GetAttribute("aria-label").Should().Be("Rename page");
        Row(cut, "p2").GetAttribute("draggable").Should().Be("false", "an input inside a draggable row cannot be edited");

        input.Input("Plans");
        NameInput(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        doc.Pages[1].Name.Should().Be("Plans");
        rec.DocumentChanges.Should().Be(1);
        cut.FindAll("input.tm-wd-pages__name-input").Should().BeEmpty();
        cut.Find("[data-testid='wd-page-p2']").TextContent.Trim().Should().Be("Plans");
        rec.ActivePages.Should().BeEmpty("renaming does not switch the active page");
    }

    [Fact]
    public void Rename_ViaF2_OnTheRow()
    {
        var doc = NewDocument();
        var cut = RenderPanel(doc);

        Row(cut, "p3").KeyDown(new KeyboardEventArgs { Key = "F2" });

        var input = Row(cut, "p3").QuerySelector("input.tm-wd-pages__name-input")!;
        input.GetAttribute("value").Should().Be("About");
        input.Input("Contact");
        NameInput(cut, "p3").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        doc.Pages[2].Name.Should().Be("Contact");
    }

    [Fact]
    public void Rename_ViaDoubleClick()
    {
        var cut = RenderPanel(NewDocument());

        cut.Find("[data-testid='wd-page-p2']").DoubleClick();

        Row(cut, "p2").QuerySelector("input.tm-wd-pages__name-input").Should().NotBeNull();
    }

    [Fact]
    public void Rename_Escape_Cancels()
    {
        var doc = NewDocument();
        var rec = new Recorder();
        var cut = RenderPanel(doc, rec);
        Row(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "F2" });
        var input = Row(cut, "p2").QuerySelector("input.tm-wd-pages__name-input")!;

        input.Input("Changed");
        NameInput(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        doc.Pages[1].Name.Should().Be("Pricing");
        cut.FindAll("input.tm-wd-pages__name-input").Should().BeEmpty();
        rec.DocumentChanges.Should().Be(0);
    }

    [Fact]
    public void Rename_Blur_Commits()
    {
        var doc = NewDocument();
        var cut = RenderPanel(doc);
        Row(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "F2" });
        var input = Row(cut, "p2").QuerySelector("input.tm-wd-pages__name-input")!;

        input.Input("Blurred");
        NameInput(cut, "p2").Blur();

        doc.Pages[1].Name.Should().Be("Blurred");
        cut.FindAll("input.tm-wd-pages__name-input").Should().BeEmpty();
    }

    [Fact]
    public void Rename_BlankOrUnchanged_KeepsTheName_AndRaisesNothing()
    {
        var doc = NewDocument();
        var rec = new Recorder();
        var cut = RenderPanel(doc, rec);

        Row(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "F2" });
        var input = Row(cut, "p2").QuerySelector("input.tm-wd-pages__name-input")!;
        input.Input("   ");
        NameInput(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        doc.Pages[1].Name.Should().Be("Pricing");

        Row(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "F2" });
        NameInput(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        doc.Pages[1].Name.Should().Be("Pricing");

        rec.DocumentChanges.Should().Be(0);
    }

    [Fact]
    public void RowShortcuts_AreInertWhileRenaming()
    {
        var doc = NewDocument();
        var cut = RenderPanel(doc);
        Row(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "F2" });

        Row(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        Names(doc).Should().Equal("Home", "Pricing", "About");
    }

    // ── Duplicate ─────────────────────────────────────────────────────────────

    [Fact]
    public void Duplicate_ViaMenu_DeepCopiesAndActivatesTheCopy()
    {
        var doc = NewDocument();
        doc.Pages[1].Elements.Add(WireframeDocumentExtensions.NewElement("TmButton", 5, 6, 100, 30));
        var rec = new Recorder();
        var cut = RenderPanel(doc, rec);

        OpenMenu(cut, "p2");
        MenuItem(cut, "p2", "duplicate").Click();

        cut.WaitForAssertion(() => doc.Pages.Should().HaveCount(4));
        var copy = doc.Pages.Single(p => p.Name == "Pricing (copy)");
        copy.Id.Should().NotBe("p2").And.StartWith("p").And.HaveLength(8);
        copy.Width.Should().Be(800);
        copy.Elements.Should().ContainSingle();
        copy.Elements[0].Should().NotBeSameAs(doc.Pages[1].Elements[0]);
        doc.ActivePageId.Should().Be(copy.Id);
        rec.ActivePages.Should().Equal(copy.Id);
        rec.DocumentChanges.Should().Be(1);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_ViaMenu_RemovesThePage_AndActivatesANeighbourWhenItWasActive()
    {
        var doc = NewDocument();
        doc.ActivePageId = "p2";
        var rec = new Recorder();
        var cut = RenderPanel(doc, rec);

        OpenMenu(cut, "p2");
        var del = MenuItem(cut, "p2", "delete");
        del.ClassList.Should().Contain("tm-context-menu__item--danger");
        del.Click();

        cut.WaitForAssertion(() => Names(doc).Should().Equal("Home", "About"));
        doc.ActivePageId.Should().Be("p1", "the previous neighbour takes over");
        rec.ActivePages.Should().Equal("p1");
        rec.DocumentChanges.Should().Be(1);
        cut.FindAll("[data-page-id='p2']").Should().BeEmpty();
        cut.Find("[role='status']").TextContent.Should().Be("Page Pricing deleted");
    }

    [Fact]
    public void Delete_OfANonActivePage_KeepsTheActivePage_AndRaisesNoActivePageChanged()
    {
        var doc = NewDocument();
        var rec = new Recorder();
        var cut = RenderPanel(doc, rec);

        OpenMenu(cut, "p3");
        MenuItem(cut, "p3", "delete").Click();

        cut.WaitForAssertion(() => Names(doc).Should().Equal("Home", "Pricing"));
        doc.ActivePageId.Should().Be("p1");
        rec.ActivePages.Should().BeEmpty();
        rec.DocumentChanges.Should().Be(1);
    }

    [Fact]
    public void Delete_FirstPage_ActivatesTheNextOne()
    {
        var doc = NewDocument();
        var rec = new Recorder();
        var cut = RenderPanel(doc, rec);

        OpenMenu(cut, "p1");
        MenuItem(cut, "p1", "delete").Click();

        cut.WaitForAssertion(() => doc.ActivePageId.Should().Be("p2"));
        rec.ActivePages.Should().Equal("p2");
    }

    [Fact]
    public void Delete_LastRemainingPage_IsRefused_NoMenuEntry()
    {
        var doc = SinglePageDocument();
        var cut = RenderPanel(doc);

        OpenMenu(cut, "p1");

        cut.FindAll(".tm-wd-pages__row-menu-item[data-action='delete']").Should().BeEmpty();
        doc.Pages.Should().ContainSingle();
    }

    // ── Reorder ───────────────────────────────────────────────────────────────

    [Fact]
    public void Move_ViaMenu_UpAndDown()
    {
        var doc = NewDocument();
        var rec = new Recorder();
        var cut = RenderPanel(doc, rec);

        OpenMenu(cut, "p2");
        MenuItem(cut, "p2", "down").Click();
        cut.WaitForAssertion(() => Names(doc).Should().Equal("Home", "About", "Pricing"));
        rec.DocumentChanges.Should().Be(1);

        OpenMenu(cut, "p2");
        MenuItem(cut, "p2", "up").Click();
        cut.WaitForAssertion(() => Names(doc).Should().Equal("Home", "Pricing", "About"));
        cut.FindAll("li.tm-wd-pages__item").Select(r => r.GetAttribute("data-page-id")).Should().Equal("p1", "p2", "p3");
        cut.FindAll(".tm-wd-pages__index").Select(i => i.TextContent).Should().Equal("1", "2", "3");
        rec.ActivePages.Should().BeEmpty("reordering never changes the active page");
    }

    [Fact]
    public void MenuEdges_MoveUpDisabledOnFirst_MoveDownDisabledOnLast()
    {
        var cut = RenderPanel(NewDocument());

        OpenMenu(cut, "p1");
        MenuItem(cut, "p1", "up").HasAttribute("disabled").Should().BeTrue();
        MenuItem(cut, "p1", "down").HasAttribute("disabled").Should().BeFalse();
        OpenMenu(cut, "p3");
        MenuItem(cut, "p3", "down").HasAttribute("disabled").Should().BeTrue();
        MenuItem(cut, "p3", "up").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void AltArrowDown_OnTheRow_MovesThePageDown_AndAnnouncesIt()
    {
        var doc = NewDocument();
        var cut = RenderPanel(doc);

        Row(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        Names(doc).Should().Equal("Home", "About", "Pricing");
        cut.Find("[role='status']").TextContent.Should().Be("Page Pricing moved down, position 3 of 3");
    }

    [Fact]
    public void AltArrowUp_OnTheRow_MovesThePageUp_AndAnnouncesIt()
    {
        var doc = NewDocument();
        var cut = RenderPanel(doc);

        Row(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });

        Names(doc).Should().Equal("Pricing", "Home", "About");
        cut.Find("[role='status']").TextContent.Should().Be("Page Pricing moved up, position 1 of 3");
    }

    [Fact]
    public void AltArrow_WorksFromAnyControlOfTheRow_PlainArrowsDoNothing()
    {
        var doc = NewDocument();
        var cut = RenderPanel(doc);

        Row(cut, "p2").QuerySelector(".tm-wd-pages__select")!.KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        Row(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Names(doc).Should().Equal("Home", "Pricing", "About");

        Row(cut, "p2").QuerySelector(".tm-wd-pages__select")!.KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });
        Names(doc).Should().Equal("Pricing", "Home", "About");
    }

    [Fact]
    public void AltArrow_AtTheEdges_IsANoOp()
    {
        var doc = NewDocument();
        var rec = new Recorder();
        var cut = RenderPanel(doc, rec);

        Row(cut, "p1").KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });
        Row(cut, "p3").KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        Names(doc).Should().Equal("Home", "Pricing", "About");
        rec.DocumentChanges.Should().Be(0);
    }

    [Fact]
    public void DeleteKey_DoesNotDeleteAPage()
    {
        var doc = NewDocument();
        var cut = RenderPanel(doc);

        Row(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "Delete" });
        Row(cut, "p2").QuerySelector(".tm-wd-pages__select")!.KeyDown(new KeyboardEventArgs { Key = "Delete" });
        Row(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "Backspace" });

        doc.Pages.Should().HaveCount(3);
    }

    [Fact]
    public void RowMenu_IsFencedFromTheRowShortcuts_AndTriggerIsNamed()
    {
        var cut = RenderPanel(NewDocument());

        var trigger = cut.Find("[data-page-id='p2'] .tm-wd-pages__row-menu .tm-context-menu__trigger");
        trigger.GetAttribute("aria-haspopup").Should().Be("menu");
        trigger.TextContent.Should().Contain("Actions for page Pricing");
        cut.Find("[data-testid='wd-page-menu-p2']").Should().NotBeNull();

        OpenMenu(cut, "p2");
        var fence = Row(cut, "p2").QuerySelector(".tm-wd-pages__menu-fence")!;
        fence.Attributes.Select(a => a.Name).Should().Contain(a => a.Contains("onkeydown") && a.Contains("stoppropagation"));
        fence.QuerySelectorAll(".tm-wd-pages__row-menu-item").Length.Should().Be(5);
    }

    // ── Drag and drop ─────────────────────────────────────────────────────────

    [Fact]
    public void DragAndDrop_ReordersLikeTheEditor()
    {
        var doc = NewDocument();
        var rec = new Recorder();
        var cut = RenderPanel(doc, rec);

        Row(cut, "p1").TriggerEvent("ondragstart", new DragEventArgs());
        Row(cut, "p1").ClassList.Should().Contain("tm-wd-pages__item--dragging");
        Row(cut, "p3").TriggerEvent("ondragenter", new DragEventArgs());
        Row(cut, "p3").ClassList.Should().Contain("tm-wd-pages__item--drag-over");
        Row(cut, "p3").TriggerEvent("ondrop", new DragEventArgs());

        Names(doc).Should().Equal("Pricing", "About", "Home");
        rec.DocumentChanges.Should().Be(1);
        Row(cut, "p3").ClassList.Should().NotContain("tm-wd-pages__item--drag-over");
    }

    [Fact]
    public void DragAndDrop_OntoItself_IsANoOp()
    {
        var doc = NewDocument();
        var rec = new Recorder();
        var cut = RenderPanel(doc, rec);

        Row(cut, "p2").TriggerEvent("ondragstart", new DragEventArgs());
        Row(cut, "p2").TriggerEvent("ondrop", new DragEventArgs());

        Names(doc).Should().Equal("Home", "Pricing", "About");
        rec.DocumentChanges.Should().Be(0);
    }

    // ── Read-only ─────────────────────────────────────────────────────────────

    [Fact]
    public void ReadOnly_HasNoAddMenuDragOrRename_ButSelectingStillWorks()
    {
        var doc = NewDocument();
        var rec = new Recorder();
        var cut = RenderPanel(doc, rec, readOnly: true);

        cut.FindAll("[data-testid='wd-page-add']").Should().BeEmpty();
        cut.FindAll(".tm-wd-pages__row-menu").Should().BeEmpty();
        cut.FindAll("li.tm-wd-pages__item").Should().OnlyContain(r => r.GetAttribute("draggable") == "false");

        Row(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "F2" });
        Row(cut, "p2").KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });
        cut.Find("[data-testid='wd-page-p2']").DoubleClick();
        Row(cut, "p1").TriggerEvent("ondragstart", new DragEventArgs());
        Row(cut, "p3").TriggerEvent("ondrop", new DragEventArgs());
        cut.FindAll("input.tm-wd-pages__name-input").Should().BeEmpty();
        Names(doc).Should().Equal("Home", "Pricing", "About");

        cut.Find("[data-testid='wd-page-p2']").Click();
        doc.ActivePageId.Should().Be("p2");
        rec.ActivePages.Should().Equal("p2");
    }

    // ── Operations ────────────────────────────────────────────────────────────

    [Fact]
    public void Operations_Add_UsesGivenNameAndSize()
    {
        var doc = NewDocument();

        var page = WireframePageOperations.Add(doc, "Settings", 640, 480);

        doc.Pages[^1].Should().BeSameAs(page);
        page.Name.Should().Be("Settings");
        page.Width.Should().Be(640);
        page.Height.Should().Be(480);
    }

    [Fact]
    public void Operations_Duplicate_InsertsRightAfterTheSource_UnknownIdReturnsNull()
    {
        var doc = NewDocument();

        var copy = WireframePageOperations.Duplicate(doc, "p1", "copy");

        copy.Should().NotBeNull();
        doc.Pages.Select(p => p.Id).Should().Equal("p1", copy!.Id, "p2", "p3");
        copy.Name.Should().Be("Home (copy)");
        WireframePageOperations.Duplicate(doc, "nope", "copy").Should().BeNull();
    }

    [Fact]
    public void Operations_Delete_ReturnsNewActiveId_OrNullWhenRefused()
    {
        var doc = NewDocument();
        doc.ActivePageId = "p3";

        WireframePageOperations.Delete(doc, "p3").Should().Be("p2");
        doc.ActivePageId.Should().Be("p2");
        WireframePageOperations.Delete(doc, "nope").Should().BeNull();
        WireframePageOperations.Delete(doc, "p1").Should().Be("p2", "deleting a non-active page keeps the active one");
        WireframePageOperations.Delete(doc, "p2").Should().BeNull("the last page can never be deleted");
        doc.Pages.Should().ContainSingle();
    }

    [Fact]
    public void Operations_Move_ClampsAtTheEdges()
    {
        var doc = NewDocument();

        WireframePageOperations.Move(doc, "p1", -1).Should().Be(-1);
        WireframePageOperations.Move(doc, "p1", +1).Should().Be(1);
        WireframePageOperations.Move(doc, "p3", +1).Should().Be(-1);
        WireframePageOperations.Move(doc, "nope", +1).Should().Be(-1);
        doc.Pages.Select(p => p.Id).Should().Equal("p2", "p1", "p3");
    }

    [Fact]
    public void Operations_Rename_TrimsAndRejectsBlank()
    {
        var doc = NewDocument();

        WireframePageOperations.Rename(doc, "p1", "  Start ").Should().BeTrue();
        doc.Pages[0].Name.Should().Be("Start");
        WireframePageOperations.Rename(doc, "p1", "   ").Should().BeFalse();
        WireframePageOperations.Rename(doc, "p1", "Start").Should().BeFalse("unchanged");
        WireframePageOperations.Rename(doc, "nope", "X").Should().BeFalse();
    }
}
