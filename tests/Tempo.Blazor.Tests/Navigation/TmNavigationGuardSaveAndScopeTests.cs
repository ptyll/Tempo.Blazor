using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Navigation;

/// <summary>
/// Tests for the phase-2 additions to TmNavigationGuard: a <c>ShouldGuard</c> predicate that scopes
/// which destinations are guarded (needed for same-document URL tabs / sub-pages) and an optional
/// third "save and leave" action.
/// </summary>
public class TmNavigationGuardSaveAndScopeTests : LocalizationTestBase
{
    private BunitNavigationManager Nav => Services.GetRequiredService<NavigationManager>() as BunitNavigationManager
        ?? throw new InvalidOperationException("BunitNavigationManager not registered.");

    [Fact]
    public void ShouldGuard_ReturnsFalse_AllowsDirtyNavigationWithoutPrompt()
    {
        var cut = Render<TmNavigationGuard>(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.ShouldGuard, uri => !uri.Contains("/loss/", StringComparison.Ordinal)));

        Nav.NavigateTo("/oprisk/123-2024/loss/new");
        cut.WaitForState(() => Nav.History.Count > 0);

        Nav.History.First().State.Should().Be(NavigationState.Succeeded);
        cut.FindAll(".tm-dialog").Should().BeEmpty();
    }

    [Fact]
    public void ShouldGuard_ReturnsTrue_StillGuardsDirtyNavigation()
    {
        var cut = Render<TmNavigationGuard>(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.ShouldGuard, _ => true));

        Nav.NavigateTo("/somewhere-else");
        cut.WaitForState(() => Nav.History.Count > 0);

        Nav.History.First().State.Should().Be(NavigationState.Prevented);
        cut.Find(".tm-dialog").Should().NotBeNull();
    }

    [Fact]
    public void SaveAndLeave_ThirdButton_NotRendered_WhenCallbackUnset()
    {
        var cut = Render<TmNavigationGuard>(p => p.Add(x => x.IsDirty, true));

        Nav.NavigateTo("/next-page");
        cut.WaitForState(() => Nav.History.Count > 0);

        // Classic two-choice confirm dialog.
        cut.FindAll(".tm-dialog-footer button").Should().HaveCount(2);
    }

    [Fact]
    public void SaveAndLeave_ThirdButton_Rendered_WhenCallbackSet()
    {
        var cut = Render<TmNavigationGuard>(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.OnSaveAndLeave, () => Task.FromResult(true))
            .Add(x => x.SaveAndLeaveText, "Save and leave"));

        Nav.NavigateTo("/next-page");
        cut.WaitForState(() => Nav.History.Count > 0);

        var buttons = cut.FindAll(".tm-dialog-footer button");
        buttons.Should().HaveCount(3);
        buttons.Should().Contain(b => b.TextContent.Contains("Save and leave"));
    }

    [Fact]
    public async Task SaveAndLeave_Click_InvokesCallbackThenReissuesNavigation()
    {
        var saved = false;
        var cut = Render<TmNavigationGuard>(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.OnSaveAndLeave, () => { saved = true; return Task.FromResult(true); })
            .Add(x => x.SaveAndLeaveText, "Save and leave"));

        Nav.NavigateTo("/next-page");
        cut.WaitForState(() => Nav.History.Count > 0);
        Nav.History.First().State.Should().Be(NavigationState.Prevented);

        var saveButton = cut.FindAll(".tm-dialog-footer button")
            .First(b => b.TextContent.Contains("Save and leave"));
        await cut.InvokeAsync(() => saveButton.Click());
        cut.WaitForState(() => Nav.History.Count > 1);

        saved.Should().BeTrue();
        Nav.History.First().State.Should().Be(NavigationState.Succeeded);
        cut.FindAll(".tm-dialog").Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAndLeave_Variant_Escape_Stays_WithoutReissuingNavigation()
    {
        var saved = false;
        var cut = Render<TmNavigationGuard>(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.OnSaveAndLeave, () => { saved = true; return Task.FromResult(true); }));

        Nav.NavigateTo("/next-page");
        cut.WaitForState(() => Nav.History.Count > 0);
        cut.Find(".tm-dialog").Should().NotBeNull();

        // Escape in the three-choice variant maps to "stay" (like the two-choice variant), not save/leave.
        await cut.InvokeAsync(() => cut.Find(".tm-dialog").KeyUp(new KeyboardEventArgs { Key = "Escape" }));

        saved.Should().BeFalse();
        Nav.History.Should().HaveCount(1, "Escape must stay on the page and not re-issue navigation");
        cut.FindAll(".tm-dialog").Should().BeEmpty();
    }

    /// <summary>
    /// A FAILED save must not leave — and per F10 (DEC-SAVE-AND-LEAVE-FAILURE) the guard's own
    /// dialog CLOSES on failure: the guard cannot know what failed or where the host wants focus,
    /// so the host's error surface takes over, signalled through <c>OnSaveAndLeaveFailed</c>.
    /// Navigation stays cancelled and the blocked destination is forgotten.
    /// </summary>
    /// <remarks>
    /// This test replaces <c>SaveAndLeave_FailedSave_StaysPut_AndKeepsTheDialogOpen</c>, which
    /// asserted exactly the pre-F10 contract the owner decision retired (dialog open, destination
    /// remembered). Rewritten to the new contract, not deleted silently.
    /// </remarks>
    [Fact]
    public async Task SaveAndLeave_FailedSave_ClosesDialog_CancelsNavigation_AndRaisesFailedCallback()
    {
        var attempts = 0;
        var failedRaised = 0;
        var cut = Render<TmNavigationGuard>(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.OnSaveAndLeave, () => { attempts++; return Task.FromResult(false); })
            .Add(x => x.OnSaveAndLeaveFailed, EventCallback.Factory.Create(this, () => failedRaised++))
            .Add(x => x.SaveAndLeaveText, "Save and leave"));

        Nav.NavigateTo("/next-page");
        cut.WaitForState(() => Nav.History.Count > 0);
        Nav.History.First().State.Should().Be(NavigationState.Prevented);

        var saveButton = cut.FindAll(".tm-dialog-footer button")
            .First(b => b.TextContent.Contains("Save and leave"));
        await cut.InvokeAsync(() => saveButton.Click());

        attempts.Should().Be(1);
        failedRaised.Should().Be(1,
            "the host must be told the save failed so it can move focus to its own error surface");
        Nav.History.Should().HaveCount(1, "a failed save must not re-issue the blocked navigation");
        Nav.History.First().State.Should().Be(NavigationState.Prevented);
        cut.FindAll(".tm-dialog").Should().BeEmpty(
            "F10: the guard's own dialog closes on failure — the host's error surface takes over");
    }

    /// <summary>
    /// F10: a failed save abandons the blocked destination — the guard does not remember it. A
    /// genuine retry is a FRESH navigation attempt, which re-opens a fresh dialog from scratch.
    /// </summary>
    /// <remarks>
    /// This test replaces <c>SaveAndLeave_RetryAfterFailure_LeavesForTheOriginalDestination</c>,
    /// which relied on <c>_pendingTargetLocation</c> surviving the failure so a second click in the
    /// SAME dialog could leave for the ORIGINAL target. Rewritten to the new contract.
    /// </remarks>
    [Fact]
    public async Task SaveAndLeave_AfterFailure_ANewNavigationAttempt_ReopensAFreshDialog()
    {
        var succeed = false;
        var cut = Render<TmNavigationGuard>(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.OnSaveAndLeave, () => Task.FromResult(succeed))
            .Add(x => x.SaveAndLeaveText, "Save and leave"));

        Nav.NavigateTo("/next-page");
        cut.WaitForState(() => Nav.History.Count > 0);
        var saveButton = cut.FindAll(".tm-dialog-footer button")
            .First(b => b.TextContent.Contains("Save and leave"));
        await cut.InvokeAsync(() => saveButton.Click());
        cut.FindAll(".tm-dialog").Should().BeEmpty("dialog closed after the failure");

        // F10: leaving is abandoned, not remembered — a genuine retry is a FRESH navigation
        // attempt, which re-opens the dialog from scratch.
        succeed = true;
        Nav.NavigateTo("/next-page");
        cut.WaitForState(() => cut.FindAll(".tm-dialog").Count > 0);
        var retryButton = cut.FindAll(".tm-dialog-footer button")
            .First(b => b.TextContent.Contains("Save and leave"));
        await cut.InvokeAsync(() => retryButton.Click());
        cut.WaitForState(() => Nav.History.Count > 2);

        // BunitNavigationManager.History records EVERY NavigateTo, most recent first — including
        // the two prevented attempts. So: [re-issue Succeeded, attempt-2 Prevented, attempt-1 Prevented].
        Nav.History.Should().HaveCount(3,
            "each blocked attempt is recorded once, and the second, successful attempt re-issues " +
            "navigation to the destination it was actually blocked from this time");
        Nav.History.First().State.Should().Be(NavigationState.Succeeded,
            "the most recent entry is the re-issued navigation after the successful save");
        Nav.History.First().Uri.Should().EndWith("/next-page");
    }

    /// <summary>
    /// <c>OnSaveAndLeaveFailed</c> is strictly a failure signal — a successful save-and-leave must
    /// never raise it.
    /// </summary>
    [Fact]
    public async Task SaveAndLeave_SuccessfulSave_DoesNotRaiseFailedCallback()
    {
        var failedRaised = 0;
        var cut = Render<TmNavigationGuard>(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.OnSaveAndLeave, () => Task.FromResult(true))
            .Add(x => x.OnSaveAndLeaveFailed, EventCallback.Factory.Create(this, () => failedRaised++))
            .Add(x => x.SaveAndLeaveText, "Save and leave"));

        Nav.NavigateTo("/next-page");
        cut.WaitForState(() => Nav.History.Count > 0);

        var saveButton = cut.FindAll(".tm-dialog-footer button")
            .First(b => b.TextContent.Contains("Save and leave"));
        await cut.InvokeAsync(() => saveButton.Click());
        cut.WaitForState(() => Nav.History.Count > 1);

        failedRaised.Should().Be(0, "a successful save raises no failure callback");
        Nav.History.First().State.Should().Be(NavigationState.Succeeded,
            "History is most-recent-first: the re-issued navigation after the save succeeded");
        cut.FindAll(".tm-dialog").Should().BeEmpty();
    }

    /// <summary>
    /// A thrown <c>OnSaveAndLeave</c> is still a FAILED save for the guard's dialog contract: the
    /// same close-on-failure path a <c>false</c> return runs must execute — dialog closed, blocked
    /// destination forgotten, <c>OnSaveAndLeaveFailed</c> raised. The pre-carry-forward code left
    /// the dialog open here, inconsistent with the <c>false</c> path (20C UX review). The exception
    /// itself keeps propagating — an escaped exception is the error boundary's report, not this
    /// component's to swallow.
    /// </summary>
    [Fact]
    public async Task SaveAndLeave_ThrownException_RunsTheFailurePath_AndStillPropagates()
    {
        var failedRaised = 0;
        var cut = Render<TmNavigationGuard>(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.OnSaveAndLeave, () => Task.FromException<bool>(new InvalidOperationException("save blew up")))
            .Add(x => x.OnSaveAndLeaveFailed, EventCallback.Factory.Create(this, () => failedRaised++))
            .Add(x => x.SaveAndLeaveText, "Save and leave"));

        Nav.NavigateTo("/next-page");
        cut.WaitForState(() => Nav.History.Count > 0);

        var saveButton = cut.FindAll(".tm-dialog-footer button")
            .First(b => b.TextContent.Contains("Save and leave"));
        var act = async () => await cut.InvokeAsync(() => saveButton.Click());

        await act.Should().ThrowAsync<Exception>(
            "an escaped save exception is the error boundary's report — the guard runs the " +
            "failure path, it does not swallow it");

        failedRaised.Should().Be(1,
            "a thrown save is a failed save — the host's error surface must still be signalled");
        Nav.History.Should().HaveCount(1, "the blocked navigation stays cancelled");
        Nav.History.First().State.Should().Be(NavigationState.Prevented);
        cut.FindAll(".tm-dialog").Should().BeEmpty(
            "close-on-failure applies to a thrown save exactly as to a false return");
    }

    /// <summary>
    /// While a save is in flight the whole footer must be inert: Leave would abandon the in-flight
    /// write (the navigation it confirms resolves the pending target the save still needs), and
    /// Stay would dismiss the dialog the save still reports through (20C UX review residual —
    /// only the Save button carried <c>Disabled="@_saving"</c>).
    /// </summary>
    [Fact]
    public async Task SaveAndLeave_DuringInFlightSave_AllDialogActionsAreDisabled()
    {
        var gate = new TaskCompletionSource<bool>();
        var cut = Render<TmNavigationGuard>(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.OnSaveAndLeave, () => gate.Task)
            .Add(x => x.SaveAndLeaveText, "Save and leave")
            .Add(x => x.CancelText, "Stay")
            .Add(x => x.ConfirmLeaveText, "Leave"));

        Nav.NavigateTo("/next-page");
        cut.WaitForState(() => Nav.History.Count > 0);

        var saveButton = cut.FindAll(".tm-dialog-footer button")
            .First(b => b.TextContent.Contains("Save and leave"));
        var first = cut.InvokeAsync(() => saveButton.Click());

        cut.WaitForAssertion(() => cut.FindAll(".tm-dialog-footer button")
            .Should().OnlyContain(b => b.HasAttribute("disabled"),
                "an in-flight save owns the dialog — Leave must not abandon the pending write, " +
                "Stay must not dismiss the dialog the save still reports through"));

        gate.SetResult(true);
        await first;
        cut.WaitForState(() => Nav.History.Count > 1);
        Nav.History.First().State.Should().Be(NavigationState.Succeeded);
    }

    /// <summary>
    /// A second click while the first save is still in flight must not start a second save. The dialog
    /// stays up during the save, so the button stays there to be clicked.
    /// </summary>
    [Fact]
    public async Task SaveAndLeave_SecondClickDuringSave_DoesNotStartASecondSave()
    {
        var gate = new TaskCompletionSource<bool>();
        var attempts = 0;
        var cut = Render<TmNavigationGuard>(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.OnSaveAndLeave, () => { attempts++; return gate.Task; })
            .Add(x => x.SaveAndLeaveText, "Save and leave"));

        Nav.NavigateTo("/next-page");
        cut.WaitForState(() => Nav.History.Count > 0);

        var saveButton = cut.FindAll(".tm-dialog-footer button")
            .First(b => b.TextContent.Contains("Save and leave"));
        var first = cut.InvokeAsync(() => saveButton.Click());
        await cut.InvokeAsync(() => cut.FindAll(".tm-dialog-footer button")
            .First(b => b.TextContent.Contains("Save and leave")).Click());

        attempts.Should().Be(1, "the in-flight save must swallow the second click, not duplicate the write");

        gate.SetResult(true);
        await first;
        cut.WaitForState(() => Nav.History.Count > 1);
        Nav.History.First().State.Should().Be(NavigationState.Succeeded);
    }
}
