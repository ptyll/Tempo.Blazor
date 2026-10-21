using Microsoft.Extensions.Time.Testing;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Components;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7 — single-flight saving (D16, §3.2). At most one host request runs at a time, the button is
/// disabled while it does, a trigger accepted during a save merges into exactly one follow-up save (only
/// when the document is still dirty — otherwise no second write), and the caller of the merged request
/// receives that follow-up's result.
/// </summary>
public class EditorSingleFlightTests : EmailEditorTestBase
{
    [Fact]
    public void TheSaveButton_StaysFocusableWhileTheSaveIsInFlight_ButAnnouncesItsState()
    {
        var gate = new TaskCompletionSource<EmailSaveResult>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.OnSave, (EmailSaveRequest _) => gate.Task));

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Find("[data-tm-save]").Click();

        cut.Instance.IsSaving.Should().BeTrue();
        // A native `disabled` would blur the focused button and drop a keyboard user's focus to <body> right
        // after Enter; the single-flight queue already makes a second click harmless.
        cut.FindAll("[data-tm-save][disabled]").Should().BeEmpty();
        cut.Find("[data-tm-save]").GetAttribute("aria-disabled").Should().Be("true");

        gate.SetResult(EmailSaveResult.Saved());
        cut.WaitForAssertion(() => cut.Instance.IsSaving.Should().BeFalse());
        cut.Find("[data-tm-save]").GetAttribute("aria-disabled").Should().BeNull();
    }

    [Fact]
    public async Task ADoubleClick_SendsExactlyOneRequest()
    {
        var gate = new TaskCompletionSource<EmailSaveResult>();
        var requests = new List<EmailSaveRequest>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                requests.Add(r);
                return gate.Task;
            }));

        // Two clicks in the same frame: the second lands while the first is still awaiting the host.
        cut.Find("[data-tm-save]").Click();
        cut.Find("[data-tm-save]").Click();

        requests.Should().ContainSingle("single-flight: the second invocation joins the running save");
        gate.SetResult(EmailSaveResult.Saved());
        cut.WaitForAssertion(() => cut.Instance.IsSaving.Should().BeFalse());
        requests.Should().ContainSingle("the document was not edited, so no follow-up write is needed");
        await Task.CompletedTask;
    }

    [Fact]
    public void AShortcutDuringASave_BecomesOneFollowUpSave_WhenTheDocumentIsStillDirty()
    {
        var gate = new TaskCompletionSource<EmailSaveResult>();
        var requests = new List<EmailSaveRequest>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                requests.Add(r);
                return gate.Task;
            }));

        cut.Find("[data-tm-block=\"text\"]").Click();                                  // dirty
        cut.Find("[data-tm-email-editor]").KeyDown(new KeyboardEventArgs { Key = "s", CtrlKey = true }); // save #1
        cut.Find("[data-tm-block=\"button\"]").Click();                                // edited during the save
        cut.Find("[data-tm-email-editor]").KeyDown(new KeyboardEventArgs { Key = "s", CtrlKey = true }); // merged
        cut.Find("[data-tm-email-editor]").KeyDown(new KeyboardEventArgs { Key = "s", CtrlKey = true }); // merged

        requests.Should().ContainSingle();
        gate.SetResult(EmailSaveResult.Saved());

        // One follow-up (not one per trigger) — the two shortcuts merged into a single second write.
        cut.WaitForAssertion(() => requests.Should().HaveCount(2));
        requests[0].Trigger.Should().Be(EmailSaveTrigger.Shortcut);
        requests[1].Trigger.Should().Be(EmailSaveTrigger.Shortcut, "a shortcut outranks an auto-save in the merge");
        requests[1].Document.Sections[0].Columns[0].Blocks.Should().HaveCount(3,
            "the follow-up carries the newer snapshot (the seed block plus the two edits)");
    }

    [Fact]
    public void AnAutoSaveDuringASave_DoesNotStartASecondConcurrentRequest()
    {
        var time = new FakeTimeProvider();
        var gate = new TaskCompletionSource<EmailSaveResult>();
        var triggers = new List<EmailSaveTrigger>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.TimeProvider, time)
            .Add(c => c.AutoSave, new AutoSaveOptions { Enabled = true, Interval = TimeSpan.FromSeconds(2) })
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                triggers.Add(r.Trigger);
                return gate.Task;
            }));

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Find("[data-tm-save]").Click();          // manual save in flight
        cut.Find("[data-tm-block=\"button\"]").Click();
        time.Advance(TimeSpan.FromSeconds(2));        // auto-save fires while manual is pending

        triggers.Should().ContainSingle("auto-save must not run a parallel request");
        gate.SetResult(EmailSaveResult.Saved());

        cut.WaitForAssertion(() => triggers.Should().HaveCount(2));
        triggers[^1].Should().Be(EmailSaveTrigger.AutoSave);
    }

    [Fact]
    public async Task TheMergedCaller_ReceivesTheResultOfTheCoalescedSave()
    {
        var gate = new TaskCompletionSource<EmailSaveResult>();
        var requests = new List<EmailSaveRequest>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                requests.Add(r);
                return requests.Count == 1
                    ? gate.Task
                    : Task.FromResult(EmailSaveResult.Failed("the follow-up failed"));
            }));

        cut.Find("[data-tm-block=\"text\"]").Click();

        var first = cut.InvokeAsync(() => cut.Instance.SaveAsync());
        requests.Should().ContainSingle();

        cut.Find("[data-tm-block=\"button\"]").Click();   // still dirty → the follow-up will run
        var merged = cut.InvokeAsync(() => cut.Instance.SaveAsync());

        gate.SetResult(EmailSaveResult.Saved());

        (await first).IsSaved.Should().BeTrue("the first caller gets its own confirmed save");
        var mergedResult = await merged;
        mergedResult.Status.Should().Be(EmailSaveStatus.Failed,
            "the caller that arrived during the save is answered by the merged follow-up, not by the first result");
        requests.Should().HaveCount(2);
    }

    /// <summary>
    /// A conflict pauses auto-save. A queued auto-save that the conflict itself produced must therefore not
    /// become the second write the pause exists to prevent — the follow-up is answered with the conflict and
    /// the host is asked exactly once.
    /// </summary>
    [Fact]
    public void AQueuedAutoSaveAfterAConflict_DoesNotBecomeASecondWrite()
    {
        var time = new FakeTimeProvider();
        var gate = new TaskCompletionSource<EmailSaveResult>();
        var triggers = new List<EmailSaveTrigger>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.TimeProvider, time)
            .Add(c => c.AutoSave, new AutoSaveOptions { Enabled = true, Interval = TimeSpan.FromSeconds(2) })
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                triggers.Add(r.Trigger);
                return gate.Task;
            }));

        cut.Find("[data-tm-block=\"text\"]").Click();          // dirty
        cut.Find("[data-tm-save]").Click();                     // manual save in flight
        cut.Find("[data-tm-block=\"button\"]").Click();       // still dirty → the queued trigger runs next
        time.Advance(TimeSpan.FromSeconds(2));                  // auto-save joins the queue

        triggers.Should().ContainSingle();
        gate.SetResult(EmailSaveResult.Conflict("revision 12 is newer"));

        cut.WaitForAssertion(() => cut.Instance.IsSaving.Should().BeFalse());
        triggers.Should().ContainSingle("the queued auto-save is what the conflict pause exists to stop");
        BadgeState(cut).Should().Be("conflict");
    }

    /// <summary>
    /// The world can change while a request is in flight: a host that pins the editor read-only (or removes
    /// the save handler, or disposes it) between the two writes must be obeyed, not called a second time.
    /// </summary>
    [Fact]
    public void AFollowUpThatFindsTheEditorReadOnly_IsRefusedWithoutCallingTheHost()
    {
        var gate = new TaskCompletionSource<EmailSaveResult>();
        var triggers = new List<EmailSaveTrigger>();
        var document = DocWithTextBlock(out _);
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                triggers.Add(r.Trigger);
                return gate.Task;
            }));

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Find("[data-tm-save]").Click();                     // save in flight
        cut.Find("[data-tm-block=\"button\"]").Click();       // still dirty → a follow-up is queued

        // The host pins the editor read-only while the first write is still in flight.
        cut.Render(p => p
            .Add(c => c.Document, document)
            .Add(c => c.ReadOnly, true)
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                triggers.Add(r.Trigger);
                return gate.Task;
            }));
        cut.Instance.IsDirty.Should().BeTrue("the document was edited during the save");

        gate.SetResult(EmailSaveResult.Saved());
        cut.WaitForAssertion(() => cut.Instance.IsSaving.Should().BeFalse());
        triggers.Should().ContainSingle("a read-only editor never writes through the host");
    }
}
