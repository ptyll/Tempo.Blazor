using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Toolbar;

/// <summary>
/// An overflow-only toolbar button never renders on the bar, so the test id its host wrote must
/// survive on the More entry. Additive: a button that stays on the bar keeps its own id too.
/// </summary>
public class TmToolbarButtonTestIdTests : LocalizationTestBase
{
    [Fact]
    public void OverflowOnly_Button_Carries_Its_TestId_On_The_More_Entry()
    {
        var cut = Render<TmToolbar>(p => p
            .Add(c => c.Overflow, ToolbarOverflow.Menu)
            .Add(c => c.AriaLabel, "Tools")
            .AddChildContent<TmToolbarButton>(b => b
                .Add(c => c.Priority, ToolbarButtonPriority.OverflowOnly)
                .Add(c => c.Text, "Print")
                .Add(c => c.OnClick, EventCallback.Factory.Create(this, () => { }))
                .AddUnmatched("data-testid", "scheduler-print")));

        cut.FindAll("button[data-testid='scheduler-print']").Should().BeEmpty(
            "an overflow-only button is omitted from the bar");

        cut.Find("button.tm-toolbar-more").Click();

        var entry = cut.Find("[role='menu'] [data-testid='scheduler-print']");
        entry.GetAttribute("role").Should().Be("menuitem");
        entry.TextContent.Should().Contain("Print");
    }

    [Fact]
    public void CollapsedSecondary_Keeps_DataTestId_On_The_Bar_And_DataMenuTestId_On_The_More_Entry()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-toolbar.js");
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();

        var cut = Render<TmToolbar>(p => p
            .Add(c => c.Overflow, ToolbarOverflow.Menu)
            .Add(c => c.AriaLabel, "Tools")
            .AddChildContent(builder =>
            {
                builder.OpenComponent<TmToolbarButton>(0);
                builder.AddAttribute(1, "Text", "Keep");
                builder.AddAttribute(2, "Priority", ToolbarButtonPriority.Primary);
                builder.CloseComponent();
                builder.OpenComponent<TmToolbarButton>(3);
                builder.AddAttribute(4, "Text", "Import");
                builder.AddAttribute(5, "Priority", ToolbarButtonPriority.Secondary);
                builder.AddAttribute(6, "data-testid", "wd-import");
                builder.CloseComponent();
            }));

        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(2));
        cut.InvokeAsync(() => cut.Instance.OnFitChanged(1));

        var bar = cut.FindAll("[data-testid='wd-import']").Should().ContainSingle().Subject;
        bar.ClassList.Should().Contain("tm-toolbar-item--collapsed");

        cut.Find("button.tm-toolbar-more").Click();

        cut.FindAll("[data-testid='wd-import']").Should().ContainSingle(
            "the collapsed bar button still owns data-testid, so the menu entry must not copy it");
        var entry = cut.Find("[role='menu'] [data-menu-testid='wd-import']");
        entry.GetAttribute("role").Should().Be("menuitem");
        entry.GetAttribute("data-testid").Should().BeNull();
        entry.TextContent.Should().Contain("Import");
    }
}
