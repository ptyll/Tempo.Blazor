using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>TDD tests for TmToast and TmToastContainer.</summary>
public class TmToastTests : LocalizationTestBase
{
    public TmToastTests()
    {
        Services.AddScoped<ToastService>();
    }

    // ── TmToastContainer rendering ──

    [Fact]
    public void Container_Renders_WhenNoToasts_IsEmpty()
    {
        var cut = Render<TmToastContainer>();
        cut.FindAll(".tm-toast").Should().BeEmpty();
    }

    [Fact]
    public void Container_Renders_Toast_WhenServiceShowsCalled()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("Hello!");
        cut.Render();

        cut.FindAll(".tm-toast").Should().HaveCount(1);
    }

    [Fact]
    public void Container_Renders_ToastMessage()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("Test message");
        cut.Render();

        cut.Find(".tm-toast-message").TextContent.Trim().Should().Be("Test message");
    }

    [Fact]
    public void Container_Renders_ToastTitle_WhenProvided()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("Body", "Title Here");
        cut.Render();

        cut.Find(".tm-toast-title").TextContent.Trim().Should().Be("Title Here");
    }

    [Fact]
    public void Container_NoTitle_WhenNotProvided()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("Body only");
        cut.Render();

        cut.FindAll(".tm-toast-title").Should().BeEmpty();
    }

    [Fact]
    public void Container_SeverityClass_Success()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("OK");
        cut.Render();

        cut.Find(".tm-toast").ClassList.Should().Contain("tm-toast--success");
    }

    [Fact]
    public void Container_SeverityClass_Error()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowError("Fail");
        cut.Render();

        cut.Find(".tm-toast").ClassList.Should().Contain("tm-toast--error");
    }

    [Fact]
    public void Container_SeverityClass_Warning()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowWarning("Careful");
        cut.Render();

        cut.Find(".tm-toast").ClassList.Should().Contain("tm-toast--warning");
    }

    [Fact]
    public void Container_SeverityClass_Info()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("FYI");
        cut.Render();

        cut.Find(".tm-toast").ClassList.Should().Contain("tm-toast--info");
    }

    [Fact]
    public void Container_PositionClass_TopRight()
    {
        var cut = Render<TmToastContainer>(p => p.Add(c => c.Position, ToastPosition.TopRight));
        cut.Find(".tm-toast-container").ClassList.Should().Contain("tm-toast-container--top-right");
    }

    [Fact]
    public void Container_PositionClass_BottomLeft()
    {
        var cut = Render<TmToastContainer>(p => p.Add(c => c.Position, ToastPosition.BottomLeft));
        cut.Find(".tm-toast-container").ClassList.Should().Contain("tm-toast-container--bottom-left");
    }

    [Fact]
    public void Container_DismissButton_RemovesToast()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("Bye");
        cut.Render();
        cut.FindAll(".tm-toast").Should().HaveCount(1);

        cut.Find(".tm-toast-dismiss").Click();
        cut.Render();

        cut.FindAll(".tm-toast").Should().BeEmpty();
    }

    [Fact]
    public void Container_MaxVisible_LimitsRenderedToasts()
    {
        var cut = Render<TmToastContainer>(p => p.Add(c => c.MaxVisible, 2));
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("One");
        svc.ShowInfo("Two");
        svc.ShowWarning("Three");
        cut.Render();

        // Only 2 should be visible (most recent)
        cut.FindAll(".tm-toast").Count.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public void Container_MultipleToasts_RendersAll()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("One");
        svc.ShowError("Two");
        cut.Render();

        cut.FindAll(".tm-toast").Should().HaveCount(2);
    }

    [Fact]
    public void Container_HasIcon_PerSeverity()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("With icon");
        cut.Render();

        cut.FindAll(".tm-toast-icon").Should().NotBeEmpty();
    }

    [Fact]
    public void Container_HasProgressBar()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("Progress");
        cut.Render();

        cut.FindAll(".tm-toast-progress").Should().NotBeEmpty();
    }

    [Fact]
    public void Container_IsA_LabelledRegion()
    {
        var cut = Render<TmToastContainer>();

        var region = cut.Find(".tm-toast-container");
        region.GetAttribute("role").Should().Be("region");
        region.GetAttribute("aria-label").Should().Be("Notifications");
    }

    [Fact]
    public void Container_TwoPersistentLiveRegions_ExistEmpty_OnFirstRender()
    {
        var cut = Render<TmToastContainer>();

        var polite = cut.Find("[data-toast-live='polite']");
        var assertive = cut.Find("[data-toast-live='assertive']");
        polite.GetAttribute("role").Should().Be("status");
        assertive.GetAttribute("role").Should().Be("alert");
        polite.TextContent.Should().BeEmpty("nothing was announced yet");
        assertive.TextContent.Should().BeEmpty("nothing was announced yet");
    }

    [Fact]
    public void Container_Show_AnnouncesTitleAndMessage_InThePoliteRegion()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("Saved", "Backup");
        cut.Render();

        cut.WaitForAssertion(() =>
            cut.Find("[data-toast-live='polite']").TextContent.Trim().Should().Be("Backup: Saved"));
    }

    [Fact]
    public void Container_Error_AnnouncesInTheAssertiveRegion()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowError("Failed");
        cut.Render();

        cut.WaitForAssertion(() =>
            cut.Find("[data-toast-live='assertive']").TextContent.Trim().Should().Be("Failed"));
    }

    [Fact]
    public void Container_Dedupe_AnnouncesOncePerTextChange()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.Show(new ToastOptions { Message = "Saving…", Key = "save" });
        cut.Render();
        cut.WaitForAssertion(() =>
            cut.Find("[data-toast-live='polite']").TextContent.Trim().Should().Be("Saving…"));

        // Same text: the replacement announces NOTHING (an unchanged textContent would not be
        // re-spoken by a screen reader anyway).
        svc.Show(new ToastOptions { Message = "Saving…", Key = "save" });
        cut.Render();
        cut.WaitForAssertion(() => cut.Find(".tm-toast-message").TextContent.Trim().Should().Be("Saving…"));
        cut.Find("[data-toast-live='polite']").TextContent.Trim().Should().Be("Saving…");

        // Changed text: exactly one fresh announcement of the new text.
        svc.Show(new ToastOptions { Message = "Saved", Key = "save" });
        cut.Render();
        cut.WaitForAssertion(() =>
            cut.Find("[data-toast-live='polite']").TextContent.Trim().Should().Be("Saved"));
    }

    [Fact]
    public void Container_ToastCard_CarriesNoLiveRole()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("FYI");
        svc.ShowError("Boom");
        cut.Render();

        foreach (var card in cut.FindAll(".tm-toast"))
            card.HasAttribute("role").Should().BeFalse("announcements live in the two persistent regions, not the cards");
    }

    // ── F3 review round 3 (V1b): the container re-raises on every push ─────────
    // The round-2 raise was keyed on the VISIBLE COUNT: a push at the MaxVisible cap (count
    // unchanged) or an expire+push in one render never re-raised, so the newest toast painted
    // UNDER a sheet or dialog opened after the first push. The container must track the newest
    // toast id instead and raise whenever an unseen id appears.

    [Fact]
    public void Container_Raises_OnEveryPush_EvenAtTheMaxVisibleCap()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-top-layer.js");
        module.SetupVoid("pinRoot", _ => true).SetVoidResult();
        module.SetupVoid("raise", _ => true).SetVoidResult();

        var cut = Render<TmToastContainer>(p => p.Add(c => c.MaxVisible, 1));
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("One");
        cut.Render();
        svc.ShowSuccess("Two");
        cut.Render();

        // MaxVisible=1: the visible count stays 1 across both pushes, yet each push must re-raise
        // the container above every surface promoted in between.
        module.Invocations.Where(i => i.Identifier == "raise").Should().HaveCount(2,
            "a push at the MaxVisible cap changes no count — only the newest-id tracking re-raises");
    }

    [Fact]
    public void Container_Raises_WhenAVisibleToastExpiresAndAnotherPushesInOneRender()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-top-layer.js");
        module.SetupVoid("pinRoot", _ => true).SetVoidResult();
        module.SetupVoid("raise", _ => true).SetVoidResult();

        var cut = Render<TmToastContainer>(p => p.Add(c => c.MaxVisible, 2));
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("One");
        cut.Render();
        svc.ShowSuccess("Two");
        cut.Render();

        // Expire the OLDEST and push a new one before a render runs: the count is unchanged (still
        // 2 of MaxVisible 2) but the newest id changed, so the container must re-raise.
        svc.Remove(svc.Toasts.OrderBy(t => t.CreatedAt).First().Id);
        svc.ShowInfo("Three");
        cut.Render();

        module.Invocations.Where(i => i.Identifier == "raise").Should().HaveCount(3,
            "an expire+push in one render changes no count — the newest-id tracking must still re-raise");
    }

    [Fact]
    public void Container_Demotes_WhenTheLastToastLeaves_AndUnpins()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-top-layer.js");
        module.SetupVoid("pinRoot", _ => true).SetVoidResult();
        module.SetupVoid("unpinRoot", _ => true).SetVoidResult();
        module.SetupVoid("raise", _ => true).SetVoidResult();
        module.SetupVoid("demote", _ => true).SetVoidResult();

        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("One");
        cut.Render();
        svc.Remove(svc.Toasts.Single().Id);
        cut.Render();

        module.Invocations.Where(i => i.Identifier == "demote").Should().HaveCount(1,
            "the persistent container demotes when the last toast leaves");
        module.Invocations.Where(i => i.Identifier == "unpinRoot").Should().HaveCount(1,
            "a demoted container leaves the pinned-on-top registry so promote() stops re-raising it");
    }

    // ── F6 follow-up (c51ddf81 #4): a dismissed toast must not strand focus on <body> ──────
    // The container is inert-exempt status UI, so its dismiss button can hold focus while a
    // modal sheet/dialog is open. Removing the toast removes that button, so the container asks
    // the shared focus trap to recover the lost focus — once per render in which an id leaves
    // the visible set, and never on a push.

    [Fact]
    public void Container_PushingAToast_DoesNotAskTheTrapToRecoverFocus()
    {
        var trap = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js");
        trap.Setup<bool>("restoreTrapFocusIfLost", _ => true).SetResult(true);

        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("One");
        cut.WaitForAssertion(() => cut.FindAll(".tm-toast").Should().HaveCount(1));

        trap.Invocations.Where(i => i.Identifier == "restoreTrapFocusIfLost").Should().BeEmpty(
            "a push never moves focus and must not ask the trap for anything");
    }

    [Fact]
    public void Container_RemovingAToast_AsksTheTrapToRecoverLostFocus()
    {
        var trap = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js");
        trap.Setup<bool>("restoreTrapFocusIfLost", _ => true).SetResult(true);

        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("One");
        cut.WaitForAssertion(() => cut.FindAll(".tm-toast").Should().HaveCount(1));

        svc.Remove(svc.Toasts.Single().Id);
        cut.WaitForAssertion(() => cut.FindAll(".tm-toast").Should().BeEmpty());

        trap.Invocations.Where(i => i.Identifier == "restoreTrapFocusIfLost").Should().HaveCount(1,
            "the removed toast took its focused dismiss button along — the trap gets one chance to recover");
    }

    [Fact]
    public void Container_MaxVisible1_ReplacingTheVisibleToast_AsksTheTrapToRecoverLostFocus()
    {
        // Review fix: the ask keyed on the visible COUNT misses replacements — at MaxVisible=1 the
        // second push replaces the rendered card and the count stays 1, yet the toast that could
        // hold focus left the DOM. The visible id set drops an entry: exactly one ask.
        var trap = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js");
        trap.Setup<bool>("restoreTrapFocusIfLost", _ => true).SetResult(true);

        var cut = Render<TmToastContainer>(p => p.Add(c => c.MaxVisible, 1));
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("One");
        cut.WaitForAssertion(() => cut.FindAll(".tm-toast").Should().HaveCount(1));
        trap.Invocations.Where(i => i.Identifier == "restoreTrapFocusIfLost").Should().BeEmpty();

        svc.ShowInfo("Two");
        cut.WaitForAssertion(() => cut.Find(".tm-toast-message").TextContent.Trim().Should().Be("Two"));

        trap.Invocations.Where(i => i.Identifier == "restoreTrapFocusIfLost").Should().HaveCount(1,
            "the replaced card left the DOM — a count comparison never sees this, the id set does");
    }

    [Fact]
    public void Container_MaxVisible1_PushTwoAndRemoveTheVisibleToast_AsksTheTrapToRecoverExactlyOnce()
    {
        // Review fix: "push 2, remove visible → exactly one invocation". The second push and the
        // removal of the rendered card coalesce into one render: the visible id set moves
        // {first} -> {second} in a single pass — one ask for the whole exchange, never two, and
        // a count comparison would ask ZERO times (1 -> 1 -> 1, never "dropped").
        var trap = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js");
        trap.Setup<bool>("restoreTrapFocusIfLost", _ => true).SetResult(true);

        var cut = Render<TmToastContainer>(p => p.Add(c => c.MaxVisible, 1));
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("One");
        cut.WaitForAssertion(() => cut.FindAll(".tm-toast").Should().HaveCount(1));
        trap.Invocations.Where(i => i.Identifier == "restoreTrapFocusIfLost").Should().BeEmpty();

        var renderedId = svc.Toasts.Single(t => t.Message == "One").Id;
        svc.ShowInfo("Two");
        svc.Remove(renderedId);
        cut.WaitForAssertion(() => cut.Find(".tm-toast-message").TextContent.Trim().Should().Be("Two"));

        trap.Invocations.Where(i => i.Identifier == "restoreTrapFocusIfLost").Should().HaveCount(1,
            "one coalesced id-set change ({first} -> {second}) asks the trap exactly once");
    }

    // ── fze-3: action button, Content fragment, pause on hover/focus, Escape, politeness ──

    [Fact]
    public void Container_RendersActionButton_ClickInvokesDelegateAndClosesToast()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();
        var invoked = false;
        svc.Show(new ToastOptions
        {
            Message = "Deleted",
            Action = new ToastAction { Label = "Restore", OnClick = () => { invoked = true; return Task.CompletedTask; } },
        });
        cut.Render();

        var button = cut.Find(".tm-toast-action");
        button.TextContent.Trim().Should().Be("Restore");

        button.Click();
        cut.Render();

        invoked.Should().BeTrue("activating the action runs the delegate");
        cut.FindAll(".tm-toast").Should().BeEmpty("the toast closes after its action ran");
    }

    [Fact]
    public void Container_ShowUndo_RendersLocalizedUndoLabel()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowUndo("Item deleted", () => Task.CompletedTask);
        cut.Render();

        cut.Find(".tm-toast-action").TextContent.Trim().Should().Be("Undo");
    }

    [Fact]
    public void Container_RendersContentFragmentBelowMessage()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.Show(new ToastOptions
        {
            Message = "Body",
            Content = builder => builder.AddMarkupContent(0, "<em>extra</em>"),
        });
        cut.Render();

        cut.Find(".tm-toast-custom").TextContent.Should().Contain("extra");
    }

    [Fact]
    public void Container_PoliteToast_IsAnnouncedViaThePoliteRegion()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("FYI");
        cut.Render();

        cut.WaitForAssertion(() =>
            cut.Find("[data-toast-live='polite']").TextContent.Trim().Should().Be("FYI"));
    }

    [Fact]
    public void Container_MouseEnter_PausesAndMarksPaused_MouseLeave_Resumes()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("hover me");
        cut.Render();
        var id = svc.Toasts[0].Id;

        cut.Find(".tm-toast").TriggerEvent("onmouseenter", new MouseEventArgs());
        cut.Render();

        svc.IsPaused(id).Should().BeTrue("hovering pauses the auto-dismiss timer");
        cut.Find(".tm-toast").ClassList.Should().Contain("tm-toast--paused",
            "the paused modifier freezes the progress animation via animation-play-state");

        cut.Find(".tm-toast").TriggerEvent("onmouseleave", new MouseEventArgs());
        cut.Render();

        svc.IsPaused(id).Should().BeFalse("leaving resumes the timer with the remaining time");
        cut.Find(".tm-toast").ClassList.Should().NotContain("tm-toast--paused");
    }

    [Fact]
    public void Container_FocusIn_Pauses_FocusOut_Resumes()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("focus me");
        cut.Render();
        var id = svc.Toasts[0].Id;

        cut.Find(".tm-toast").TriggerEvent("onfocusin", new FocusEventArgs());
        cut.Render();
        svc.IsPaused(id).Should().BeTrue("focusing into the toast (Tab to its buttons) pauses it");

        cut.Find(".tm-toast").TriggerEvent("onfocusout", new FocusEventArgs());
        cut.Render();
        svc.IsPaused(id).Should().BeFalse();
    }

    [Fact]
    public void Container_PauseOnHoverFalse_DoesNotPause()
    {
        var cut = Render<TmToastContainer>(p => p.Add(c => c.PauseOnHover, false));
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("never pause");
        cut.Render();
        var id = svc.Toasts[0].Id;

        cut.Find(".tm-toast").TriggerEvent("onmouseenter", new MouseEventArgs());
        cut.Render();

        svc.IsPaused(id).Should().BeFalse();
        cut.Find(".tm-toast").ClassList.Should().NotContain("tm-toast--paused");
    }

    [Fact]
    public void Container_StickyToast_DoesNotPauseOnHover()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("sticky", duration: 0);
        cut.Render();
        var id = svc.Toasts[0].Id;

        cut.Find(".tm-toast").TriggerEvent("onmouseenter", new MouseEventArgs());
        cut.Render();

        svc.IsPaused(id).Should().BeFalse("a sticky toast has no timer to pause");
    }

    [Fact]
    public void Container_EscapeDismissal_ComesFromTheJsModule_DismissToastRemovesTheCard()
    {
        // Escape handling lives in tm-toast.js (keydown listener on the container): Blazor only
        // exposes the dismissal entry point the module calls.
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("escapable");
        cut.Render();
        cut.FindAll(".tm-toast").Should().HaveCount(1);

        cut.Instance.DismissToast(svc.Toasts.Single().Id);
        cut.Render();

        cut.FindAll(".tm-toast").Should().BeEmpty("DismissToast (fired by the JS Escape listener) removes the card");
    }

    [Fact]
    public void Container_ActionButton_CarriesTheAltTKeyshortcut()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowUndo("Item deleted", () => Task.CompletedTask);
        cut.Render();

        cut.Find(".tm-toast-action").GetAttribute("aria-keyshortcuts").Should().Be("Alt+T",
            "the JS module focuses the newest action button on Alt+T and the button advertises it");
    }

    [Fact]
    public void Container_DoubleClickAction_RunsTheDelegateOnce()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();
        var invocations = 0;
        svc.Show(new ToastOptions
        {
            Message = "Deleted",
            Action = new ToastAction { Label = "Restore", OnClick = () => { invocations++; return Task.CompletedTask; } },
        });
        cut.Render();

        cut.Find(".tm-toast-action").Click();
        cut.Render();

        invocations.Should().Be(1);
        cut.FindAll(".tm-toast-action").Should().BeEmpty(
            "the toast is removed BEFORE the delegate runs — a second click lands on a gone card");
        cut.FindAll(".tm-toast").Should().BeEmpty();
    }

    [Fact]
    public void Container_ActionDelegateThrowing_StillClosesTheToast()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();
        svc.Show(new ToastOptions
        {
            Message = "Deleted",
            Action = new ToastAction { Label = "Restore", OnClick = () => throw new InvalidOperationException("boom") },
        });
        cut.Render();

        var click = () => cut.Find(".tm-toast-action").Click();

        click.Should().Throw<InvalidOperationException>();
        cut.Render();
        cut.FindAll(".tm-toast").Should().BeEmpty("remove-first means the toast closes even when the delegate throws");
    }

    [Fact]
    public void Container_MinActionDuration_FloorsActionToasts()
    {
        var cut = Render<TmToastContainer>(p => p.Add(c => c.MinActionDuration, 15000));
        var svc = Services.GetRequiredService<ToastService>();

        svc.Show(new ToastOptions { Message = "m", Duration = 3000, Action = new ToastAction { Label = "Undo" } });
        cut.Render();

        svc.Toasts[0].Duration.Should().Be(15000, "the container pushes its floor into the service");
        cut.Find(".tm-toast-progress-bar").GetAttribute("style").Should().Contain("15000ms");
    }

    [Fact]
    public void Container_MinActionDuration_DefaultIsTenSeconds()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.Show(new ToastOptions { Message = "m", Duration = 3000, Action = new ToastAction { Label = "Undo" } });
        cut.Render();

        svc.Toasts[0].Duration.Should().Be(10_000);
    }

    [Fact]
    public async Task Container_DocumentHidden_PausesAll_VisibleResumesNonHeldToasts()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-toast-visibility.js");
        module.Setup<bool>("watch", _ => true).SetResult(false);

        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();
        svc.ShowInfo("one");
        svc.ShowInfo("two");
        cut.Render();
        var idOne = svc.Toasts[0].Id;

        var instance = cut.Instance;
        await instance.OnDocumentVisibilityChanged(hidden: true);
        cut.Render();

        svc.IsPaused(idOne).Should().BeTrue("a hidden document pauses every auto-dismiss timer");

        // A toast the user is hovering when the tab hides must stay paused after the tab returns.
        cut.FindAll(".tm-toast")[0].TriggerEvent("onmouseenter", new MouseEventArgs());
        cut.Render();

        await instance.OnDocumentVisibilityChanged(hidden: false);
        cut.Render();

        svc.IsPaused(idOne).Should().BeTrue("the hovered toast stays paused — the hold outlives the tab switch");
        svc.Toasts.Count(t => !svc.IsPaused(t.Id)).Should().Be(1, "the non-held toast resumed");
    }
}
