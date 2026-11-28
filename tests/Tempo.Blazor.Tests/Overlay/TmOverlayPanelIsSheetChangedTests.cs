using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Overlay;

/// <summary>
/// Redesign 146c7e1b, phase 13: <c>TmOverlayPanel.IsSheetChanged</c> reports the sheet-versus-popover answer of the core,
/// including the FIRST resolution (<c>ResolvedLayoutChanged</c> deliberately never reports it), so a wrapper never derives
/// the answer a second time from the viewport scope or from a forced layout.
/// </summary>
public class TmOverlayPanelIsSheetChangedTests : LocalizationTestBase
{
    [Fact]
    public void FirstResolution_ViewportScopeMobile_ReportsTrue()
    {
        var reports = new List<bool>();
        var viewport = new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile);

        Render(builder =>
        {
            builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Name", TmLayoutScopes.Viewport);
            builder.AddAttribute(2, "Value", viewport);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(b =>
            {
                b.OpenComponent<TmOverlayPanel>(0);
                b.AddAttribute(1, "IsOpen", true);
                b.AddAttribute(2, "Title", "Panel");
                b.AddAttribute(5, "MobilePresentation", PanelPresentation.Auto);
                b.AddAttribute(3, "IsSheetChanged", EventCallback.Factory.Create<bool>(this, reports.Add));
                b.AddAttribute(4, "ChildContent", (RenderFragment)(bb => bb.AddContent(0, "Body")));
                b.CloseComponent();
            }));
            builder.CloseComponent();
        });

        reports.Should().Equal(true);
    }

    [Fact]
    public void FirstResolution_ForcedLayoutModeMobile_ReportsTrue()
    {
        var reports = new List<bool>();

        Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.MobilePresentation, PanelPresentation.Auto)
            .Add(c => c.Title, "Panel")
            .Add(c => c.IsSheetChanged, EventCallback.Factory.Create<bool>(this, reports.Add))
            .AddChildContent("<div>Body</div>"));

        reports.Should().Equal(true);
    }

    [Fact]
    public void FirstResolution_ForcedLayoutModeDesktop_ReportsFalse()
    {
        var reports = new List<bool>();

        Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.MobilePresentation, PanelPresentation.Auto)
            .Add(c => c.Title, "Panel")
            .Add(c => c.IsSheetChanged, EventCallback.Factory.Create<bool>(this, reports.Add))
            .AddChildContent("<div>Body</div>"));

        reports.Should().Equal(false);
    }

    [Fact]
    public void MobilePresentationPopover_OnMobileViewport_ReportsFalse()
    {
        var reports = new List<bool>();
        var viewport = new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile);

        Render(builder =>
        {
            builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Name", TmLayoutScopes.Viewport);
            builder.AddAttribute(2, "Value", viewport);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(b =>
            {
                b.OpenComponent<TmOverlayPanel>(0);
                b.AddAttribute(1, "IsOpen", true);
                b.AddAttribute(2, "MobilePresentation", PanelPresentation.Popover);
                b.AddAttribute(3, "IsSheetChanged", EventCallback.Factory.Create<bool>(this, reports.Add));
                b.AddAttribute(4, "ChildContent", (RenderFragment)(bb => bb.AddContent(0, "Body")));
                b.CloseComponent();
            }));
            builder.CloseComponent();
        });

        reports.Should().Equal(false);
    }

    [Fact]
    public void ViewportScope_DesktopToMobile_ReportsFirstFalseThenTrue_AndResolvedLayoutChangedKeepsItsSemantics()
    {
        var sheetReports = new List<bool>();
        var layoutReports = new List<TmLayoutMode>();
        var host = new ScopeHost(this, sheetReports, layoutReports);

        var cut = Render(host.Build);
        sheetReports.Should().Equal(false);
        layoutReports.Should().BeEmpty("the first resolution is never a ResolvedLayoutChanged");

        host.Mode = TmLayoutMode.Mobile;
        cut.InvokeAsync(host.Refresh).GetAwaiter().GetResult();

        sheetReports.Should().Equal(false, true);
        layoutReports.Should().Equal(TmLayoutMode.Mobile);
    }

    [Fact]
    public void AChangeOfMobilePresentation_IsReported()
    {
        var reports = new List<bool>();
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.MobilePresentation, PanelPresentation.Popover)
            .Add(c => c.Title, "Panel")
            .Add(c => c.IsSheetChanged, EventCallback.Factory.Create<bool>(this, reports.Add))
            .AddChildContent("<div>Body</div>"));

        cut.Render(p => p.Add(c => c.MobilePresentation, PanelPresentation.Auto));
        cut.Render(p => p.Add(c => c.MobilePresentation, PanelPresentation.Popover));

        reports.Should().Equal(false, true, false);
    }

    [Fact]
    public void AnAutoPanelWithoutAScope_ReportsTheInitialModeFirst_ThenTheProbeResult()
    {
        // No scope, no forced mode: the panel renders InitialMode until its probe has measured. A mobile InitialMode is a sheet from the very first report.
        var reports = new List<bool>();
        Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.MobilePresentation, PanelPresentation.Auto)
            .Add(c => c.InitialMode, TmLayoutMode.Mobile)
            .Add(c => c.Title, "Panel")
            .Add(c => c.IsSheetChanged, EventCallback.Factory.Create<bool>(this, reports.Add))
            .AddChildContent("<div>Body</div>"));

        reports.Should().Equal(true);
    }

    [Fact]
    public void NoChange_NoRepeatedReport()
    {
        var reports = new List<bool>();
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.MobilePresentation, PanelPresentation.Auto)
            .Add(c => c.Title, "Panel")
            .Add(c => c.IsSheetChanged, EventCallback.Factory.Create<bool>(this, reports.Add))
            .AddChildContent("<div>Body</div>"));

        cut.Render(p => p.Add(c => c.Title, "Other"));
        cut.Render(p => p.Add(c => c.Title, "Third"));

        reports.Should().Equal(true);
    }

    private sealed class ScopeHost
    {
        private readonly object _owner;
        private readonly List<bool> _sheet;
        private readonly List<TmLayoutMode> _layout;
        private Action? _rerender;

        public ScopeHost(object owner, List<bool> sheet, List<TmLayoutMode> layout)
        {
            _owner = owner;
            _sheet = sheet;
            _layout = layout;
        }

        public TmLayoutMode Mode { get; set; } = TmLayoutMode.Desktop;

        public Task Refresh()
        {
            _rerender?.Invoke();
            return Task.CompletedTask;
        }

        public void Build(RenderTreeBuilder builder)
        {
            builder.OpenComponent<HostComponent>(0);
            builder.AddAttribute(1, "Owner", this);
            builder.CloseComponent();
        }

        internal RenderFragment Content => builder =>
        {
            var viewport = new TmLayoutContext(Mode, Mode);
            builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Name", TmLayoutScopes.Viewport);
            builder.AddAttribute(2, "Value", viewport);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(b =>
            {
                b.OpenComponent<TmOverlayPanel>(0);
                b.AddAttribute(1, "IsOpen", true);
                b.AddAttribute(2, "Title", "Panel");
                b.AddAttribute(6, "MobilePresentation", PanelPresentation.Auto);
                b.AddAttribute(3, "IsSheetChanged", EventCallback.Factory.Create<bool>(_owner, _sheet.Add));
                b.AddAttribute(4, "ResolvedLayoutChanged", EventCallback.Factory.Create<TmLayoutMode>(_owner, _layout.Add));
                b.AddAttribute(5, "ChildContent", (RenderFragment)(bb => bb.AddContent(0, "Body")));
                b.CloseComponent();
            }));
            builder.CloseComponent();
        };

        internal void Attach(Action rerender) => _rerender = rerender;
    }

    private sealed class HostComponent : ComponentBase
    {
        [Parameter] public ScopeHost Owner { get; set; } = default!;

        protected override void OnInitialized() => Owner.Attach(StateHasChanged);

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Owner.Content);
    }
}
