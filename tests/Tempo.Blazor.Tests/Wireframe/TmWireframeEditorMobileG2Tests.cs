using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Commands;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// The G2 (mobile viewing + light edits) review debt of plan 643bba06: the focus returns to the action-bar tile that opened the sheet, a selection
/// that peeks the sheet is announced politely, the sheet's scroll reset belongs to the shell, and the render-gate token follows the command
/// stack's <see cref="WireframeCommandStack.Revision"/> instead of a hand-bumped counter.
/// </summary>
public class TmWireframeEditorMobileG2Tests : LocalizationTestBase
{
    private IRenderedComponent<TmWireframeEditor> RenderEditor(
        Action<ComponentParameterCollectionBuilder<TmWireframeEditor>>? configure = null)
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterDefinition(new WireframeComponentDef
        {
            Type = "TmButton", DisplayName = "Button", Category = "Test", DefaultWidth = 120, DefaultHeight = 36,
            IsBuiltIn = true, Props = [], RenderSvg = (_, _) => { }
        });
        Services.AddSingleton(registry);

        var doc = new WireframeDocument { Title = "Test", Width = 390, Height = 844 };
        doc.Pages.Clear();
        doc.Pages.Add(new WireframePage { Id = "p1", Name = "Home", Width = 390, Height = 844 });
        doc.ActivePageId = "p1";
        return Render<TmWireframeEditor>(p =>
        {
            p.Add(c => c.Document, doc);
            p.Add(c => c.LayoutMode, TmLayoutMode.Mobile);
            configure?.Invoke(p);
        });
    }

    private static TmWireframeDesignerCanvas Canvas(IRenderedComponent<TmWireframeEditor> cut) =>
        cut.FindComponent<TmWireframeDesignerCanvas>().Instance;

    private static string AddElement(IRenderedComponent<TmWireframeEditor> cut)
    {
        var element = WireframeDocumentExtensions.NewElement("TmButton", 100, 100);
        element.Name = "Hero button";
        Canvas(cut).Document!.Elements.Add(element);
        return element.Id;
    }

    private static void Select(IRenderedComponent<TmWireframeEditor> cut, string id) =>
        cut.InvokeAsync(() => Canvas(cut).JsOnSelectionChanged([id])).GetAwaiter().GetResult();

    private static string Status(IRenderedComponent<TmWireframeEditor> cut) =>
        cut.Find("[data-testid='wd-canvas-status']").TextContent.Trim();

    private IReadOnlyList<Bunit.JSRuntimeInvocation> Calls(string identifier) =>
        JSInterop.Invocations.Where(i => i.Identifier == identifier).ToList();

    // ── Focus returns to the opener ──────────────────────────────────────────────

    [Theory]
    [InlineData("components")]
    [InlineData("pages-layers")]
    public void Closing_TheSheet_ReturnsTheFocusToTheTileThatOpenedIt(string tile)
    {
        var cut = RenderEditor();
        cut.Find($".tm-mobile-action-bar__action[data-action-id='{tile}']").Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-editor-shell__sheet").Should().NotBeEmpty());

        cut.Find(".tm-editor-shell__sheet-close").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Calls("tmWireframeDesigner.focusMobileAction").Should().ContainSingle().Subject;
            call.Arguments[1].Should().Be(tile, "the focus goes back to the tile the user pressed, not to the first one");
        });
    }

    [Fact]
    public void Closing_ASheetThatASelectionOpened_FallsBackToTheFirstTile()
    {
        var cut = RenderEditor();
        var id = AddElement(cut);
        Select(cut, id);
        cut.WaitForAssertion(() => cut.FindAll(".tm-editor-shell__sheet").Should().NotBeEmpty());

        cut.Find(".tm-editor-shell__sheet-close").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Calls("tmWireframeDesigner.focusMobileAction").Should().ContainSingle().Subject;
            call.Arguments[1].Should().BeNull("no tile opened a selection's sheet: the script falls back to the first tile");
        });
    }

    [Fact]
    public void ASelectionPeek_OverASheetATileOpened_KeepsThatTileAsTheOpener()
    {
        var cut = RenderEditor();
        cut.Find(".tm-mobile-action-bar__action[data-action-id='components']").Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-editor-shell__sheet").Should().NotBeEmpty());
        var id = AddElement(cut);

        Select(cut, id);   // the sheet was opened by the tile: the selection moves it to the properties but does not make the sheet "its own"
        cut.Find(".tm-editor-shell__sheet-close").Click();

        cut.WaitForAssertion(() => Calls("tmWireframeDesigner.focusMobileAction").Should().ContainSingle()
            .Which.Arguments[1].Should().Be("components"));
    }

    // ── The polite announcement of a selection peek ───────────────────────────────

    [Fact]
    public void ASelection_ThatPeeksTheSheet_IsAnnouncedPolitely_WithTheSelectionName()
    {
        var cut = RenderEditor();
        var id = AddElement(cut);

        Select(cut, id);

        cut.WaitForAssertion(() => Status(cut).Should().Be("Selected: Hero button. Properties and actions are open."));
        cut.Find("[data-testid='wd-canvas-status']").GetAttribute("aria-live").Should().Be("polite");
    }

    [Fact]
    public void ASelection_OverAnAlreadyOpenInspector_IsNotAnnouncedAgain()
    {
        var cut = RenderEditor();
        var id = AddElement(cut);
        Select(cut, id);
        cut.WaitForAssertion(() => Status(cut).Should().StartWith("Selected:"));
        // The live region alternates a trailing no-break space on every announcement so a repeat is spoken again: the RAW text therefore
        // changes with every announcement, even an identical one.
        string Raw() => cut.Find("[data-testid='wd-canvas-status']").TextContent;
        var raw = Raw();
        var second = WireframeDocumentExtensions.NewElement("TmButton", 200, 200);
        second.Name = "Second";
        Canvas(cut).Document!.Elements.Add(second);

        Select(cut, second.Id);

        cut.Render();
        Raw().Should().Be(raw, "the sheet already shows the properties: moving the selection announces nothing new");
    }
    [Fact]
    public void TheSelectionPeekAnnouncement_IsLocalizedInCzech()
    {
        Services.AddSingleton<ITmLocalizer>(BuildCzechLocalizer());
        var cut = RenderEditor();
        var id = AddElement(cut);

        Select(cut, id);

        cut.WaitForAssertion(() => Status(cut).Should().Be("Vybráno: Hero button. Otevřeny vlastnosti a akce."));
    }

    // ── The render-gate token follows the command stack ───────────────────────────

    [Fact]
    public void ARepaintAfterACommand_NeedsNoHandBumpedVersion_TheStackRevisionDrivesTheGate()
    {
        var cut = RenderEditor();
        var id = AddElement(cut);
        Select(cut, id);
        var before = Canvas(cut).ContentVersion;

        cut.Find("[data-testid='wd-quick-lock']").Click();

        cut.WaitForAssertion(() => Canvas(cut).ContentVersion.Should().NotBe(before,
            "a command pushed through the stack changes the canvas' ContentVersion, so the gated canvas repaints"));
    }

    [Fact]
    public void Revision_GrowsOnPush_Undo_Redo_Clear_AndOnACoalescedMove()
    {
        var doc = new WireframeDocument { Title = "T" };
        doc.EnsureActivePage();
        var element = WireframeDocumentExtensions.NewElement("TmButton", 10, 10);
        doc.Elements.Add(element);
        var stack = new WireframeCommandStack();
        var seen = new List<int> { stack.Revision };
        stack.OnStackChanged += () => seen.Add(stack.Revision);

        stack.Push(new LockElementsCommand(doc, [element.Id]));
        stack.Undo();
        stack.Redo();
        stack.Clear();

        stack.Revision.Should().Be(seen[0] + 4);
        seen.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems("the listener sees the new revision already, and it never repeats");
    }

    [Fact]
    public void Revision_GrowsOnACoalescedMove_ThoughNoStepWasAdded()
    {
        var doc = new WireframeDocument { Title = "T" };
        doc.EnsureActivePage();
        var element = WireframeDocumentExtensions.NewElement("TmButton", 10, 10);
        doc.Elements.Add(element);
        var stack = new WireframeCommandStack();
        stack.Push(new MoveElementsCommand(doc,
            new Dictionary<string, (double X, double Y)> { [element.Id] = (10, 10) },
            new Dictionary<string, (double X, double Y)> { [element.Id] = (20, 20) }));
        var revision = stack.Revision;

        stack.Push(new MoveElementsCommand(doc,
            new Dictionary<string, (double X, double Y)> { [element.Id] = (20, 20) },
            new Dictionary<string, (double X, double Y)> { [element.Id] = (30, 30) }));

        stack.Revision.Should().BeGreaterThan(revision, "a coalesced move still edits the document behind the gated canvas' back");
    }

    // ── The sheet scroll reset lives in the shell ─────────────────────────────────

    [Fact]
    public void ChangingTheSheetTab_AsksTheShellModuleToResetTheScroll_NotTheWireframeScript()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-editor-shell.js");
        module.SetupVoid("resetSheetScroll", _ => true).SetVoidResult();
        var cut = RenderEditor();
        var id = AddElement(cut);
        Select(cut, id);
        cut.WaitForAssertion(() => cut.FindAll(".tm-editor-shell__tabs [role='tab']").Should().NotBeEmpty());

        var tabs = cut.FindAll(".tm-editor-shell__tabs [role='tab']");
        tabs.Last().Click();

        cut.WaitForAssertion(() => module.Invocations.Where(i => i.Identifier == "resetSheetScroll").Should().NotBeEmpty(
            "the shell scrolls the shared sheet body to the top when its tab changes"));
        JSInterop.Invocations.Select(i => i.Identifier).Should().NotContain("tmWireframeDesigner.resetMobileSheetScroll",
            "the wireframe-specific helper moved into TmEditorShell");
    }
}
