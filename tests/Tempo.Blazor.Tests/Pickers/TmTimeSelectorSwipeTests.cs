using System.Linq.Expressions;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// TDD for <see cref="TmTimeSelector"/> Fáze 8 (plan 1c95309c, ADR-0007): opt-in
/// <c>EnableSwipe</c>, JSInvokable swipe start/step/end/cancel, the render gate during
/// ticks, Type the time coexistence, and pass-through from <c>TmTimePicker</c>.
/// Gesture wiring itself lives in <c>tm-time-selector.js</c> over shared
/// <c>pointer-drag.js</c> (<c>npm run test:core-modules</c>).
/// </summary>
public class TmTimeSelectorSwipeTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-time-selector.js";
    private const string OverlayModulePath = "./_content/Tempo.Blazor/js/overlay.js";

    private BunitJSModuleInterop SetupSelectorModule()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.SetupVoid("update", _ => true).SetVoidResult();
        return module;
    }

    private IRenderedComponent<TmTimeSelector> RenderSelector(
        Action<ComponentParameterCollectionBuilder<TmTimeSelector>>? configure = null,
        TimeHourCycle hourCycle = TimeHourCycle.H23,
        TimeSelectorFit fit = TimeSelectorFit.Regular,
        TmLayoutMode layout = TmLayoutMode.Desktop)
    {
        SetupSelectorModule();
        return Render<TmTimeSelector>(p =>
        {
            p.Add(c => c.HourCycle, hourCycle);
            p.Add(c => c.LayoutMode, layout);
            p.Add(c => c.Fit, fit);
            configure?.Invoke(p);
        });
    }

    private static IElement Hours(IRenderedComponent<TmTimeSelector> cut)
        => cut.Find("[data-tm-time-segment='hours']");

    private static IElement Live(IRenderedComponent<TmTimeSelector> cut)
        => cut.Find("[role='status']");

    [Fact]
    public void EnableSwipe_DefaultsFalse_AndOmitsSwipeAttribute()
    {
        new TmTimeSelector().EnableSwipe.Should().BeFalse();

        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)));

        var root = cut.Find("[data-testid='time-selector']");
        var attr = root.GetAttribute("data-tm-time-swipe");
        (attr is null || attr == "false").Should().BeTrue(
            "without EnableSwipe the DOM must stay today's markup so JS never attaches pointer-drag");
    }

    [Fact]
    public void EnableSwipeTrue_SetsDataTmTimeSwipeOnRoot()
    {
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.EnableSwipe, true));

        cut.Find("[data-testid='time-selector']")
            .GetAttribute("data-tm-time-swipe").Should().Be("true");
    }

    [Fact]
    public async Task SwipeStep_HoursUp_RaisesValueChanged_WithoutAnnouncing()
    {
        TimeOnly? captured = null;
        TimeOnly? committed = null;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.EnableSwipe, true)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v)
            .Add(c => c.OnCommitted, (TimeOnly? v) => committed = v));

        await cut.InvokeAsync(() => cut.Instance.OnSwipeStartAsync("hours"));
        await cut.InvokeAsync(() => cut.Instance.OnSwipeStepAsync("hours", 1));
        await cut.InvokeAsync(() => cut.Instance.OnSwipeStepAsync("hours", 1));

        captured.Should().Be(new TimeOnly(16, 30));
        Hours(cut).TextContent.Trim().Should().Be("16");
        committed.Should().BeNull("ticks never fire OnCommitted — that waits for release");
        Live(cut).TextContent.Trim().Should().BeEmpty(
            "announce=false during ticks; the live region speaks on release");
    }

    [Fact]
    public async Task SwipeStep_WithoutPriorStart_StillSteps()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.EnableSwipe, true)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        // JS invokeMethodAsync is fire-and-forget: a fast pointermove can deliver the first
        // step before OnSwipeStartAsync has set _swipeActive. The step must still land.
        await cut.InvokeAsync(() => cut.Instance.OnSwipeStepAsync("hours", 1));

        captured.Should().Be(new TimeOnly(15, 30));
        Hours(cut).TextContent.Trim().Should().Be("15");
    }

    [Fact]
    public async Task SwipeTick_DoesNotCallJsUpdate_WhileGestureIsActive()
    {
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.EnableSwipe, true)
            .Add(c => c.ValueChanged, (TimeOnly? _) => { }));

        await cut.InvokeAsync(() => cut.Instance.OnSwipeStartAsync("hours"));
        var before = JSInterop.Invocations.Count(i => i.Identifier == "update");

        await cut.InvokeAsync(() => cut.Instance.OnSwipeStepAsync("hours", 1));

        JSInterop.Invocations.Count(i => i.Identifier == "update").Should().Be(before,
            "update() rebinds pointer-drag, detach() cancels the live gesture and rolls the origin back");
    }

    [Fact]
    public async Task SwipeEnd_AnnouncesAndFiresOnCommitted()
    {
        TimeOnly? committed = null;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.EnableSwipe, true)
            .Add(c => c.ValueChanged, (TimeOnly? v) => { })
            .Add(c => c.OnCommitted, (TimeOnly? v) => committed = v));

        await cut.InvokeAsync(() => cut.Instance.OnSwipeStartAsync("hours"));
        await cut.InvokeAsync(() => cut.Instance.OnSwipeStepAsync("hours", 1));
        await cut.InvokeAsync(() => cut.Instance.OnSwipeEndAsync("hours"));

        committed.Should().Be(new TimeOnly(15, 30));
        Live(cut).TextContent.Trim().Should().Be("Set to 15:30");
    }

    [Fact]
    public async Task SwipeCancel_RestoresOrigin_WithoutOnCommitted()
    {
        TimeOnly? captured = null;
        var committed = false;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.EnableSwipe, true)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v)
            .Add(c => c.OnCommitted, (TimeOnly? _) => committed = true));

        await cut.InvokeAsync(() => cut.Instance.OnSwipeStartAsync("hours"));
        await cut.InvokeAsync(() => cut.Instance.OnSwipeStepAsync("hours", 1));
        await cut.InvokeAsync(() => cut.Instance.OnSwipeStepAsync("hours", 1));
        captured.Should().Be(new TimeOnly(16, 30));

        await cut.InvokeAsync(() => cut.Instance.OnSwipeCancelAsync("hours"));

        Hours(cut).TextContent.Trim().Should().Be("14",
            "cancel rolls the spinbutton back to the gesture-start value");
        captured.Should().Be(new TimeOnly(14, 30),
            "a two-way bind must not be left mid-gesture; cancel restores through ValueChanged");
        committed.Should().BeFalse("cancel never fires OnCommitted");
        Live(cut).TextContent.Trim().Should().BeEmpty();
    }

    [Fact]
    public async Task SwipeCancel_FromNullValue_RestoresNull_WithoutOnCommitted()
    {
        TimeOnly? captured = new TimeOnly(0, 0);
        var committed = false;
        var cut = RenderSelector(p => p
            .Add(c => c.EnableSwipe, true)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v)
            .Add(c => c.OnCommitted, (TimeOnly? _) => committed = true));

        Hours(cut).TextContent.Trim().Should().Be("--");

        await cut.InvokeAsync(() => cut.Instance.OnSwipeStepAsync("hours", 1));
        captured.Should().NotBeNull("a tick from empty starts from now so ValueChanged sees a time");

        await cut.InvokeAsync(() => cut.Instance.OnSwipeCancelAsync("hours"));

        Hours(cut).TextContent.Trim().Should().Be("--",
            "cancel from an empty origin must restore the empty readout, not StartingTime()");
        captured.Should().BeNull(
            "ValueChanged must not leave a time the user never picked; cancel restores null");
        committed.Should().BeFalse("cancel never fires OnCommitted");
        Live(cut).TextContent.Trim().Should().BeEmpty();
    }

    [Fact]
    public async Task SwipeCancel_FromNullValue_NotifiesEditContext()
    {
        SetupSelectorModule();
        var model = new AppointmentModel();
        var cut = Render(builder =>
        {
            builder.OpenComponent<EditForm>(0);
            builder.AddAttribute(1, nameof(EditForm.Model), model);
            builder.AddAttribute(2, nameof(EditForm.ChildContent), (RenderFragment<EditContext>)(_ =>
                (RenderTreeBuilder b) =>
                {
                    b.OpenComponent<TmTimeSelector>(0);
                    b.AddAttribute(1, nameof(TmTimeSelector.HourCycle), TimeHourCycle.H23);
                    b.AddAttribute(2, nameof(TmTimeSelector.LayoutMode), TmLayoutMode.Desktop);
                    b.AddAttribute(3, nameof(TmTimeSelector.Fit), TimeSelectorFit.Regular);
                    b.AddAttribute(4, nameof(TmTimeSelector.EnableSwipe), true);
                    b.AddAttribute(5, nameof(TmTimeSelector.Value), model.Start);
                    b.AddAttribute(6, nameof(TmTimeSelector.ValueChanged),
                        EventCallback.Factory.Create<TimeOnly?>(this, v => model.Start = v));
                    b.AddAttribute(7, nameof(TmTimeSelector.ValueExpression),
                        (Expression<Func<TimeOnly?>>)(() => model.Start));
                    b.CloseComponent();
                }));
            builder.CloseComponent();
        });

        var selector = cut.FindComponent<TmTimeSelector>();
        await cut.InvokeAsync(() => selector.Instance.OnSwipeStepAsync("hours", 1));
        model.Start.Should().NotBeNull("a tick from empty starts from now so ValueChanged sees a time");

        var changedOnCancel = new List<string>();
        cut.FindComponent<EditForm>().Instance.EditContext!.OnFieldChanged +=
            (_, e) => changedOnCancel.Add(e.FieldIdentifier.FieldName);

        await cut.InvokeAsync(() => selector.Instance.OnSwipeCancelAsync("hours"));

        model.Start.Should().BeNull("cancel from an empty origin restores null");
        changedOnCancel.Should().ContainSingle().Which.Should().Be(nameof(AppointmentModel.Start),
            "cancel-from-null must NotifyFieldChanged so EditForm re-validates [Required] after the mid-gesture tick");
    }

    [Fact]
    public async Task SwipeStep_Disabled_IsNoOp()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.EnableSwipe, true)
            .Add(c => c.Disabled, true)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        await cut.InvokeAsync(() => cut.Instance.OnSwipeStartAsync("hours"));
        await cut.InvokeAsync(() => cut.Instance.OnSwipeStepAsync("hours", 1));
        await cut.InvokeAsync(() => cut.Instance.OnSwipeEndAsync("hours"));

        captured.Should().BeNull();
        Hours(cut).TextContent.Trim().Should().Be("14");
    }

    [Fact]
    public async Task SwipeStep_ReadOnly_IsNoOp()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.EnableSwipe, true)
            .Add(c => c.ReadOnly, true)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        await cut.InvokeAsync(() => cut.Instance.OnSwipeStartAsync("hours"));
        await cut.InvokeAsync(() => cut.Instance.OnSwipeStepAsync("hours", 1));
        await cut.InvokeAsync(() => cut.Instance.OnSwipeEndAsync("hours"));

        captured.Should().BeNull();
        Hours(cut).TextContent.Trim().Should().Be("14");
    }

    [Fact]
    public async Task SwipeTick_ThatDoesNotChangeTheReadout_DoesNotRerender()
    {
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.EnableSwipe, true)
            .Add(c => c.MinTime, new TimeOnly(14, 0))
            .Add(c => c.MaxTime, new TimeOnly(14, 30)));

        await cut.InvokeAsync(() => cut.Instance.OnSwipeStartAsync("hours"));
        var afterStart = cut.RenderCount;

        await cut.InvokeAsync(() => cut.Instance.OnSwipeStepAsync("hours", 1));

        Hours(cut).TextContent.Trim().Should().Be("14",
            "14:30 is already the inclusive max, so +hours clamps in place");
        cut.RenderCount.Should().Be(afterStart,
            "ShouldRender skips a swipe/auto-repeat tick whose visual fingerprint is unchanged");
    }

    [Fact]
    public void EnableSwipe_DoesNotHideTypeTime_OnMobileRegular()
    {
        var cut = RenderSelector(p => p
                .Add(c => c.AllowTyping, true)
                .Add(c => c.EnableSwipe, true),
            layout: TmLayoutMode.Mobile);

        cut.Find("[data-testid='time-selector-type-time']").TextContent.Trim()
            .Should().Be("Type the time");
        cut.Find("[data-testid='time-selector']")
            .GetAttribute("data-tm-time-swipe").Should().Be("true");
    }

    [Fact]
    public void EnableSwipe_NeverOffersTypeTime_InSquareFit()
    {
        var cut = RenderSelector(p => p
                .Add(c => c.AllowTyping, true)
                .Add(c => c.EnableSwipe, true),
            fit: TimeSelectorFit.Square,
            layout: TmLayoutMode.Mobile);

        cut.FindAll("[data-testid='time-selector-type-time']").Should().BeEmpty();
    }

    [Fact]
    public void TimePicker_ForwardsEnableSwipe_ToNestedSelector()
    {
        var overlay = JSInterop.SetupModule(OverlayModulePath);
        overlay.SetupVoid("open", _ => true).SetVoidResult();
        overlay.SetupVoid("update", _ => true).SetVoidResult();
        overlay.SetupVoid("close", _ => true).SetVoidResult();
        SetupSelectorModule();

        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.Label, "Meeting")
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.Presentation, TimePickerPresentation.Popup)
            .Add(c => c.EnableSwipe, true));

        cut.Find(".tm-time-picker__trigger").Click();

        cut.FindComponent<TmTimeSelector>().Instance.EnableSwipe.Should().BeTrue();
        cut.Find("[data-testid='time-selector']")
            .GetAttribute("data-tm-time-swipe").Should().Be("true");
        cut.FindComponents<TmOverlayPanel>().Should().ContainSingle();
    }

    private sealed class AppointmentModel
    {
        public TimeOnly? Start { get; set; }
    }
}
