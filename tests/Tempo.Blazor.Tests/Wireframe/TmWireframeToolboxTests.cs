using Bunit;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// bUnit tests for TmWireframeToolbox.
/// Registers a populated WireframeComponentRegistry so the toolbox has components to display.
/// </summary>
public class TmWireframeToolboxTests : LocalizationTestBase
{
    private static WireframeComponentDef MakeDef(string type, string category,
        bool isBuiltIn = true, string? displayName = null)
        => new()
        {
            Type        = type,
            DisplayName = displayName ?? type,
            Category    = category,
            DefaultWidth  = 120,
            DefaultHeight = 36,
            IsBuiltIn   = isBuiltIn,
            Props       = [],
            RenderSvg   = (_, _) => { }
        };

    private WireframeComponentRegistry BuildRegistry(params WireframeComponentDef[] defs)
    {
        var registry = new WireframeComponentRegistry();
        foreach (var d in defs)
            registry.RegisterDefinition(d);
        return registry;
    }

    // ── Container renders ──────────────────────────────────────────────────────

    [Fact]
    public void Toolbox_RendersContainer()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons"));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();

        cut.Find(".tm-wd-toolbox").Should().NotBeNull();
    }

    [Fact]
    public void Toolbox_RendersSearchInput()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons"));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();

        cut.Find("input[type=\"search\"]").Should().NotBeNull();
    }

    // ── Items render ───────────────────────────────────────────────────────────

    [Fact]
    public void Toolbox_RendersItemForEachComponent()
    {
        var registry = BuildRegistry(
            MakeDef("TmButton",    "Buttons"),
            MakeDef("TmTextInput", "Inputs"),
            MakeDef("TmSelect",    "Inputs"));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();

        cut.FindAll(".tm-wd-toolbox__item").Should().HaveCount(3);
    }

    [Fact]
    public void Toolbox_ItemHasDraggableAttribute()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons"));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();
        var item = cut.Find(".tm-wd-toolbox__item");

        item.GetAttribute("draggable").Should().Be("true");
    }

    [Fact]
    public void Toolbox_ItemHasDataComponentTypeAttribute()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons"));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();
        var item = cut.Find(".tm-wd-toolbox__item");

        item.GetAttribute("data-component-type").Should().Be("TmButton");
    }

    [Fact]
    public void Toolbox_ItemDisplaysName()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons", displayName: "Button"));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();

        cut.Find(".tm-wd-toolbox__item-name").TextContent.Should().Be("Button");
    }

    [Fact]
    public void Toolbox_RendersCategory()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons"));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();

        cut.Find(".tm-wd-toolbox__category-name").TextContent.Should().Be("Buttons");
    }

    // ── Search filter ──────────────────────────────────────────────────────────

    [Fact]
    public void Toolbox_SearchFilter_HidesNonMatchingItems()
    {
        var registry = BuildRegistry(
            MakeDef("TmButton",    "Buttons", displayName: "Button"),
            MakeDef("TmTextInput", "Inputs",  displayName: "Text Input"));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();

        cut.Find("input[type=\"search\"]").Input("Button");

        cut.FindAll(".tm-wd-toolbox__item").Should().HaveCount(1);
        cut.Find(".tm-wd-toolbox__item-name").TextContent.Should().Contain("Button");
    }

    [Fact]
    public void Toolbox_SearchFilter_EmptySearch_ShowsAll()
    {
        var registry = BuildRegistry(
            MakeDef("TmButton",    "Buttons"),
            MakeDef("TmTextInput", "Inputs"));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();

        cut.Find("input[type=\"search\"]").Input("X");
        cut.Find("input[type=\"search\"]").Input("");

        cut.FindAll(".tm-wd-toolbox__item").Should().HaveCount(2);
    }

    [Fact]
    public void Toolbox_SearchFilter_NoMatches_ShowsEmptyState()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons"));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();

        cut.Find("input[type=\"search\"]").Input("xyzzy_no_match");

        cut.Find(".tm-wd-toolbox__empty").Should().NotBeNull();
    }

    [Fact]
    public void Toolbox_SearchFilter_MatchesOnCategory()
    {
        var registry = BuildRegistry(
            MakeDef("TmButton", "Buttons"),
            MakeDef("TmCard",   "Layout"));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();

        cut.Find("input[type=\"search\"]").Input("Layout");

        cut.FindAll(".tm-wd-toolbox__item").Should().HaveCount(1);
        cut.Find(".tm-wd-toolbox__item").GetAttribute("data-component-type").Should().Be("TmCard");
    }

    // ── Filter tabs (only shown when custom components exist) ──────────────────

    [Fact]
    public void Toolbox_FilterTabs_NotShownWithOnlyBuiltIns()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons", isBuiltIn: true));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();

        cut.FindAll(".tm-wd-toolbox__filters").Should().BeEmpty();
    }

    [Fact]
    public void Toolbox_FilterTabs_ShownWhenCustomExists()
    {
        var registry = BuildRegistry(
            MakeDef("TmButton",   "Buttons",  isBuiltIn: true),
            MakeDef("HeroSection","Custom",   isBuiltIn: false));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();

        cut.Find(".tm-wd-toolbox__filters").Should().NotBeNull();
    }

    [Fact]
    public void Toolbox_CustomComponent_ShowsBadge()
    {
        var registry = BuildRegistry(
            MakeDef("HeroSection", "Custom", isBuiltIn: false));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();

        cut.Find(".tm-wd-toolbox__item-badge--custom").Should().NotBeNull();
    }

    [Fact]
    public void Toolbox_BuiltInComponent_NoBadge()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons", isBuiltIn: true));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();

        cut.FindAll(".tm-wd-toolbox__item-badge--custom").Should().BeEmpty();
    }

    [Fact]
    public void Toolbox_ComponentScope_RendersBuiltInsAndMatchingScopedCustomsOnly()
    {
        var appA = Guid.NewGuid().ToString("D");
        var appB = Guid.NewGuid().ToString("D");
        var registry = new WireframeComponentRegistry();
        registry.RegisterDefinition(MakeDef("TmButton", "Buttons", isBuiltIn: true, displayName: "Button"));
        registry.RegisterDefinition(MakeDef("LegacyCustom", "Custom", isBuiltIn: false, displayName: "Legacy Custom"));
        registry.RegisterDefinition(MakeDef("InvoiceCard", "Custom", isBuiltIn: false, displayName: "A Invoice"), scopeAppId: appA);
        registry.RegisterDefinition(MakeDef("InvoiceCard", "Custom", isBuiltIn: false, displayName: "B Invoice"), scopeAppId: appB);
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>(p => p
            .Add(x => x.ComponentScope, WireframeComponentScope.ForApp(appA)));

        var types = cut.FindAll(".tm-wd-toolbox__item")
            .Select(item => item.GetAttribute("data-component-type"))
            .ToList();

        types.Should().BeEquivalentTo(["TmButton", $"app:{appA}:InvoiceCard"]);
        types.Should().NotContain("LegacyCustom");
        types.Should().NotContain($"app:{appB}:InvoiceCard");
    }

    // ── Keyboard activation ────────────────────────────────────────────────────

    [Fact]
    public void Toolbox_EnterKeyOnItem_FiresOnComponentActivated()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons"));
        Services.AddSingleton(registry);

        string? activated = null;
        var cut = Render<TmWireframeToolbox>(p => p
            .Add(x => x.OnComponentActivated, t => activated = t));

        cut.Find(".tm-wd-toolbox__item")
           .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        activated.Should().Be("TmButton");
    }

    [Fact]
    public void Toolbox_SpaceKeyOnItem_FiresOnComponentActivated()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons"));
        Services.AddSingleton(registry);

        string? activated = null;
        var cut = Render<TmWireframeToolbox>(p => p
            .Add(x => x.OnComponentActivated, t => activated = t));

        var item = cut.Find(".tm-wd-toolbox__item");
        item.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = " " });
        item.KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = " " });

        activated.Should().Be("TmButton");
    }

    [Fact]
    public void Toolbox_OtherKeyOnItem_DoesNotFireCallback()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons"));
        Services.AddSingleton(registry);

        string? activated = null;
        var cut = Render<TmWireframeToolbox>(p => p
            .Add(x => x.OnComponentActivated, t => activated = t));

        cut.Find(".tm-wd-toolbox__item")
           .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Tab" });

        activated.Should().BeNull();
    }

    // ── Phase 1: click / tap activation + keyboard-activation convention ──────

    [Fact]
    public void ItemClick_FiresOnComponentActivated()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons"));
        Services.AddSingleton(registry);

        var activated = new List<string>();
        var cut = Render<TmWireframeToolbox>(p => p
            .Add(x => x.OnComponentActivated, t => activated.Add(t)));

        cut.Find(".tm-wd-toolbox__item").Click();

        activated.Should().Equal("TmButton");
    }

    [Fact]
    public void EnterAndSpace_ActivateExactlyOnce()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons"));
        Services.AddSingleton(registry);

        var activated = new List<string>();
        var cut = Render<TmWireframeToolbox>(p => p
            .Add(x => x.OnComponentActivated, t => activated.Add(t)));
        var item = cut.Find(".tm-wd-toolbox__item");

        // The item is a div[role=button] (non-native): no browser click is synthesised, so the
        // library emulates — Enter on keydown, Space on keyup, once per press.
        item.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        activated.Should().HaveCount(1);

        item.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = " " });
        item.KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = " " });
        activated.Should().HaveCount(2);
    }

    [Fact]
    public void HeldEnter_AutoRepeat_DoesNotReactivate()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons"));
        Services.AddSingleton(registry);

        var activated = new List<string>();
        var cut = Render<TmWireframeToolbox>(p => p
            .Add(x => x.OnComponentActivated, t => activated.Add(t)));
        var item = cut.Find(".tm-wd-toolbox__item");

        item.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        item.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", Repeat = true });
        item.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", Repeat = true });

        activated.Should().HaveCount(1);
    }

    [Fact]
    public void SpaceKeyDown_Alone_DoesNotActivate_UntilKeyUp()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons"));
        Services.AddSingleton(registry);

        var activated = new List<string>();
        var cut = Render<TmWireframeToolbox>(p => p
            .Add(x => x.OnComponentActivated, t => activated.Add(t)));
        var item = cut.Find(".tm-wd-toolbox__item");

        item.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = " " });
        activated.Should().BeEmpty();

        item.KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = " " });
        activated.Should().HaveCount(1);
    }
    // ── Class parameter ────────────────────────────────────────────────────────

    [Fact]
    public void Toolbox_ClassParameter_AppliedToWrapper()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons"));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>(p => p.Add(x => x.Class, "my-class"));

        cut.Find(".tm-wd-toolbox").ClassList.Should().Contain("my-class");
    }

    // ── SVG preview ────────────────────────────────────────────────────────────

    [Fact]
    public void Toolbox_ItemPreview_ContainsSvg()
    {
        var registry = BuildRegistry(MakeDef("TmButton", "Buttons"));
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();

        cut.Find(".tm-wd-toolbox__item-preview svg").Should().NotBeNull();
    }

    // ── Phase 2: label, read-only, icon ────────────────────────────────────────

    [Fact]
    public void Toolbox_ItemLabel_AdvertisesClickAndDrag()
    {
        Services.AddSingleton(BuildRegistry(MakeDef("TmButton", "Buttons", displayName: "Button")));

        var cut = Render<TmWireframeToolbox>();

        cut.Find(".tm-wd-toolbox__item").GetAttribute("aria-label").Should().Be("Add Button to canvas (or drag)");
    }

    [Fact]
    public void Toolbox_ReadOnly_MarksItemsAriaDisabledAndStopsActivation()
    {
        Services.AddSingleton(BuildRegistry(MakeDef("TmButton", "Buttons")));
        var activated = new List<string>();

        var cut = Render<TmWireframeToolbox>(p => p
            .Add(c => c.ReadOnly, true)
            .Add(c => c.OnComponentActivated, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<string>(this, (Action<string>)activated.Add)));

        var item = cut.Find(".tm-wd-toolbox__item");
        item.GetAttribute("aria-disabled").Should().Be("true");
        item.GetAttribute("draggable").Should().Be("false");
        item.ClassList.Should().Contain("tm-wd-toolbox__item--disabled");

        item.Click();
        item.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        item.KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = " " });

        activated.Should().BeEmpty("a read-only toolbox never raises OnComponentActivated");
    }

    [Fact]
    public void Toolbox_Editable_ItemsAreNotAriaDisabled()
    {
        Services.AddSingleton(BuildRegistry(MakeDef("TmButton", "Buttons")));

        var cut = Render<TmWireframeToolbox>();

        cut.Find(".tm-wd-toolbox__item").HasAttribute("aria-disabled").Should().BeFalse();
        cut.Find(".tm-wd-toolbox__item").ClassList.Should().NotContain("tm-wd-toolbox__item--disabled");
    }

    [Fact]
    public void Toolbox_ClearSearch_RendersTmIcon()
    {
        Services.AddSingleton(BuildRegistry(MakeDef("TmButton", "Buttons")));
        var cut = Render<TmWireframeToolbox>();

        cut.Find("input[type=\"search\"]").Input("but");

        cut.Find(".tm-wd-toolbox__search-clear svg.tm-icon").Should().NotBeNull();
        cut.Find(".tm-wd-toolbox__search-clear").TextContent.Should().NotContain("✕");
    }
}
