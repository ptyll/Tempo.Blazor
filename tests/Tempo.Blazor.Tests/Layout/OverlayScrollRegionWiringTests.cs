using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// F6 review round 1, F17: TmDrawer and TmModal bodies adopted <c>syncScrollRegion</c> (a body that
/// overflows becomes a named, keyboard-reachable scroll region and so adds a tab stop). The
/// attach/stop calls are part of that behaviour change and are pinned here.
/// </summary>
public class OverlayScrollRegionWiringTests : LocalizationTestBase
{
    private const string FocusTrapModule = "./_content/Tempo.Blazor/js/tm-focus-trap.js";

    private BunitJSModuleInterop FocusTrap()
    {
        var module = JSInterop.SetupModule(FocusTrapModule);
        module.SetupVoid("activate", _ => true).SetVoidResult();
        module.SetupVoid("deactivate", _ => true).SetVoidResult();
        module.SetupVoid("syncScrollRegion", _ => true).SetVoidResult();
        module.SetupVoid("stopScrollRegion", _ => true).SetVoidResult();
        return module;
    }

    [Fact]
    public void Drawer_OpenSyncsTheBodyAsAScrollRegion_NamedByItsTitle()
    {
        var module = FocusTrap();

        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Modal, false)
            .Add(x => x.Position, DrawerPosition.Right)
            .Add(x => x.Title, "Details")
            .AddChildContent("Body"));

        cut.WaitForAssertion(() => module.VerifyInvoke("syncScrollRegion"));
        var call = module.Invocations["syncScrollRegion"].Single();
        call.Arguments[2].Should().NotBeNull("a titled drawer names its scroll region by the title id");
    }

    [Fact]
    public void Drawer_WithoutATitle_NamesItsScrollRegionByTheAriaLabel()
    {
        // F6 r2 G9: an inline sheet has header content, not a Title - its overflowing body used to be
        // a nameless tab stop.
        var module = FocusTrap();

        Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Modal, false)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.AriaLabel, "Panels")
            .AddChildContent("Body"));

        module.Invocations["syncScrollRegion"].Single().Arguments[3].Should().Be("Panels");
    }
    [Fact]
    public void Drawer_CloseStopsTheScrollRegion()
    {
        var module = FocusTrap();
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Modal, false)
            .Add(x => x.Title, "Details")
            .AddChildContent("Body"));
        cut.WaitForAssertion(() => module.VerifyInvoke("syncScrollRegion"));

        cut.Render(p => p.Add(x => x.IsOpen, false));

        cut.WaitForAssertion(() => module.VerifyInvoke("stopScrollRegion"));
    }

    [Fact]
    public async Task Drawer_DisposeStopsTheScrollRegion()
    {
        var module = FocusTrap();
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Modal, false)
            .AddChildContent("Body"));
        cut.WaitForAssertion(() => module.VerifyInvoke("syncScrollRegion"));

        await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());

        module.VerifyInvoke("stopScrollRegion");
    }

    [Fact]
    public void Modal_OpenSyncsTheBodyAsAScrollRegion_AndCloseStopsIt()
    {
        var module = FocusTrap();
        var cut = Render<TmModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Title, "Details")
            .AddChildContent("Body"));
        cut.WaitForAssertion(() => module.VerifyInvoke("syncScrollRegion"));

        cut.Render(p => p.Add(x => x.Show, false));

        cut.WaitForAssertion(() => module.VerifyInvoke("stopScrollRegion"));
    }
}