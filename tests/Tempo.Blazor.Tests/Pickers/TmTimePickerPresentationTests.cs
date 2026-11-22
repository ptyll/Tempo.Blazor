using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// Plan 1c95309c Fáze 6 — TmTimePicker Presentation (Segments / Popup / Auto).
/// Default Segments keeps today's DOM; Popup/Auto compose TmTimeSelector into TmOverlayPanel.
/// </summary>
public class TmTimePickerPresentationTests : LocalizationTestBase
{
    private const string OverlayModulePath = "./_content/Tempo.Blazor/js/overlay.js";
    private const string SelectorModulePath = "./_content/Tempo.Blazor/js/tm-time-selector.js";

    private void SetupOverlayAndSelectorModules()
    {
        var overlay = JSInterop.SetupModule(OverlayModulePath);
        overlay.SetupVoid("open", _ => true).SetVoidResult();
        overlay.SetupVoid("update", _ => true).SetVoidResult();
        overlay.SetupVoid("close", _ => true).SetVoidResult();

        var selector = JSInterop.SetupModule(SelectorModulePath);
        selector.SetupVoid("attach", _ => true).SetVoidResult();
        selector.SetupVoid("detach", _ => true).SetVoidResult();
        selector.SetupVoid("update", _ => true).SetVoidResult();
    }

    private IRenderedComponent<TmTimePicker> RenderPicker(
        Action<ComponentParameterCollectionBuilder<TmTimePicker>>? configure = null)
    {
        SetupOverlayAndSelectorModules();
        return Render<TmTimePicker>(p =>
        {
            p.Add(c => c.Value, new TimeOnly(14, 30));
            p.Add(c => c.Label, "Meeting");
            p.Add(c => c.HourCycle, TimeHourCycle.H23);
            configure?.Invoke(p);
        });
    }

    // ── 1. Segments snapshot (GREEN already: default DOM must not change) ────

    [Fact]
    public void DefaultPresentation_IsSegments_AndKeepsTodaysDom()
    {
        new TmTimePicker().Presentation.Should().Be(TimePickerPresentation.Segments);

        var cut = RenderPicker();

        AssertSegmentsDom(cut);
        cut.FindAll("[aria-haspopup]").Should().BeEmpty();
        cut.FindComponents<TmOverlayPanel>().Should().BeEmpty();
        cut.FindComponents<TmTimeSelector>().Should().BeEmpty();
    }

    [Fact]
    public void ExplicitSegments_KeepsTodaysDom()
    {
        var cut = RenderPicker(p => p.Add(c => c.Presentation, TimePickerPresentation.Segments));

        AssertSegmentsDom(cut);
        cut.FindAll("[aria-haspopup]").Should().BeEmpty();
        cut.FindComponents<TmOverlayPanel>().Should().BeEmpty();
    }

    private static void AssertSegmentsDom(IRenderedComponent<TmTimePicker> cut)
    {
        cut.Find(".tm-time-picker").Should().NotBeNull();
        var body = cut.Find(".tm-time-picker-body");
        body.GetAttribute("role").Should().Be("group");
        cut.FindAll(".tm-time-seg").Should().HaveCount(2);
        cut.Find(".tm-time-seg--hours").GetAttribute("value").Should().Be("14");
        cut.Find(".tm-time-seg--minutes").GetAttribute("value").Should().Be("30");
        cut.Find(".tm-picker-label").TextContent.Should().Be("Meeting");
    }

    // ── 2. Popup ─────────────────────────────────────────────────────────────

    [Fact]
    public void Popup_RendersSegmentsPlusClosedTrigger()
    {
        var cut = RenderPicker(p => p.Add(c => c.Presentation, TimePickerPresentation.Popup));

        AssertSegmentsDom(cut);

        var trigger = cut.Find(".tm-time-picker__trigger");
        trigger.GetAttribute("type").Should().Be("button");
        trigger.GetAttribute("aria-haspopup").Should().Be("dialog");
        trigger.GetAttribute("aria-expanded").Should().Be("false");
        trigger.GetAttribute("aria-controls").Should().NotBeNullOrWhiteSpace();
        trigger.GetAttribute("aria-label").Should().Be("Choose time");
        cut.FindComponents<TmOverlayPanel>().Should().BeEmpty("the panel renders only while open");
    }

    [Fact]
    public void Popup_ClickTrigger_OpensDialogWithSelector()
    {
        var cut = RenderPicker(p => p.Add(c => c.Presentation, TimePickerPresentation.Popup));

        cut.Find(".tm-time-picker__trigger").Click();

        cut.Find(".tm-time-picker__trigger").GetAttribute("aria-expanded").Should().Be("true");

        var panel = cut.FindComponent<TmOverlayPanel>();
        panel.Instance.Role.Should().Be("dialog");
        panel.Instance.TrapFocus.Should().BeTrue();
        panel.Instance.MobilePresentation.Should().Be(PanelPresentation.Auto);

        var selector = cut.FindComponent<TmTimeSelector>();
        selector.Instance.Value.Should().Be(new TimeOnly(14, 30));

        var scope = cut.FindComponent<Tempo.Blazor.Components.Feedback.TmFocusScope>();
        scope.Instance.Active.Should().BeTrue();
        scope.Instance.Modal.Should().BeTrue();
        scope.Find(".tm-focus-scope").GetAttribute("role").Should().Be("dialog");
    }

    // ── 3. Auto ──────────────────────────────────────────────────────────────

    [Fact]
    public void Auto_OnDesktop_BehavesLikePopup()
    {
        var cut = RenderPicker(p => p
            .Add(c => c.Presentation, TimePickerPresentation.Auto)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop));

        AssertSegmentsDom(cut);
        cut.Find(".tm-time-picker__trigger").GetAttribute("aria-haspopup").Should().Be("dialog");
        cut.FindAll(".tm-time-seg").Should().HaveCount(2);
    }

    [Fact]
    public void Auto_OnMobile_RendersTriggerOnly_AndOpensSheet()
    {
        SetupOverlayAndSelectorModules();
        var viewport = new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile);
        var cut = Render(builder =>
        {
            builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Name", TmLayoutScopes.Viewport);
            builder.AddAttribute(2, "Value", viewport);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(b =>
            {
                b.OpenComponent<TmTimePicker>(0);
                b.AddAttribute(1, nameof(TmTimePicker.Value), new TimeOnly(14, 30));
                b.AddAttribute(2, nameof(TmTimePicker.Label), "Meeting");
                b.AddAttribute(3, nameof(TmTimePicker.HourCycle), TimeHourCycle.H23);
                b.AddAttribute(4, nameof(TmTimePicker.Presentation), TimePickerPresentation.Auto);
                b.CloseComponent();
            }));
            builder.CloseComponent();
        });

        cut.FindAll(".tm-time-seg").Should().BeEmpty("Auto on mobile is a trigger, not segments");
        var trigger = cut.Find(".tm-time-picker__trigger");
        trigger.GetAttribute("aria-haspopup").Should().Be("dialog");
        trigger.GetAttribute("aria-labelledby").Should().NotBeNullOrWhiteSpace();
        trigger.TextContent.Should().Contain("14:30");

        trigger.Click();

        var panel = cut.FindComponent<TmOverlayPanel>();
        panel.Instance.IsSheet.Should().BeTrue();
        panel.Instance.Role.Should().Be("dialog");
        panel.Instance.TrapFocus.Should().BeTrue();
        panel.Instance.MobilePresentation.Should().Be(PanelPresentation.Auto);
        panel.Instance.SheetTitle.Should().Be("Meeting");
        panel.Instance.SheetInitialFocusSelector.Should().Contain("hours");
        cut.FindComponent<TmTimeSelector>().Should().NotBeNull();
    }

    [Fact]
    public void Auto_SheetTitle_FallsBack_SheetTitleThenLabelThenLocalized()
    {
        SetupOverlayAndSelectorModules();
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Presentation, TimePickerPresentation.Auto)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.SheetTitle, "Pick a slot"));

        cut.Find(".tm-time-picker__trigger").Click();
        cut.FindComponent<TmOverlayPanel>().Instance.SheetTitle.Should().Be("Pick a slot");
    }

    [Fact]
    public void Auto_SheetTitle_FallsBackToLocalized_WhenNoLabel()
    {
        SetupOverlayAndSelectorModules();
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Presentation, TimePickerPresentation.Auto)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.HourCycle, TimeHourCycle.H23));

        cut.Find(".tm-time-picker__trigger").Click();
        cut.FindComponent<TmOverlayPanel>().Instance.SheetTitle.Should().Be("Time");
    }

    [Fact]
    public void Popup_EmptyLabel_NamesDialogWithLocalizedSheetTitle()
    {
        SetupOverlayAndSelectorModules();
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Presentation, TimePickerPresentation.Popup)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.Label, string.Empty));

        cut.Find(".tm-time-picker__trigger").Click();

        var dialog = cut.Find(".tm-time-picker__popup");
        dialog.GetAttribute("role").Should().Be("dialog");
        dialog.GetAttribute("aria-label").Should().Be("Time",
            "with an empty Label, AriaLabel falls back to EffectiveSheetTitle (TmTimePicker_SheetTitle)");
        dialog.GetAttribute("aria-labelledby").Should().BeNull();
    }

    [Fact]
    public void SheetTitle_XmlDoc_NamesTheLocalizedKey()
    {
        var path = Path.Combine(
            Tempo.Blazor.Tests.Theme.ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor", "Components", "Pickers", "TmTimePicker.razor.cs");
        File.ReadAllText(path).Should().Contain(
            "localized <c>TmTimePicker_SheetTitle</c>",
            "SheetTitle XML doc must name the resource key, matching CHANGELOG and the JSON overlay");
    }

    [Fact]
    public void TriggerId_HostValue_LandsOnTheTrigger()
    {
        var cut = RenderPicker(p => p
            .Add(c => c.Presentation, TimePickerPresentation.Popup)
            .Add(c => c.TriggerId, "host-time-trigger"));

        cut.Find(".tm-time-picker__trigger").Id.Should().Be("host-time-trigger");
        new TmTimePicker().TriggerId.Should().BeNull("TriggerId is additive and defaults to the generated id");
    }

    [Fact]
    public void Popup_AllowOvernightRange_ForwardsToSelector()
    {
        var cut = RenderPicker(p => p
            .Add(c => c.Presentation, TimePickerPresentation.Popup)
            .Add(c => c.MinTime, new TimeOnly(22, 0))
            .Add(c => c.MaxTime, new TimeOnly(6, 0))
            .Add(c => c.AllowOvernightRange, true));

        cut.Find(".tm-time-picker__trigger").Click();

        cut.FindComponent<TmTimeSelector>().Instance.AllowOvernightRange.Should().BeTrue();
        cut.FindComponent<TmTimeInput>().Instance.AllowOvernightRange.Should().BeTrue();
    }
}
