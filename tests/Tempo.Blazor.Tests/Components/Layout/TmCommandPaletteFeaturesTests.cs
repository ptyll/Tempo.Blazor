using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Layout;

/// <summary>
/// TG-010 feature tests for TmCommandPalette: item icons (ShowIcons, default TRUE per decision
/// DEC-CMDPALETTE-ASYNC O2), Badge/Code trailing slot (O4), ItemTrailingTemplate/ItemTemplate,
/// local filter matching Code, HighlightMatches and the keyboard-hint footer.
/// </summary>
public class TmCommandPaletteFeaturesTests : LocalizationTestBase
{
    private static ICommandPaletteAction[] MakeActions() =>
    [
        new CommandPaletteAction("a1", "New File", "Create a new file", Icon: "file", Shortcut: "Ctrl+N"),
        new CommandPaletteAction("a2", "Open Project", null, Icon: null),
        new CommandPaletteAction("a3", "New Folder", "Create a new folder", Icon: "folder",
            Badge: "File", Code: "UC-042"),
    ];

    // ── Default markup snapshot ──────────────────────────────────────────────

    [Fact]
    public void TmCommandPalette_Default_Markup_Snapshot()
    {
        // Acceptance "bez nových parametrů identický markup": this pins the CURRENT default markup of
        // the component-owned chrome (flat list, one shortcut kbd per item, an icon slot per item
        // because ShowIcons defaults to true, a trailing slot per item, no footer, no skeleton, no
        // error row, no live region). Blazor event-handler values and the icon svg geometry are
        // normalized away — the structure is what is pinned.
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        var markup = Normalize(cut.Find(".tm-command-palette").InnerHtml);
        markup.Should().Be(
            """
            <div class="tm-command-palette-header"><input class="tm-command-palette-input" value="" blazor:oninput="EVENT" blazor:onkeydown="EVENT" placeholder="Type a command or search..." aria-label="Search commands" role="combobox" aria-expanded="true" aria-controls="LIST_ID" aria-autocomplete="list" aria-activedescendant="LIST_ID-opt-0" autocomplete="off">
                            <button class="tm-command-palette-close" aria-label="Close" blazor:onclick="EVENT">×</button></div><ul class="tm-command-palette-list" role="listbox" id="LIST_ID" aria-label="Command palette"><li class="tm-command-palette-item tm-command-palette-item-focused" id="LIST_ID-opt-0" role="option" aria-selected="true" blazor:onclick="EVENT"><span class="tm-command-palette-icon" aria-hidden="true">SVG</span><span class="tm-command-palette-title">New File</span><span class="tm-command-palette-description">Create a new file</span><span class="tm-command-palette-trailing"></span><kbd class="tm-command-palette-shortcut">Ctrl+N</kbd></li><li class="tm-command-palette-item" id="LIST_ID-opt-1" role="option" aria-selected="false" blazor:onclick="EVENT"><span class="tm-command-palette-icon" aria-hidden="true"></span><span class="tm-command-palette-title">Open Project</span><span class="tm-command-palette-trailing"></span></li><li class="tm-command-palette-item" id="LIST_ID-opt-2" role="option" aria-selected="false" blazor:onclick="EVENT"><span class="tm-command-palette-icon" aria-hidden="true">SVG</span><span class="tm-command-palette-title">New Folder</span><span class="tm-command-palette-description">Create a new folder</span><span class="tm-command-palette-trailing"><span class="tm-badge tm-badge-default tm-badge-sm tm-badge-subtle">File</span><code class="tm-command-palette-code">UC-042</code></span></li></ul><div class="tm-sr-only tm-command-palette-status" role="status" aria-live="polite" aria-atomic="true"></div>
            """);
    }

    private static string Normalize(string markup)
    {
        // Stable per-instance ids (list id) vary per render; blazor event ids and the icon svg
        // geometry are renderer noise — the pinned contract is the structure.
        markup = System.Text.RegularExpressions.Regex.Replace(markup, "tm-palette-list-[0-9a-f]{32}", "LIST_ID");
        markup = System.Text.RegularExpressions.Regex.Replace(markup, "blazor:([a-z-]+)=\"\\d+\"", "blazor:$1=\"EVENT\"");
        markup = System.Text.RegularExpressions.Regex.Replace(markup, " blazor:elementreference(=\"[^\"]*\")?", string.Empty);
        markup = System.Text.RegularExpressions.Regex.Replace(markup, "<svg.*?</svg>", "SVG", System.Text.RegularExpressions.RegexOptions.Singleline);
        return markup;
    }

    // ── Icons (ShowIcons, default true — visual breaking change DEC-CMDPALETTE-ASYNC O2) ──

    [Fact]
    public void TmCommandPalette_ShowIcons_Default_True_Renders_Icon_Slot_Per_Item()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        var items = cut.FindAll(".tm-command-palette-item");
        // Every item carries the icon slot so titles align; an item without an icon gets an empty slot.
        items.Should().AllSatisfy(i => i.QuerySelector(".tm-command-palette-icon").Should().NotBeNull());
        items[0].QuerySelector(".tm-command-palette-icon svg").Should().NotBeNull("a1 has an icon");
        items[1].QuerySelector(".tm-command-palette-icon svg").Should().BeNull("a2 has no icon — the slot stays empty");
        items[0].QuerySelector(".tm-command-palette-icon")!.GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void TmCommandPalette_ShowIcons_False_Renders_No_Icon_Slot()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.ShowIcons, false)
            .Add(c => c.Actions, MakeActions()));

        cut.FindAll(".tm-command-palette-icon").Should().BeEmpty();
    }

    // ── Badge / Code trailing slot (O4) ──────────────────────────────────────

    [Fact]
    public void TmCommandPalette_Badge_And_Code_Render_In_Trailing_Slot()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, new[]
            {
                new CommandPaletteAction("a1", "Run", null, Badge: "UC", Code: "UC-042"),
            }));

        var trailing = cut.Find(".tm-command-palette-trailing");
        trailing.QuerySelector(".tm-badge").Should().NotBeNull();
        trailing.QuerySelector(".tm-badge")!.TextContent.Trim().Should().Be("UC");
        trailing.QuerySelector("code.tm-command-palette-code").Should().NotBeNull();
        trailing.QuerySelector("code.tm-command-palette-code")!.TextContent.Trim().Should().Be("UC-042");
    }

    [Theory]
    [InlineData(CommandPaletteBadgeTone.Neutral, "tm-badge-default")]
    [InlineData(CommandPaletteBadgeTone.Primary, "tm-badge-primary")]
    [InlineData(CommandPaletteBadgeTone.Success, "tm-badge-success")]
    [InlineData(CommandPaletteBadgeTone.Warning, "tm-badge-warning")]
    [InlineData(CommandPaletteBadgeTone.Danger, "tm-badge-danger")]
    [InlineData(CommandPaletteBadgeTone.Info, "tm-badge-info")]
    public void TmCommandPalette_BadgeTone_Maps_To_Badge_Variant(CommandPaletteBadgeTone tone, string expectedClass)
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, new[]
            {
                new CommandPaletteAction("a1", "Run", null, Badge: "X", BadgeTone: tone),
            }));

        cut.Find(".tm-command-palette-trailing .tm-badge").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void TmCommandPalette_Accessible_Name_Includes_Title_Description_Badge_And_Code()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, new[]
            {
                new CommandPaletteAction("a1", "Run report", "Quarterly figures", Badge: "Report", Code: "RPT-7"),
            }));

        var name = cut.Find(".tm-command-palette-item").TextContent;
        name.Should().Contain("Run report");
        name.Should().Contain("Quarterly figures");
        name.Should().Contain("Report");
        name.Should().Contain("RPT-7");
    }

    [Fact]
    public void TmCommandPalette_ItemTrailingTemplate_Overrides_Default_Trailing()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, new[]
            {
                new CommandPaletteAction("a1", "Run", null, Badge: "UC", Code: "UC-042"),
            })
            .Add(c => c.ItemTrailingTemplate, action => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddContent(1, $"custom:{action.Id}");
                builder.CloseElement();
            }));

        cut.Find(".tm-command-palette-trailing").TextContent.Trim().Should().Be("custom:a1");
        cut.FindAll(".tm-command-palette-trailing .tm-badge").Should().BeEmpty();
    }

    [Fact]
    public void TmCommandPalette_ItemTemplate_Overrides_Content_But_Keeps_Option_Wiring()
    {
        var executed = false;
        var action = new CommandPaletteAction("a1", "Run", null, Execute: () => { executed = true; return Task.CompletedTask; });
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, new[] { action })
            .Add(c => c.ItemTemplate, a => builder =>
            {
                builder.OpenElement(0, "em");
                builder.AddContent(1, $"item:{a.Id}");
                builder.CloseElement();
            }));

        var option = cut.Find("[role='option']");
        option.GetAttribute("id").Should().NotBeNullOrEmpty();
        option.GetAttribute("aria-selected").Should().Be("true");
        option.QuerySelector("em")!.TextContent.Trim().Should().Be("item:a1");
        // The click wiring stays the component's: executing still runs the action and closes.
        option.Click();
        executed.Should().BeTrue();
    }

    // ── Local filter matches Code ────────────────────────────────────────────

    [Fact]
    public void TmCommandPalette_Local_Filter_Matches_Code()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, new[]
            {
                new CommandPaletteAction("a1", "Run report", null, Code: "UC-042"),
                new CommandPaletteAction("a2", "Other", null),
            }));

        cut.Find(".tm-command-palette-input").Input("uc-04");

        cut.FindAll(".tm-command-palette-item").Should().ContainSingle();
        cut.Find(".tm-command-palette-title").TextContent.Trim().Should().Be("Run report");
    }

    // ── HighlightMatches ─────────────────────────────────────────────────────

    [Fact]
    public void TmCommandPalette_HighlightMatches_Marks_Query_In_Title_And_Code()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.HighlightMatches, true)
            .Add(c => c.Actions, new[]
            {
                new CommandPaletteAction("a1", "RPT runner", null, Code: "RPT-7"),
            }));

        cut.Find(".tm-command-palette-input").Input("rpt");

        cut.FindAll(".tm-command-palette-title mark").Should().ContainSingle()
            .Which.TextContent.Should().Be("RPT");
        cut.Find(".tm-command-palette-code mark").TextContent.Should().Be("RPT",
            "the query is marked in the code too");
    }

    [Fact]
    public void TmCommandPalette_HighlightMatches_Off_Renders_Plain_Text()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.HighlightMatches, false)
            .Add(c => c.Actions, new[]
            {
                new CommandPaletteAction("a1", "Run report", null, Code: "RPT-7"),
            }));

        cut.Find(".tm-command-palette-input").Input("rep");

        cut.FindAll(".tm-command-palette-item mark").Should().BeEmpty();
    }

    // ── Footer with keyboard hints ───────────────────────────────────────────

    [Fact]
    public void TmCommandPalette_ShowFooter_Renders_Localized_Kbd_Hints()
    {
        var loc = Services.GetRequiredService<ITmLocalizer>();
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.ShowFooter, true)
            .Add(c => c.Actions, MakeActions()));

        var footer = cut.Find(".tm-command-palette-footer");
        footer.GetAttribute("role").Should().BeNull("the footer is not a live region");
        var kbds = footer.QuerySelectorAll("kbd").Select(k => k.TextContent.Trim()).ToList();
        kbds.Should().Contain(["↑", "↓", "↵", "Esc"]);
        footer.TextContent.Should().Contain(loc["TmCommandPalette_FooterNavigate"]);
        footer.TextContent.Should().Contain(loc["TmCommandPalette_FooterSelect"]);
        footer.TextContent.Should().Contain(loc["TmCommandPalette_FooterClose"]);
    }

    [Fact]
    public void TmCommandPalette_FooterTemplate_Renders_Without_ShowFooter()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions())
            .Add(c => c.FooterTemplate, builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddContent(1, "custom-footer");
                builder.CloseElement();
            }));

        cut.Find(".tm-command-palette-footer").TextContent.Trim().Should().Be("custom-footer");
    }

    [Fact]
    public void TmCommandPalette_Footer_Hidden_By_Default()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        cut.FindAll(".tm-command-palette-footer").Should().BeEmpty();
    }

    // ── Abstractions: model additions (O4/O5) ────────────────────────────────

    private sealed class LegacyAction : ICommandPaletteAction
    {
        public string Id => "legacy";
        public string Title => "Legacy";
        public string? Description => null;
        public string? Icon => null;
        public string? Shortcut => null;
        public string? Category => null;
        public Func<Task> Execute => () => Task.CompletedTask;
    }

    [Fact]
    public void ICommandPaletteAction_Legacy_Implementations_Get_Null_Badge_And_Code_Via_Defaults()
    {
        // Default interface members: a pre-2.11 implementation compiles unchanged and sees the
        // neutral defaults (source- and binary-compatible extension of the contract).
        ICommandPaletteAction action = new LegacyAction();
        action.Badge.Should().BeNull();
        action.Code.Should().BeNull();
        action.BadgeTone.Should().Be(CommandPaletteBadgeTone.Neutral);
    }

    [Fact]
    public void CommandPaletteAction_Record_Implements_The_Interface()
    {
        var executed = false;
        var action = new CommandPaletteAction("id", "Title", "Desc", Icon: "file", Shortcut: "Ctrl+N",
            Category: "File", Badge: "UC", Code: "UC-1", BadgeTone: CommandPaletteBadgeTone.Info,
            Execute: () => { executed = true; return Task.CompletedTask; });

        action.Id.Should().Be("id");
        action.Title.Should().Be("Title");
        action.Description.Should().Be("Desc");
        action.Icon.Should().Be("file");
        action.Shortcut.Should().Be("Ctrl+N");
        action.Category.Should().Be("File");
        action.Badge.Should().Be("UC");
        action.Code.Should().Be("UC-1");
        action.BadgeTone.Should().Be(CommandPaletteBadgeTone.Info);
        action.Execute();
        executed.Should().BeTrue();
    }

    [Fact]
    public void CommandPaletteAction_Execute_Defaults_To_NoOp()
    {
        var action = new CommandPaletteAction("id", "Title");
        ((ICommandPaletteAction)action).Execute.Should().NotBeNull();
        ((ICommandPaletteAction)action).Execute().IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void CommandPaletteSearchResult_Factories_Expose_State()
    {
        var item = new CommandPaletteAction("a", "A");
        CommandPaletteSearchResult.Found([item]).State.Should().Be(CommandPaletteFetchState.Ok);
        CommandPaletteSearchResult.Found([item]).Items.Should().ContainSingle();
        CommandPaletteSearchResult.NoResults.State.Should().Be(CommandPaletteFetchState.Empty);
        CommandPaletteSearchResult.NoResults.Items.Should().BeEmpty();
        CommandPaletteSearchResult.Failed.State.Should().Be(CommandPaletteFetchState.Transient);
        CommandPaletteSearchResult.Failed.Items.Should().BeEmpty();
    }
}
