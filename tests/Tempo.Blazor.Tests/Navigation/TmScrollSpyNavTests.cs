using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Navigation;

/// <summary>TDD tests for TmScrollSpyNav.</summary>
public class TmScrollSpyNavTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor/Components/Navigation/TmScrollSpyNav.razor.js";

    private static readonly ScrollSpyNavItem[] Items =
    [
        new("intro", "Introduction"),
        new("details", "Details", Order: 1),
        new("hidden-section", "Hidden", IsVisible: false, Order: 2),
        new("summary", "Summary", Order: 3),
    ];

    [Fact]
    public void ScrollSpyNav_DefaultsActiveToFirstVisibleItem_WithAriaCurrent()
    {
        var cut = Render<TmScrollSpyNav>(p => p.Add(x => x.Items, Items));

        var introButton = cut.Find("[data-testid='tm-scroll-spy-nav-intro']");
        introButton.GetAttribute("aria-current").Should().Be("true");
        introButton.GetAttribute("data-active").Should().Be("true");

        var detailsButton = cut.Find("[data-testid='tm-scroll-spy-nav-details']");
        detailsButton.GetAttribute("aria-current").Should().Be("false");
        detailsButton.GetAttribute("data-active").Should().Be("false");
    }

    [Fact]
    public void ScrollSpyNav_HiddenItems_AreExcludedFromRender()
    {
        var cut = Render<TmScrollSpyNav>(p => p.Add(x => x.Items, Items));

        cut.FindAll("[data-testid='tm-scroll-spy-nav-hidden-section']").Should().BeEmpty();
    }

    [Fact]
    public void ScrollSpyNav_RendersItemsInOrder()
    {
        var cut = Render<TmScrollSpyNav>(p => p.Add(x => x.Items, Items));

        var labels = cut.FindAll(".tm-scroll-spy-nav__label").Select(e => e.TextContent).ToArray();
        labels.Should().Equal("Introduction", "Details", "Summary");
    }

    [Fact]
    public void ScrollSpyNav_ActiveIdParameter_DrivesActiveItem()
    {
        var cut = Render<TmScrollSpyNav>(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.ActiveId, "summary"));

        cut.Find("[data-testid='tm-scroll-spy-nav-summary']").GetAttribute("aria-current").Should().Be("true");
        cut.Find("[data-testid='tm-scroll-spy-nav-intro']").GetAttribute("aria-current").Should().Be("false");
    }

    [Fact]
    public async Task ScrollSpyNav_Click_UpdatesActiveIdAndFiresCallbacks()
    {
        string? changedTo = null;
        string? navigatedTo = null;
        var cut = Render<TmScrollSpyNav>(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.ActiveIdChanged, id => changedTo = id)
            .Add(x => x.OnNavigate, id => navigatedTo = id));

        await cut.Find("[data-testid='tm-scroll-spy-nav-details']").ClickAsync(new());

        changedTo.Should().Be("details");
        navigatedTo.Should().Be("details");
        cut.Find("[data-testid='tm-scroll-spy-nav-details']").GetAttribute("aria-current").Should().Be("true");
        cut.Find("[data-testid='tm-scroll-spy-nav-intro']").GetAttribute("aria-current").Should().Be("false");
    }

    [Fact]
    public async Task ScrollSpyNav_Click_AlwaysSmoothScrollsRegardlessOfEnableScrollSpy()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("scrollTo", _ => true).SetVoidResult();

        var cut = Render<TmScrollSpyNav>(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.EnableScrollSpy, false));

        await cut.Find("[data-testid='tm-scroll-spy-nav-details']").ClickAsync(new());

        module.Invocations.Should().Contain(invocation =>
            invocation.Identifier == "scrollTo" && invocation.Arguments.Count == 1 && (string)invocation.Arguments[0]! == "details");
    }

    [Fact]
    public void ScrollSpyNav_EnableScrollSpyTrue_RegistersObserver()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("observe", _ => true).SetVoidResult();

        var cut = Render<TmScrollSpyNav>(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.EnableScrollSpy, true)
            .Add(x => x.ScrollOffset, 200));

        module.Invocations.Should().Contain(invocation => invocation.Identifier == "observe");
    }

    [Fact]
    public void ScrollSpyNav_EnableScrollSpyFalse_DoesNotRegisterObserver()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("observe", _ => true).SetVoidResult();

        var cut = Render<TmScrollSpyNav>(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.EnableScrollSpy, false));

        module.Invocations.Should().NotContain(invocation => invocation.Identifier == "observe");
    }

    [Fact]
    public async Task ScrollSpyNav_SetActiveFromScroll_UpdatesActiveItemAndFiresChanged()
    {
        string? changedTo = null;
        var cut = Render<TmScrollSpyNav>(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.EnableScrollSpy, true)
            .Add(x => x.ActiveIdChanged, id => changedTo = id));

        await cut.InvokeAsync(() => cut.Instance.SetActiveFromScroll("summary"));

        changedTo.Should().Be("summary");
        cut.Find("[data-testid='tm-scroll-spy-nav-summary']").GetAttribute("aria-current").Should().Be("true");
    }

    [Fact]
    public void ScrollSpyNav_ItemTemplate_OverridesDefaultLabelRendering()
    {
        var cut = Render<TmScrollSpyNav>(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.ItemTemplate, item => builder =>
            {
                builder.OpenElement(0, "strong");
                builder.AddAttribute(1, "class", "custom-item");
                builder.AddContent(2, $"** {item.Label} **");
                builder.CloseElement();
            }));

        cut.Find("[data-testid='tm-scroll-spy-nav-intro'] .custom-item").TextContent.Should().Be("** Introduction **");
        cut.FindAll(".tm-scroll-spy-nav__label").Should().BeEmpty();
    }

    [Fact]
    public void ScrollSpyNav_UsesNativeButtonElements_ForKeyboardFocusability()
    {
        var cut = Render<TmScrollSpyNav>(p => p.Add(x => x.Items, Items));

        var button = cut.Find("[data-testid='tm-scroll-spy-nav-intro']");
        button.TagName.Should().Be("BUTTON");
        button.GetAttribute("type").Should().Be("button");
    }

    [Fact]
    public void ScrollSpyNav_Title_RendersOnlyInSideRailVariant()
    {
        var sideRail = Render<TmScrollSpyNav>(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.Title, "On this page")
            .Add(x => x.Variant, ScrollSpyNavVariant.SideRail));
        sideRail.Find(".tm-scroll-spy-nav__title").TextContent.Should().Be("On this page");

        var breadcrumb = Render<TmScrollSpyNav>(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.Title, "On this page")
            .Add(x => x.Variant, ScrollSpyNavVariant.Breadcrumb));
        breadcrumb.FindAll(".tm-scroll-spy-nav__title").Should().BeEmpty();
    }

    /// <summary>
    /// A shell that scrolls its own content column raises no scroll event on the window, so the released
    /// listener never fires. The selector for that column has to reach the JS side.
    /// </summary>
    [Fact]
    public void ScrollSpyNav_ScrollContainerSelector_IsHandedToTheObserver()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("observe", _ => true).SetVoidResult();

        Render<TmScrollSpyNav>(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.EnableScrollSpy, true)
            .Add(x => x.ScrollOffset, 200)
            .Add(x => x.ScrollContainerSelector, "[data-testid='main-content']"));

        var observe = module.Invocations.Single(invocation => invocation.Identifier == "observe");
        observe.Arguments.Should().HaveCount(4);
        observe.Arguments[2].Should().Be(200);
        observe.Arguments[3].Should().Be("[data-testid='main-content']");
    }

    [Fact]
    public void ScrollSpyNav_NoScrollContainerSelector_ObservesWithNullRoot()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("observe", _ => true).SetVoidResult();

        Render<TmScrollSpyNav>(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.EnableScrollSpy, true));

        var observe = module.Invocations.Single(invocation => invocation.Identifier == "observe");
        observe.Arguments.Should().HaveCount(4);
        observe.Arguments[3].Should().BeNull();
    }

    /// <summary>
    /// The re-observe guard used to look only at the section ids, so a shell that resolves its scrolling
    /// column late would keep a listener on the old root forever.
    /// </summary>
    [Fact]
    public void ScrollSpyNav_ScrollContainerSelectorChange_ReObserves()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("observe", _ => true).SetVoidResult();

        var cut = Render<TmScrollSpyNav>(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.EnableScrollSpy, true)
            .Add(x => x.ScrollContainerSelector, "#first"));

        cut.Render(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.EnableScrollSpy, true)
            .Add(x => x.ScrollContainerSelector, "#second"));

        var roots = module.Invocations
            .Where(invocation => invocation.Identifier == "observe")
            .Select(invocation => invocation.Arguments[3])
            .ToArray();
        roots.Should().Equal("#first", "#second");
    }

    [Fact]
    public void ScrollSpyNav_Variant_Breadcrumb_AppliesBreadcrumbClass()
    {
        var cut = Render<TmScrollSpyNav>(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.Variant, ScrollSpyNavVariant.Breadcrumb));

        cut.Find(".tm-scroll-spy-nav").ClassList.Should().Contain("tm-scroll-spy-nav--breadcrumb");
    }

    /// <summary>
    /// The scoped CSS fixes the measured `.tm-scroll-spy-nav__link|--active` cascade tie with the
    /// compound <c>.tm-scroll-spy-nav__link.tm-scroll-spy-nav__link--active</c> — a selector that
    /// only reaches an element carrying BOTH classes. This pins the markup contract the compound
    /// relies on, in both variants (Fáze 18.1).
    /// </summary>
    [Theory]
    [InlineData(ScrollSpyNavVariant.SideRail)]
    [InlineData(ScrollSpyNavVariant.Breadcrumb)]
    public void ScrollSpyNav_ActiveLink_CarriesBaseAndModifier_OnTheSameElement(ScrollSpyNavVariant variant)
    {
        var cut = Render<TmScrollSpyNav>(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.Variant, variant)
            .Add(x => x.ActiveId, "details"));

        var active = cut.Find("[data-testid='tm-scroll-spy-nav-details']");
        active.ClassList.Should().Contain("tm-scroll-spy-nav__link")
            .And.Contain("tm-scroll-spy-nav__link--active");

        var inactive = cut.Find("[data-testid='tm-scroll-spy-nav-intro']");
        inactive.ClassList.Should().Contain("tm-scroll-spy-nav__link")
            .And.NotContain("tm-scroll-spy-nav__link--active");
    }

    /// <summary>
    /// The scoped selectors the active state wins against are variant-scoped —
    /// <c>.tm-scroll-spy-nav--siderail …</c> / <c>.tm-scroll-spy-nav--breadcrumb …</c> — so the nav
    /// root must always carry the variant class, for every variant the enum offers.
    /// </summary>
    [Fact]
    public void ScrollSpyNav_Root_AlwaysCarriesAVariantClass()
    {
        foreach (var variant in Enum.GetValues<ScrollSpyNavVariant>())
        {
            var cut = Render<TmScrollSpyNav>(p => p
                .Add(x => x.Items, Items)
                .Add(x => x.Variant, variant));

            var classes = cut.Find(".tm-scroll-spy-nav").ClassList;
            classes.Should().Contain(c => c.StartsWith("tm-scroll-spy-nav--"),
                "scoped aktivní pravidla se opírají o variantní třídu kořene — bez ní nechytí");
        }
    }
}
