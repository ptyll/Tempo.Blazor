using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Layout;

/// <summary>
/// The additive palette contract the document editor's command palette is an adapter over (redesign 146c7e1b, phase 5 review):
/// disabled actions (<see cref="ICommandPaletteAction.IsDisabled"/> / <see cref="ICommandPaletteAction.DisabledReason"/>) and the
/// host attribute hooks (<c>ItemAttributes</c>, <c>InputAttributes</c>, <c>CloseButtonAttributes</c>).
/// </summary>
public class TmCommandPaletteDisabledAndAttributesTests : LocalizationTestBase
{
    private static (ICommandPaletteAction[] Actions, Func<int> Runs) Make()
    {
        var runs = 0;
        var actions = new ICommandPaletteAction[]
        {
            new CommandPaletteAction("a", "Alpha", Execute: () => { runs++; return Task.CompletedTask; }),
            new CommandPaletteAction("b", "Beta", "A description", IsDisabled: true, DisabledReason: "Needs a selection", Execute: () => { runs += 10; return Task.CompletedTask; }),
            new CommandPaletteAction("c", "Gamma", Execute: () => { runs += 100; return Task.CompletedTask; }),
        };
        return (actions, () => runs);
    }

    [Fact]
    public void DisabledAction_IsListed_AriaDisabled_AndShowsItsReasonInsteadOfTheDescription()
    {
        var (actions, _) = Make();
        var cut = Render<TmCommandPalette>(p => p.Add(c => c.IsOpen, true).Add(c => c.Actions, actions));

        var options = cut.FindAll("[role=option]");
        options.Should().HaveCount(3);
        options[0].HasAttribute("aria-disabled").Should().BeFalse();
        options[1].GetAttribute("aria-disabled").Should().Be("true");
        options[1].QuerySelector(".tm-command-palette-disabled-reason")!.TextContent.Should().Be("Needs a selection");
        options[1].TextContent.Should().NotContain("A description");
        options[1].ClassList.Should().Contain("tm-command-palette-item-disabled");
    }

    [Fact]
    public async Task DisabledAction_DoesNotRun_OnClickOrEnter_AndThePaletteStaysOpen()
    {
        var (actions, runs) = Make();
        var closed = false;
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, actions)
            .Add(c => c.IsOpenChanged, open => { if (!open) { closed = true; } }));

        cut.FindAll("[role=option]")[1].Click();
        runs().Should().Be(0);
        closed.Should().BeFalse();

        var input = cut.Find("input");
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.FindAll("[role=option]")[1].GetAttribute("aria-selected").Should().Be("true", "arrows reach a disabled option");
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await Task.Delay(30);
        runs().Should().Be(0);
        closed.Should().BeFalse();

        cut.FindAll("[role=option]")[2].Click();
        runs().Should().Be(100);
    }

    [Fact]
    public void ItemAttributes_AreSplattedOnTheOption_AndTheOwnRoleWins()
    {
        var (actions, _) = Make();
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, actions)
            .Add(c => c.ItemAttributes, action => new Dictionary<string, object>
            {
                ["data-testid"] = "item",
                ["data-command"] = action.Id,
                ["role"] = "button",
            }));

        var first = cut.FindAll("[role=option]")[0];
        first.GetAttribute("data-testid").Should().Be("item");
        first.GetAttribute("data-command").Should().Be("a");
        cut.FindAll("[role=button]").Should().BeEmpty();
    }

    [Fact]
    public void InputAndCloseButtonAttributes_ReachTheirElements()
    {
        var (actions, _) = Make();
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, actions)
            .Add(c => c.InputAttributes, new Dictionary<string, object> { ["data-testid"] = "search" })
            .Add(c => c.CloseButtonAttributes, new Dictionary<string, object> { ["data-testid"] = "close" }));

        cut.Find("input.tm-command-palette-input").GetAttribute("data-testid").Should().Be("search");
        cut.Find("button.tm-command-palette-close").GetAttribute("data-testid").Should().Be("close");
    }

    [Fact]
    public void SearchTerms_AreMatchedByTheLocalFilter_ButNeverDisplayed()
    {
        var actions = new ICommandPaletteAction[]
        {
            new CommandPaletteAction("a", "Alpha", SearchTerms: ["review-group"]),
            new CommandPaletteAction("b", "Beta"),
        };
        var cut = Render<TmCommandPalette>(p => p.Add(c => c.IsOpen, true).Add(c => c.Actions, actions));

        cut.Find("input").Input("review");

        var options = cut.FindAll("[role=option]");
        options.Should().ContainSingle();
        options[0].TextContent.Should().NotContain("review-group");
    }

    [Fact]
    public void NarrowPanelContainerQuery_HidesTheDescription_ButNeverTheDisabledReason()
    {
        // A phone-width palette hides the (decorative) description; the reason a row does nothing is information and must stay.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        var css = File.ReadAllText(Path.Combine(directory!.FullName, "src", "Tempo.Blazor", "wwwroot", "css", "components", "_command-palette.css"));
        var start = css.IndexOf("@container tm-command-palette (width < 30rem)", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var block = css[start..css.IndexOf("/* Trailing slot", start, StringComparison.Ordinal)];

        block.Should().Contain(".tm-command-palette-description:not(.tm-command-palette-disabled-reason)");
        block.Should().NotMatchRegex(@"\.tm-command-palette-description\s*\{[^}]*display:\s*none", "the unqualified description rule would hide the reason too");
        var reason = block[block.IndexOf(".tm-command-palette-disabled-reason {", StringComparison.Ordinal)..];
        reason = reason[..reason.IndexOf('}')];
        reason.Should().Contain("white-space: normal").And.NotContain("display: none");
    }
}
