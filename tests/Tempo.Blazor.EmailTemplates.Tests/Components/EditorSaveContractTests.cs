using Microsoft.Extensions.Time.Testing;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Components;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7 — the save contract (§3.2, D1/D16). The editor owns <c>IsDirty</c>/<c>IsSaving</c>, asks the
/// host through <c>OnSave</c> with a deep-copy snapshot and reports exactly what the host answered:
/// "Saved" only after a confirmed write, a failed save keeps the document dirty, and a conflict pauses
/// auto-save until a successful manual/programmatic save or a new document reference.
/// </summary>
public class EditorSaveContractTests : EmailEditorTestBase
{
    [Fact]
    public void Save_HandsTheHostASnapshotWithTriggerAndRevision()
    {
        var document = DocWithTextBlock(out _);
        var calls = new List<EmailSaveRequest>();
        var cut = RenderEditor(document, EmailSaveResult.Saved(), calls.Add);

        cut.Find("[data-tm-save]").Click();

        calls.Should().ContainSingle();
        calls[0].Trigger.Should().Be(EmailSaveTrigger.Manual);
        calls[0].Revision.Should().Be(0, "nothing was edited before the first save");
        calls[0].Document.Should().NotBeSameAs(document, "the host receives a snapshot, not the live instance");
        calls[0].Document.Subject.Should().Be("Hello");
        calls[0].Document.Sections.Should().HaveCount(1);
    }

    [Fact]
    public void EditorNeverShowsSavedBeforeTheHostConfirms()
    {
        var document = DocWithTextBlock(out _);
        var gate = new TaskCompletionSource<EmailSaveResult>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.OnSave, (EmailSaveRequest _) => gate.Task));

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Find("[data-tm-save]").Click();

        BadgeState(cut).Should().Be("saving", "the in-flight save is announced, never the outcome");
        BadgeText(cut).Should().Contain("EmailEditor_SaveStatus_Saving");
        cut.Find("[data-tm-save]").GetAttribute("aria-disabled").Should().Be("true",
            "the in-flight state is announced, not enforced with `disabled` — that would drop the focus of a "
            + "keyboard user who just pressed Enter on the button");
        cut.FindAll("[data-tm-save][disabled]").Should().BeEmpty("the button stays focusable while saving");
        cut.Find("[data-tm-save-badge]").GetAttribute("data-tm-save-state").Should().NotBe("saved");

        gate.SetResult(EmailSaveResult.Saved());

        cut.WaitForAssertion(() => BadgeState(cut).Should().Be("saved"));
        cut.Instance.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task Failed_ShowsTheHostMessage_AndKeepsTheDocumentDirty()
    {
        var document = DocWithTextBlock(out _);
        var cut = RenderEditor(document, EmailSaveResult.Failed("The server rejected the template."));

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Find("[data-tm-save]").Click();

        BadgeState(cut).Should().Be("failed");
        BadgeText(cut).Should().Contain("The server rejected the template.",
            "the host's reason is what the user needs to act on");
        cut.Instance.IsDirty.Should().BeTrue("a failed save leaves the document unsaved");
        cut.Find("[data-tm-email-editor]").GetAttribute("data-tm-dirty").Should().Be("true");

        var returned = await cut.InvokeAsync(() => cut.Instance.SaveAsync());
        returned.Status.Should().Be(EmailSaveStatus.Failed);
    }

    [Fact]
    public void FailedWithoutAMessage_StillShowsALocalizedFailure()
    {
        var document = DocWithTextBlock(out _);
        var cut = RenderEditor(document, EmailSaveResult.Failed());

        cut.Find("[data-tm-save]").Click();

        BadgeState(cut).Should().Be("failed");
        BadgeText(cut).Should().Contain("EmailEditor_SaveStatus_Failed");
    }

    [Fact]
    public void Conflict_ShowsTheConflictState_AndPausesAutoSave()
    {
        var time = new FakeTimeProvider();
        var document = DocWithTextBlock(out _);
        var triggers = new List<EmailSaveTrigger>();

        // Auto-save on, and the host answers a conflict for the first request and "saved" afterwards,
        // so a request that should not happen is visible in the trigger log.
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.TimeProvider, time)
            .Add(c => c.AutoSave, new AutoSaveOptions { Enabled = true, Interval = TimeSpan.FromSeconds(2) })
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                triggers.Add(r.Trigger);
                return Task.FromResult(triggers.Count == 1
                    ? EmailSaveResult.Conflict("Revision 12 is newer.")
                    : EmailSaveResult.Saved());
            }));

        cut.Find("[data-tm-block=\"text\"]").Click();
        time.Advance(TimeSpan.FromSeconds(2));
        cut.WaitForAssertion(() => triggers.Should().ContainSingle());
        BadgeState(cut).Should().Be("conflict");
        BadgeText(cut).Should().Contain("Revision 12 is newer.");

        // Paused: an edit schedules nothing, so no further host request happens.
        cut.Find("[data-tm-block=\"button\"]").Click();
        time.Advance(TimeSpan.FromMinutes(5));
        triggers.Should().ContainSingle("a conflict pauses auto-save until it is resolved");
        BadgeState(cut).Should().Be("conflict");
    }

    [Fact]
    public void Conflict_AutosaveResumesAfterASuccessfulManualSave()
    {
        var time = new FakeTimeProvider();
        var document = DocWithTextBlock(out _);
        var triggers = new List<EmailSaveTrigger>();

        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.TimeProvider, time)
            .Add(c => c.AutoSave, new AutoSaveOptions { Enabled = true, Interval = TimeSpan.FromSeconds(2) })
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                triggers.Add(r.Trigger);
                return Task.FromResult(r.Trigger == EmailSaveTrigger.AutoSave && triggers.Count == 1
                    ? EmailSaveResult.Conflict("stale revision")
                    : EmailSaveResult.Saved());
            }));

        cut.Find("[data-tm-block=\"text\"]").Click();
        time.Advance(TimeSpan.FromSeconds(2));
        cut.WaitForAssertion(() => triggers.Should().ContainSingle());
        BadgeState(cut).Should().Be("conflict");

        // A manual save resolves the conflict (the host accepted it now).
        cut.Find("[data-tm-save]").Click();
        triggers.Should().HaveCount(2);
        BadgeState(cut).Should().Be("saved");

        // Auto-save is armed again.
        cut.Find("[data-tm-block=\"button\"]").Click();
        time.Advance(TimeSpan.FromSeconds(2));
        cut.WaitForAssertion(() => triggers.Should().HaveCount(3));
        triggers[^1].Should().Be(EmailSaveTrigger.AutoSave);
    }

    [Fact]
    public void Conflict_AutosaveResumesWhenTheHostPassesANewDocument()
    {
        var time = new FakeTimeProvider();
        var document = DocWithTextBlock(out _);
        var triggers = new List<EmailSaveTrigger>();

        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.TimeProvider, time)
            .Add(c => c.AutoSave, new AutoSaveOptions { Enabled = true, Interval = TimeSpan.FromSeconds(2) })
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                triggers.Add(r.Trigger);
                return Task.FromResult(triggers.Count == 1
                    ? EmailSaveResult.Conflict("stale revision")
                    : EmailSaveResult.Saved());
            }));

        cut.Find("[data-tm-block=\"text\"]").Click();
        time.Advance(TimeSpan.FromSeconds(2));
        cut.WaitForAssertion(() => triggers.Should().ContainSingle());
        BadgeState(cut).Should().Be("conflict");

        // The host resolved the conflict by handing over a fresh document instance.
        var reloaded = DocWithTextBlock(out _);
        cut.Render(p => p.Add(c => c.Document, reloaded).Add(c => c.TimeProvider, time)
            .Add(c => c.AutoSave, new AutoSaveOptions { Enabled = true, Interval = TimeSpan.FromSeconds(2) })
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                triggers.Add(r.Trigger);
                return Task.FromResult(triggers.Count == 1
                    ? EmailSaveResult.Conflict("stale revision")
                    : EmailSaveResult.Saved());
            }));

        cut.Instance.IsDirty.Should().BeFalse("a new document reference resets the baseline");

        cut.Find("[data-tm-block=\"text\"]").Click();
        time.Advance(TimeSpan.FromSeconds(2));
        cut.WaitForAssertion(() => triggers.Should().HaveCount(2));
        triggers[^1].Should().Be(EmailSaveTrigger.AutoSave);
    }

    [Fact]
    public void ExceptionFromOnSave_IsReportedAsFailed_AndLogged()
    {
        var document = DocWithTextBlock(out _);
        var log = new CapturingLogger<TmEmailTemplateEditor>();
        Services.AddSingleton<ILogger<TmEmailTemplateEditor>>(log);

        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.OnSave, (EmailSaveRequest _) => throw new InvalidOperationException("network down")));

        cut.Find("[data-tm-block=\"text\"]").Click();   // dirty, so a failed save must leave it dirty
        cut.Find("[data-tm-save]").Click();

        BadgeState(cut).Should().Be("failed", "an exception is a failed save, never a crash");
        cut.Instance.IsDirty.Should().BeTrue();
        log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    [Fact]
    public void HostReturningNull_IsTreatedAsFailed()
    {
        var document = DocWithTextBlock(out _);
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult<EmailSaveResult>(null!)));

        cut.Find("[data-tm-save]").Click();

        BadgeState(cut).Should().Be("failed");
    }

    [Fact]
    public async Task SaveAsync_WithoutAHost_ReturnsFailed_WithoutAnyHostCall()
    {
        var document = DocWithTextBlock(out _);
        var cut = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, document));

        var result = await cut.InvokeAsync(() => cut.Instance.SaveAsync());

        result.Status.Should().Be(EmailSaveStatus.Failed, "a save nobody handles is a failure, not a silent no-op");
        result.Message.Should().NotBeNullOrWhiteSpace("the editor explains why nothing was saved");
        BadgeState(cut).Should().Be("failed");
    }

    [Fact]
    public async Task SaveAsync_InReadOnly_ReturnsFailed_WithoutCallingTheHost()
    {
        var document = DocWithTextBlock(out _);
        var calls = 0;
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.ReadOnly, true)
            .Add(c => c.OnSave, (EmailSaveRequest _) =>
            {
                calls++;
                return Task.FromResult(EmailSaveResult.Saved());
            }));

        var result = await cut.InvokeAsync(() => cut.Instance.SaveAsync());

        result.Status.Should().Be(EmailSaveStatus.Failed);
        calls.Should().Be(0, "a read-only editor must never write through the host");
    }

    [Fact]
    public async Task SaveAsync_ReturnsTheHostResult()
    {
        var document = DocWithTextBlock(out _);
        var cut = RenderEditor(document, EmailSaveResult.Conflict("stale"));

        var result = await cut.InvokeAsync(() => cut.Instance.SaveAsync());

        result.Status.Should().Be(EmailSaveStatus.Conflict);
        result.Message.Should().Be("stale");
        result.IsSaved.Should().BeFalse();
    }

    [Fact]
    public void AutoSave_IsNotCalled_WhileTheDocumentIsClean()
    {
        var time = new FakeTimeProvider();
        var calls = 0;
        var document = DocWithTextBlock(out _);
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.TimeProvider, time)
            .Add(c => c.AutoSave, new AutoSaveOptions { Enabled = true, Interval = TimeSpan.FromSeconds(2) })
            .Add(c => c.OnSave, (EmailSaveRequest _) =>
            {
                calls++;
                return Task.FromResult(EmailSaveResult.Saved());
            }));

        time.Advance(TimeSpan.FromMinutes(5));

        calls.Should().Be(0, "auto-save with a clean document is a no-op, not a request");
    }

    [Fact]
    public void AutoSave_SendsTheAutoSaveTrigger()
    {
        var time = new FakeTimeProvider();
        var triggers = new List<EmailSaveTrigger>();
        var document = DocWithTextBlock(out _);
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.TimeProvider, time)
            .Add(c => c.AutoSave, new AutoSaveOptions { Enabled = true, Interval = TimeSpan.FromSeconds(2) })
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                triggers.Add(r.Trigger);
                return Task.FromResult(EmailSaveResult.Saved());
            }));

        cut.Find("[data-tm-block=\"text\"]").Click();
        time.Advance(TimeSpan.FromSeconds(2));

        cut.WaitForAssertion(() => triggers.Should().ContainSingle());
        triggers[0].Should().Be(EmailSaveTrigger.AutoSave);
    }

    [Fact]
    public void AutoSave_IsSkipped_InReadOnly()
    {
        var time = new FakeTimeProvider();
        var calls = 0;
        var document = DocWithTextBlock(out _);
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.ReadOnly, true)
            .Add(c => c.TimeProvider, time)
            .Add(c => c.AutoSave, new AutoSaveOptions { Enabled = true, Interval = TimeSpan.FromSeconds(2) })
            .Add(c => c.OnSave, (EmailSaveRequest _) =>
            {
                calls++;
                return Task.FromResult(EmailSaveResult.Saved());
            }));

        time.Advance(TimeSpan.FromMinutes(5));

        calls.Should().Be(0);
    }

    [Fact]
    public void CancellationToken_IsCancelledWhenTheEditorIsDisposed()
    {
        var document = DocWithTextBlock(out _);
        CancellationToken captured = default;
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                captured = r.CancellationToken;
                return Task.FromResult(EmailSaveResult.Saved());
            }));

        cut.Find("[data-tm-save]").Click();
        captured.IsCancellationRequested.Should().BeFalse("the host's I/O must be live while the editor is");

        cut.Instance.Dispose();

        captured.IsCancellationRequested.Should().BeTrue("disposing the editor cancels the in-flight host work");
    }

    /// <summary>A logger that records what the editor logged, for the "exception is failed and logged" contract.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception));

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }

    /// <summary>
    /// The user's decision (D26): Ctrl/⌘+S is an explicit save like the button, so a successful shortcut save
    /// lifts the auto-save pause a conflict put in place — the user has just proved the template can be
    /// written again.
    /// </summary>
    [Fact]
    public void Conflict_AutosaveResumesAfterASuccessfulShortcutSave()
    {
        var time = new FakeTimeProvider();
        var triggers = new List<EmailSaveTrigger>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.TimeProvider, time)
            .Add(c => c.AutoSave, new AutoSaveOptions { Enabled = true, Interval = TimeSpan.FromSeconds(2) })
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                triggers.Add(r.Trigger);
                return Task.FromResult(r.Trigger == EmailSaveTrigger.AutoSave && triggers.Count == 1
                    ? EmailSaveResult.Conflict("stale revision")
                    : EmailSaveResult.Saved());
            }));

        cut.Find("[data-tm-block=\"text\"]").Click();
        time.Advance(TimeSpan.FromSeconds(2));
        cut.WaitForAssertion(() => triggers.Should().ContainSingle());
        BadgeState(cut).Should().Be("conflict");

        // Ctrl/⌘+S resolves the conflict.
        cut.Find("[data-tm-email-editor]").KeyDown(new KeyboardEventArgs { Key = "s", CtrlKey = true });
        triggers.Should().HaveCount(2);
        triggers[^1].Should().Be(EmailSaveTrigger.Shortcut);
        BadgeState(cut).Should().Be("saved");

        // ... and auto-save is armed again.
        cut.Find("[data-tm-block=\"button\"]").Click();
        time.Advance(TimeSpan.FromSeconds(2));
        cut.WaitForAssertion(() => triggers.Should().HaveCount(3));
        triggers[^1].Should().Be(EmailSaveTrigger.AutoSave);
    }

    /// <summary>
    /// An outcome that arrives after the host replaced the document belongs to a document the editor no
    /// longer shows: it answers its caller, but it must not clear the new document's dirty flag, announce
    /// "Saved" for it, or repaint its badge.
    /// </summary>
    [Fact]
    public void ASaveThatLandsAfterTheHostReplacedTheDocument_OnlyAnswersItsCaller()
    {
        var gate = new TaskCompletionSource<EmailSaveResult>();
        var changes = new List<bool>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.IsDirtyChanged, (bool dirty) => changes.Add(dirty))
            .Add(c => c.OnSave, (EmailSaveRequest _) => gate.Task));

        cut.Find("[data-tm-block=\"text\"]").Click();          // dirty
        var pending = cut.InvokeAsync(() => cut.Instance.SaveAsync());

        // The host resolved the conflict by loading a fresh instance while the request was in flight.
        cut.Render(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.IsDirtyChanged, (bool dirty) => changes.Add(dirty))
            .Add(c => c.OnSave, (EmailSaveRequest _) => gate.Task));
        var changesAfterReload = changes.Count;
        cut.Instance.IsDirty.Should().BeFalse();

        gate.SetResult(EmailSaveResult.Saved());
        pending.Wait();

        cut.Instance.IsDirty.Should().BeFalse();
        changes.Should().HaveCount(changesAfterReload,
            "the outcome of a save against the REPLACED document is not a transition of this one");
        BadgeState(cut).Should().NotBe("saved",
            "nothing the user can see was saved — the snapshot belonged to the previous document");
    }

    /// <summary>
    /// The same rule for a conflict: the previous document's conflict must not pause the NEW document's
    /// auto-save (the user never saw that conflict on this template).
    /// </summary>
    [Fact]
    public void AConflictFromTheReplacedDocument_DoesNotPauseTheNewDocumentsAutoSave()
    {
        var time = new FakeTimeProvider();
        var gate = new TaskCompletionSource<EmailSaveResult>();
        var triggers = new List<EmailSaveTrigger>();
        Func<EmailSaveRequest, Task<EmailSaveResult>> handler = r =>
        {
            triggers.Add(r.Trigger);
            return triggers.Count == 1 ? gate.Task : Task.FromResult(EmailSaveResult.Saved());
        };

        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.TimeProvider, time)
            .Add(c => c.AutoSave, new AutoSaveOptions { Enabled = true, Interval = TimeSpan.FromSeconds(2) })
            .Add(c => c.OnSave, handler));

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Find("[data-tm-save]").Click();                       // request against document A

        cut.Render(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))         // document B
            .Add(c => c.TimeProvider, time)
            .Add(c => c.AutoSave, new AutoSaveOptions { Enabled = true, Interval = TimeSpan.FromSeconds(2) })
            .Add(c => c.OnSave, handler));

        gate.SetResult(EmailSaveResult.Conflict("revision 12 is newer"));
        cut.WaitForAssertion(() => cut.Instance.IsSaving.Should().BeFalse());

        BadgeState(cut).Should().NotBe("conflict", "the conflict belonged to the previous document");

        cut.Find("[data-tm-block=\"button\"]").Click();
        time.Advance(TimeSpan.FromSeconds(2));
        cut.WaitForAssertion(() => triggers.Should().HaveCount(2));
        triggers[^1].Should().Be(EmailSaveTrigger.AutoSave, "auto-save is not paused for the new document");
    }

    /// <summary>
    /// A handler that throws is reported as a failed save with NO detail message: the badge already says
    /// "Save failed", and repeating that sentence under the label reads as two different problems. The alert
    /// below the bar carries the hint and the retry action.
    /// </summary>
    [Fact]
    public void AnExceptionFromOnSave_LeavesTheBadgeWithoutADetail()
    {
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.OnSave, (EmailSaveRequest _) => throw new InvalidOperationException("network down")));

        cut.Find("[data-tm-save]").Click();

        BadgeState(cut).Should().Be("failed");
        cut.FindAll("[data-tm-save-badge] [data-tm-save-message]").Should().BeEmpty(
            "the label is already 'Save failed' — a second copy of it is noise, not information");
        cut.FindAll("[data-tm-save-alert]").Should().ContainSingle("the user still gets the hint and a retry");
    }

    /// <summary>
    /// A host calls <see cref="TmEmailTemplateEditor.SaveAsync"/> from its own flow — a navigation guard, a
    /// keyboard command handler, a background continuation — so it may run off the renderer's dispatcher.
    /// The save mutates component state, so it has to hop onto the dispatcher itself.
    /// </summary>
    [Fact]
    public async Task SaveAsync_CanBeCalledFromAForeignThread()
    {
        var calls = 0;
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.OnSave, (EmailSaveRequest _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(EmailSaveResult.Saved());
            }));

        var result = await Task.Run(() => cut.Instance.SaveAsync());

        result.IsSaved.Should().BeTrue();
        calls.Should().Be(1);
        cut.Instance.IsSaving.Should().BeFalse();
    }
}
