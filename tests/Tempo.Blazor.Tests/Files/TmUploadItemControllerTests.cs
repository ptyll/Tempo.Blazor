using FluentAssertions;
using Tempo.Blazor.Components.Files;

namespace Tempo.Blazor.Tests.Files;

/// <summary>
/// The public upload controller of <see cref="TmUploadItem"/>: a host that tracks uploads itself
/// (no <see cref="TmAttachmentManager"/> in the tree) cancels and resumes through the item —
/// one cancellation token, one OnCancel/OnResume callback per item — instead of keeping a
/// parallel CTS / tracker dictionary of its own.
/// </summary>
public class TmUploadItemControllerTests
{
    [Fact]
    public void CancellationToken_IsNotCancelled_WhenIdle()
    {
        var item = new TmUploadItem();

        item.CancellationToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task CancelAsync_CancelsToken_SetsCancelledState_RaisesOnCancel()
    {
        var item = new TmUploadItem { State = TmUploadState.Uploading };
        item.Cts = new CancellationTokenSource();  // the running attempt's CTS, as components assign it
        TmUploadItem? reported = null;
        item.OnCancel = it => { reported = it; return Task.CompletedTask; };

        await item.CancelAsync();

        item.CancellationToken.IsCancellationRequested.Should().BeTrue();
        item.State.Should().Be(TmUploadState.Cancelled);
        item.CanResume.Should().BeTrue();
        reported.Should().BeSameAs(item);
    }

    [Fact]
    public async Task CancelAsync_IsIdempotent_AndRaisesOnCancelOnce()
    {
        var item = new TmUploadItem { State = TmUploadState.Uploading };
        var calls = 0;
        item.OnCancel = _ => { calls++; return Task.CompletedTask; };

        await item.CancelAsync();
        await item.CancelAsync();

        calls.Should().Be(1);
    }

    [Fact]
    public async Task CancelAsync_WithoutCallback_DoesNotThrow()
    {
        var item = new TmUploadItem();

        Func<Task> act = () => item.CancelAsync();

        await act.Should().NotThrowAsync();
        item.State.Should().Be(TmUploadState.Cancelled);
    }

    [Fact]
    public async Task ResumeAsync_RaisesOnResume()
    {
        var item = new TmUploadItem { State = TmUploadState.Cancelled };
        TmUploadItem? reported = null;
        item.OnResume = it => { reported = it; return Task.CompletedTask; };

        await item.ResumeAsync();

        reported.Should().BeSameAs(item);
    }

    [Fact]
    public async Task CancelAsync_OnCompleted_IsNoOp()
    {
        var item = new TmUploadItem { State = TmUploadState.Completed };
        var cancelCalls = 0;
        item.OnCancel = _ => { cancelCalls++; return Task.CompletedTask; };

        await item.CancelAsync();

        item.State.Should().Be(TmUploadState.Completed, "a finished upload keeps its state");
        cancelCalls.Should().Be(0, "OnCancel is not raised for a finished upload");
        item.CancellationToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task CancelAsync_OnBlocked_IsNoOp()
    {
        var item = new TmUploadItem { State = TmUploadState.Blocked };

        await item.CancelAsync();

        item.State.Should().Be(TmUploadState.Blocked);
        item.CancellationToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task CancelAsync_WithoutRunningAttempt_DoesNotCreateCts_JustToCancelIt()
    {
        var item = new TmUploadItem();

        await item.CancelAsync();

        item.State.Should().Be(TmUploadState.Cancelled);
        item.CancellationToken.IsCancellationRequested.Should().BeFalse(
            "no CTS is minted when no attempt is running");
    }

    [Fact]
    public async Task ResumeAsync_OnFinished_IsNoOp()
    {
        var item = new TmUploadItem { State = TmUploadState.Completed };
        var resumeCalls = 0;
        item.OnResume = _ => { resumeCalls++; return Task.CompletedTask; };

        await item.ResumeAsync();

        item.State.Should().Be(TmUploadState.Completed);
        resumeCalls.Should().Be(0, "OnResume is not raised when the upload is not resumable");
    }

    [Fact]
    public async Task ResumeAsync_ClearsCancelFlag_SoNextCancelRaisesOnCancelAgain()
    {
        var item = new TmUploadItem { State = TmUploadState.Uploading };
        var cancelCalls = 0;
        item.OnCancel = _ => { cancelCalls++; return Task.CompletedTask; };

        await item.CancelAsync();
        item.State = TmUploadState.Failed;  // the component's resume action reclassified the attempt
        await item.ResumeAsync();
        item.State = TmUploadState.Uploading;
        await item.CancelAsync();

        cancelCalls.Should().Be(2, "each cancel cycle raises OnCancel once");
    }

    [Fact]
    public void CancellationToken_AfterDismiss_DoesNotThrow()
    {
        var item = new TmUploadItem { State = TmUploadState.Uploading };
        item.Cts = new CancellationTokenSource();

        // DismissUpload releases the CTS through the setter (which disposes it); the public
        // getter must fall back to CancellationToken.None instead of touching a disposed CTS.
        item.Cts = null;

        Func<CancellationToken> act = () => item.CancellationToken;
        act.Should().NotThrow();
        item.CancellationToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void BeginAttempt_CancelsPreviousCts_AndReturnsFreshToken()
    {
        var item = new TmUploadItem();
        var first = item.BeginAttempt();
        first.IsCancellationRequested.Should().BeFalse();

        var second = item.BeginAttempt();

        first.IsCancellationRequested.Should().BeTrue(
            "BeginAttempt must cancel the previous attempt before replacing the CTS");
        second.IsCancellationRequested.Should().BeFalse();
        second.Should().NotBe(first);
    }

    [Fact]
    public async Task ResumeAction_RunsBeforeOnResume()
    {
        var item = new TmUploadItem { State = TmUploadState.Cancelled };
        var order = new List<string>();
        item.ResumeAction = _ =>
        {
            order.Add("action");
            item.State = TmUploadState.Uploading;
            return Task.CompletedTask;
        };
        item.OnResume = _ =>
        {
            order.Add("onresume");
            return Task.CompletedTask;
        };

        await item.ResumeAsync();

        order.Should().Equal("action", "onresume");
        item.State.Should().Be(TmUploadState.Uploading);
    }

    [Fact]
    public void Dispose_ReleasesCts_SoCancellationTokenFallsBackToNone()
    {
        var item = new TmUploadItem();
        item.BeginAttempt();

        item.Dispose();

        item.CancellationToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public void Dispose_CancelsInFlightAttempt_SoHoldersSeeCancellationNotObjectDisposed()
    {
        var item = new TmUploadItem();
        var token = item.BeginAttempt();

        item.Dispose();

        token.IsCancellationRequested.Should().BeTrue(
            "Dispose must Cancel before Dispose so a token holder sees cancellation, not ObjectDisposedException");
    }
}
