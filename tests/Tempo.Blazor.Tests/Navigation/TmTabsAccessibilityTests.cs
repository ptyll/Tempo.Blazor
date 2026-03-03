using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Navigation;

/// <summary>Accessibility tests for TmTabs (tab button IDs, aria-labelledby on panels).</summary>
public class TmTabsAccessibilityTests : LocalizationTestBase
{
    [Fact]
    public void Tabs_TabButton_HasCorrectId()
    {
        var cut = RenderComponent<TmTabs>(p => p
            .Add(x => x.ActiveTabId, "tab1")
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "First")
                .AddChildContent("Content One"))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab2")
                .Add(x => x.Title, "Second")
                .AddChildContent("Content Two")));

        var tabs = cut.FindAll("button[role='tab']");
        tabs.Should().HaveCount(2);
        tabs[0].GetAttribute("id").Should().Be("tab-tab1");
        tabs[1].GetAttribute("id").Should().Be("tab-tab2");
    }

    [Fact]
    public void Tabs_TabPanel_AriaLabelledBy_MatchesTabId()
    {
        var cut = RenderComponent<TmTabs>(p => p
            .Add(x => x.ActiveTabId, "tab1")
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "First")
                .AddChildContent("Content One"))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab2")
                .Add(x => x.Title, "Second")
                .AddChildContent("Content Two")));

        var panel = cut.Find("div[role='tabpanel']");
        panel.GetAttribute("aria-labelledby").Should().Be("tab-tab1");
    }
}
