using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.DocumentEditor.Registry;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Redesign 146c7e1b, DE-1.2: the ribbon runs on the core <see cref="TmToolbar"/> (Overflow=Menu). The priority of every
/// command is the registry's, published as <c>data-priority</c>; measuring and the "More" menu are the core's — the ribbon has
/// no overflow model of its own.
/// </summary>
public class DocumentEditorToolbarOverflowTests : LocalizationTestBase
{
    private static readonly string[] Tabs = ["home", "insert", "math", "layout", "references", "review", "view"];

    // ─── The priority is the registry's ───────────────────────────────────────

    [Fact]
    public void ToolbarItemPriority_MapsOntoTheCoreToolbarPriority()
    {
        ToolbarItemPriority.Primary.ToCorePriority().Should().Be(ToolbarButtonPriority.Primary);
        ToolbarItemPriority.Secondary.ToCorePriority().Should().Be(ToolbarButtonPriority.Secondary);
        ToolbarItemPriority.OverflowOnly.ToCorePriority().Should().Be(ToolbarButtonPriority.OverflowOnly);
    }

    [Theory]
    [InlineData("home")]
    [InlineData("insert")]
    [InlineData("math")]
    [InlineData("layout")]
    [InlineData("references")]
    [InlineData("review")]
    [InlineData("view")]
    public void EveryRibbonCommand_CarriesTheRegistryPriority_OnItsTab(string tab)
    {
        var cut = RenderAdvanced();
        OpenTab(cut, tab);

        var commands = cut.FindAll("[data-testid='document-ribbon-panel'] [data-command]");
        commands.Should().NotBeEmpty();
        foreach (var element in commands)
        {
            var command = element.GetAttribute("data-command")!;
            var declared = DocumentEditorBuiltInToolbar.FindItemByCommandName(command);
            declared.Should().NotBeNull($"'{command}' on the {tab} tab must be declared in DocumentEditorBuiltInToolbar");
            element.GetAttribute("data-priority").Should().Be(declared!.Priority.ToDataValue(),
                $"'{command}' publishes the registry priority as its stable data-priority contract");
        }
    }

    [Fact]
    public void Registry_DeclaresEveryCommandTheRibbonRenders_ChecklistAgainstTheBuiltInToolbar()
    {
        var cut = RenderAdvanced(parameters => parameters
            .Add(p => p.ShowDebugTools, true)
            .Add(p => p.ShowPrintPreviewTools, true)
            .Add(p => p.ShowEquationTools, true)
            .Add(p => p.ShowSymbolTools, true)
            .Add(p => p.ShowAdvancedFields, true));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tab in Tabs)
        {
            OpenTab(cut, tab);
            foreach (var element in cut.FindAll("[data-testid='document-ribbon-panel'] [data-command]"))
            {
                seen.Add(element.GetAttribute("data-command")!);
            }
        }

        // OverflowOnly commands render only a position marker on the bar; their entries live in More (checked below).
        seen.Should().Contain(["bold", "italic", "underline", "strikethrough", "link", "addComment", "insertImage", "insertTable"]);
        foreach (var command in seen)
        {
            DocumentEditorBuiltInToolbar.FindItemByCommandName(command).Should().NotBeNull($"'{command}' must not be lost from the registry");
        }
    }

    // ─── OverflowOnly lives in "More" even without measuring ──────────────────

    [Fact]
    public void OverflowOnlyCommands_AreInMore_WithoutAnyMeasurement()
    {
        var cut = RenderAdvanced();

        cut.FindAll("[data-testid='document-ribbon-panel'] button[data-command='superscript']").Should().BeEmpty(
            "an OverflowOnly command never renders on the bar");
        var more = cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more");
        more.Click();

        var menu = cut.Find("[role='menu']");
        menu.QuerySelector("[data-testid='document-superscript']").Should().NotBeNull("the More entry owns the test id of an OverflowOnly command");
        menu.QuerySelector("[data-testid='document-small-caps']").Should().NotBeNull();
        menu.QuerySelector("[data-testid='document-double-strikethrough']").Should().NotBeNull();
        var section = menu.QuerySelectorAll("[role='group']").Single(g => g.GetAttribute("aria-label") == "More formatting");
        section.QuerySelectorAll("[role='menuitemcheckbox']").Should().NotBeEmpty("a toggle keeps its check-mark state in More");
    }

    [Fact]
    public void MoreFormatting_Options_AreCheckMarkedSectionsInMore()
    {
        var cut = RenderAdvanced(p => p.Add(x => x.CurrentSpacingBefore, 12));

        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();

        var menu = cut.Find("[role='menu']");
        var before = menu.QuerySelectorAll("[role='group']").Single(g => g.GetAttribute("aria-label") == "Before");
        before.QuerySelectorAll("[role='menuitemcheckbox']").Should().HaveCount(5);
        before.QuerySelectorAll("[aria-checked='true']").Should().ContainSingle().Which.TextContent.Should().Contain("12");
    }

    [Fact]
    public async Task ChangeCaseEntry_InMore_RunsTheChangeCaseCallback()
    {
        string? applied = null;
        var cut = RenderAdvanced(p => p.Add(x => x.OnChangeCase, value => applied = value));
        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();

        var menu = cut.Find("[role='menu']");
        var entry = menu.QuerySelectorAll("[role='group']").Single(g => g.GetAttribute("aria-label") == "Change case")
            .QuerySelectorAll("button").First(b => b.TextContent.Contains("UPPERCASE", StringComparison.OrdinalIgnoreCase));
        await entry.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.WaitForAssertion(() => applied.Should().Be("uppercase"));
    }

    // ─── Measured overflow: grouped, in written order, toggles as menuitemcheckbox ────

    [Fact]
    public async Task WhenNothingFits_TheCollapsedCommands_AreGroupedByToolbarGroup_InWrittenOrder()
    {
        var cut = RenderAdvanced();
        await cut.InvokeAsync(() => cut.FindComponent<TmToolbar>().Instance.OnFitChanged(0));
        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();

        var menu = cut.Find("[role='menu']");
        var sections = menu.QuerySelectorAll("[role='group']").Select(g => g.GetAttribute("aria-label")).ToList();
        sections.Should().Contain(["Character", "Paragraph"]);

        var tests = menu.QuerySelectorAll("button")
            .Select(b => b.GetAttribute("data-testid") ?? b.GetAttribute("data-menu-testid"))
            .Where(id => id is not null)
            .ToList();
        tests.IndexOf("document-bold").Should().BeLessThan(tests.IndexOf("document-italic"));
        tests.IndexOf("document-italic").Should().BeLessThan(tests.IndexOf("document-underline"));
        tests.IndexOf("document-underline").Should().BeLessThan(tests.IndexOf("document-clear-formatting"),
            "grouping never reorders: the written order of the ribbon is the order of the menu");
    }

    [Fact]
    public async Task Toggles_InMore_AreMenuItemCheckboxes_WithAriaCheckedFromPressed()
    {
        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.BoldState, WysiwygFormattingValue.Active)
            .Add(x => x.ItalicState, WysiwygFormattingValue.Mixed)
            .Add(x => x.UnderlineState, WysiwygFormattingValue.Inactive));
        await cut.InvokeAsync(() => cut.FindComponent<TmToolbar>().Instance.OnFitChanged(0));
        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();

        var menu = cut.Find("[role='menu']");
        MenuEntry(menu, "document-bold").GetAttribute("aria-checked").Should().Be("true");
        MenuEntry(menu, "document-italic").GetAttribute("aria-checked").Should().Be("mixed");
        MenuEntry(menu, "document-underline").GetAttribute("aria-checked").Should().Be("false");
        MenuEntry(menu, "document-bold").GetAttribute("role").Should().Be("menuitemcheckbox");
    }

    [Fact]
    public async Task MoreEntry_RunsTheSameCommand_AsTheBarButton()
    {
        var bold = 0;
        var cut = Render<TmDocumentEditorToolbar>(p => p.Add(x => x.OnBold, () => { bold++; }));
        await cut.InvokeAsync(() => cut.FindComponent<TmToolbar>().Instance.OnFitChanged(0));
        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();

        MenuEntry(cut.Find("[role='menu']"), "document-bold").Click();

        cut.WaitForAssertion(() => bold.Should().Be(1));
    }

    [Fact]
    public async Task Priority_DecidesWhichCommandsLeaveFirst()
    {
        var cut = Render<TmDocumentEditorToolbar>();
        // Keep roughly the primary commands: one visible button less than the total leaves the lowest-ranked one.
        var total = cut.FindAll("[data-testid='document-ribbon-panel'] .tm-toolbar-btn").Count;
        await cut.InvokeAsync(() => cut.FindComponent<TmToolbar>().Instance.OnFitChanged(6));

        var collapsed = cut.FindAll(".tm-toolbar-item--collapsed").Select(e => e.GetAttribute("data-priority")).ToList();
        collapsed.Should().NotBeEmpty();
        total.Should().BeGreaterThan(6);
        var visiblePrimary = cut.FindAll("[data-testid='document-ribbon-panel'] .tm-toolbar-btn:not(.tm-toolbar-item--collapsed)[data-priority='primary']");
        visiblePrimary.Should().NotBeEmpty("Secondary commands leave the bar before any Primary one");
    }

    // ─── The ribbon is one icon row: labels only on the primary calls to action ───

    [Fact]
    public void LargeActions_ShowVisibleText_AllOtherButtons_AreIconOnly_WithNameAndTooltip()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        foreach (var testId in new[] { "document-link", "document-add-comment", "document-toolbar-image", "document-toolbar-table" })
        {
            var button = cut.Find($"[data-testid='{testId}']");
            button.GetAttribute("data-large").Should().Be("true", testId);
            button.QuerySelector(".tm-toolbar-btn-text").Should().NotBeNull($"{testId} is a primary call to action with a visible label");
        }

        foreach (var testId in new[] { "document-bold", "document-italic", "document-clear-formatting" })
        {
            var button = cut.Find($"[data-testid='{testId}']");
            button.QuerySelector(".tm-toolbar-btn-text").Should().BeNull($"{testId} is an icon button");
            button.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
            button.GetAttribute("title").Should().NotBeNullOrWhiteSpace("the tooltip names the icon");
        }
    }

    [Fact]
    public void CompactMode_IsIconOnly_EvenForThePrimaryActions()
    {
        var cut = Render<TmDocumentEditorToolbar>(p => p.Add(x => x.ToolbarMode, DocumentToolbarMode.Compact));

        cut.Find("[data-testid='document-link']").QuerySelector(".tm-toolbar-btn-text").Should().BeNull();
        cut.Find("[data-testid='document-link']").GetAttribute("aria-label").Should().Be("Link");
    }

    [Fact]
    public void Ribbon_SeparatesGroupsWithDividers_AndRendersNoGroupCaptions()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        cut.FindAll("[data-testid='document-ribbon-toolbar'] .tm-toolbar-divider").Count.Should().BeGreaterThanOrEqualTo(2);
        foreach (var group in cut.FindAll("[data-testid='document-ribbon-toolbar'] .tm-toolbar-group[aria-label]"))
        {
            group.TextContent.Should().NotContain(group.GetAttribute("aria-label")!,
                "a group is named for assistive technology and the More menu; it never prints a caption on the bar");
        }
    }

    [Fact]
    public void MathTab_Stays_AndRendersItsEquationCommand()
    {
        var cut = Render<TmDocumentEditorToolbar>(p => p.Add(x => x.ShowEquationTools, true));

        cut.Find("[data-testid='document-ribbon-tab-math']").Should().NotBeNull();
        OpenTab(cut, "math");
        cut.Find("[data-testid='document-ribbon-panel'] [data-command='insertEquation']").Should().NotBeNull();
    }

    // ─── Ribbon structure ─────────────────────────────────────────────────────

    [Fact]
    public void Ribbon_IsOneCoreToolbar_InsideTheTabPanel_WithTheTokenisedOverflowMenu()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        var panel = cut.Find("[data-testid='document-ribbon-panel']");
        panel.GetAttribute("role").Should().Be("tabpanel");
        var toolbar = panel.QuerySelector("[data-testid='document-ribbon-toolbar']");
        toolbar.Should().NotBeNull();
        toolbar!.GetAttribute("role").Should().Be("toolbar");
        toolbar.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
        typeof(TmDocumentEditorToolbar).Assembly.GetType("Tempo.Blazor.Components.DocumentEditor.TmDocumentToolbarOverflowMenu")
            .Should().BeNull("the ribbon has no overflow menu of its own any more");
        cut.FindAll("[data-testid='document-toolbar-more-search']").Should().BeEmpty("groups and typeahead replace the old search box");
    }

    [Fact]
    public void RibbonTabs_HaveRovingTabindex_ActiveTabIsZero()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        cut.Find("[data-testid='document-ribbon-tab-home']").GetAttribute("tabindex").Should().Be("0");
        cut.Find("[data-testid='document-ribbon-tab-insert']").GetAttribute("tabindex").Should().Be("-1");
    }

    [Fact]
    public void RibbonTabs_RovingTabindex_UpdatesWhenTabSelected()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();

        cut.Find("[data-testid='document-ribbon-tab-insert']").GetAttribute("tabindex").Should().Be("0");
        cut.Find("[data-testid='document-ribbon-tab-home']").GetAttribute("tabindex").Should().Be("-1");
    }

    [Fact]
    public void RibbonButton_KeepsTheFixedSizeClass()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        cut.Find("[data-testid='document-save']").ClassList.Should().Contain("tm-document-editor__ribbon-button");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private IRenderedComponent<TmDocumentEditorToolbar> RenderAdvanced(Action<ComponentParameterCollectionBuilder<TmDocumentEditorToolbar>>? configure = null)
        => Render<TmDocumentEditorToolbar>(parameters =>
        {
            parameters.Add(p => p.ShowAdvancedCharacterFormatting, true);
            configure?.Invoke(parameters);
        });

    private static void OpenTab(IRenderedComponent<TmDocumentEditorToolbar> cut, string tab)
        => cut.Find($"[data-testid='document-ribbon-tab-{tab}']").Click();

    private static IElement MenuEntry(IElement menu, string testId)
        => menu.QuerySelector($"[data-testid='{testId}']") ?? menu.QuerySelector($"[data-menu-testid='{testId}']")
            ?? throw new InvalidOperationException($"No More entry for {testId}");
}
