using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Components;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7 — <c>IsDirty</c> (D2, §3.2). The flag is owned by the editor (not a <c>[Parameter]</c>, so no
/// <c>@bind-IsDirty</c>) and is computed against the serialized snapshot of the last <b>successfully
/// saved</b> state, or of the document the host handed over. <c>IsDirtyChanged</c> fires only on a
/// transition, and an edit made while a save is in flight keeps the document dirty after success.
/// </summary>
public class EditorDirtyStateTests : EmailEditorTestBase
{
    [Fact]
    public void AFreshDocument_IsNotDirty_AndDoesNotRaiseIsDirtyChanged()
    {
        var changes = new List<bool>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.IsDirtyChanged, (bool value) => changes.Add(value))
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved())));

        cut.Instance.IsDirty.Should().BeFalse();
        changes.Should().BeEmpty("initializing is not a change");
        cut.Find("[data-tm-email-editor]").GetAttribute("data-tm-dirty").Should().Be("false");
    }

    [Fact]
    public void AnEdit_MarksTheDocumentDirty_AndRaisesIsDirtyChangedOnce()
    {
        var changes = new List<bool>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.IsDirtyChanged, (bool value) => changes.Add(value))
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved())));

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Instance.IsDirty.Should().BeTrue();
        changes.Should().Equal(true);

        cut.Find("[data-tm-block=\"button\"]").Click();
        changes.Should().Equal(new[] { true }, "still dirty is not a transition, so the callback stays silent");
        BadgeState(cut).Should().Be("dirty");
    }

    [Fact]
    public void ASuccessfulSave_ClearsDirty_AndRaisesIsDirtyChangedFalse()
    {
        var changes = new List<bool>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.IsDirtyChanged, (bool value) => changes.Add(value))
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved())));

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Find("[data-tm-save]").Click();

        cut.Instance.IsDirty.Should().BeFalse();
        changes.Should().Equal(true, false);
    }

    [Fact]
    public void AFailedSave_KeepsDirty_AndDoesNotRaiseAFalseTransition()
    {
        var changes = new List<bool>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.IsDirtyChanged, (bool value) => changes.Add(value))
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Failed("nope"))));

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Find("[data-tm-save]").Click();

        cut.Instance.IsDirty.Should().BeTrue();
        changes.Should().Equal(true);
    }

    [Fact]
    public void UndoBackToTheSavedState_MakesTheDocumentCleanAgain()
    {
        var cut = RenderEditor(DocWithTextBlock(out _), EmailSaveResult.Saved());
        cut.Find("[data-tm-save]").Click();
        cut.Instance.IsDirty.Should().BeFalse();

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Instance.IsDirty.Should().BeTrue();

        cut.Find("[data-tm-undo]").Click();

        cut.Instance.IsDirty.Should().BeFalse("the undo landed exactly on the snapshot the host confirmed");
    }

    [Fact]
    public void AnEditDuringAnInFlightSave_StaysDirtyAfterTheSaveSucceeds()
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

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Find("[data-tm-save]").Click();                 // snapshot revision 1
        cut.Find("[data-tm-block=\"button\"]").Click();     // edited while saving
        gate.SetResult(EmailSaveResult.Saved());

        cut.WaitForAssertion(() => cut.Instance.IsDirty.Should().BeTrue());
        cut.Find("[data-tm-email-editor]").GetAttribute("data-tm-dirty").Should().Be("true");
    }

    [Fact]
    public void ANewDocumentReference_ResetsDirtyBaselineAndUndoHistory()
    {
        var cut = RenderEditor(DocWithTextBlock(out _), EmailSaveResult.Saved());
        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Instance.IsDirty.Should().BeTrue();
        cut.FindAll("[data-tm-undo]:not([disabled])").Should().ContainSingle("there is an undo step to lose");

        var reloaded = DocWithTextBlock(out _);
        cut.Render(p => p.Add(c => c.Document, reloaded)
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved())));

        cut.Instance.IsDirty.Should().BeFalse();
        cut.FindAll("[data-tm-undo]:not([disabled])").Should().BeEmpty("a new reference starts a new history");
        cut.FindAll("[data-tm-redo]:not([disabled])").Should().BeEmpty();
        cut.Instance.IsSaving.Should().BeFalse();
    }

    /// <summary>
    /// A host that passes <c>Document</c> ONE WAY (no <c>DocumentChanged</c> handler) and re-renders from
    /// <c>IsDirtyChanged</c>. Its re-render hands the editor the SAME instance it handed over at the start,
    /// while the editor is working on its own undo/redo snapshot: comparing that reference with the live
    /// document would read the host's own value as "a new template" and throw the undo history away after
    /// the first undo.
    /// </summary>
    [Fact]
    public void AOneWayHost_KeepsTheHistoryAcrossUndo_AndSavesTheUndoneDocument()
    {
        var requests = new List<EmailSaveRequest>();
        var document = DocWithTextBlock(out _);

        var wrapper = Render<OneWayHost>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.Requests, requests));
        var editor = wrapper.FindComponent<TmEmailTemplateEditor>();

        editor.Find("[data-tm-block=\"text\"]").Click();
        editor.Instance.IsDirty.Should().BeTrue();

        editor.Find("[data-tm-undo]").Click();

        editor.Instance.IsDirty.Should().BeFalse("the undo landed on the state the host handed over");
        editor.FindAll("[data-tm-redo]:not([disabled])").Should().ContainSingle(
            "the host's re-render is not a reload — the redo step must survive it");

        editor.Find("[data-tm-save]").Click();
        requests.Should().ContainSingle();
        requests[0].Document.Sections[0].Columns[0].Blocks.Should().HaveCount(1,
            "the host saves the document AS UNDONE, not the pre-undo instance");
    }

    /// <summary>
    /// The same echo, but asynchronous: the host's <c>DocumentChanged</c> continues after
    /// <c>await Task.Yield()</c>, so a re-render can still see the PREVIOUS value while the editor has moved
    /// on to its own undo snapshot. A stale reference from the host is not a new document.
    /// </summary>
    [Fact]
    public void AStaleReferenceEchoedBackByTheHost_IsNotANewDocument()
    {
        var document = DocWithTextBlock(out _);
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.DocumentChanged, async (EmailTemplateDocument _) => await Task.Yield())
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved())));

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Find("[data-tm-undo]").Click();

        // The host re-renders with the instance it passed in (its continuation has not stored the undo
        // snapshot yet): the editor must recognise its own host reference and keep the history.
        cut.Render(p => p
            .Add(c => c.Document, document)
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved())));

        cut.Instance.IsDirty.Should().BeFalse();
        cut.FindAll("[data-tm-redo]:not([disabled])").Should().ContainSingle(
            "an echoed/stale reference must not reset the baseline or the history");
    }

    /// <summary>A minimal host: one-way <c>Document</c>, re-render driven by <c>IsDirtyChanged</c>.</summary>
    private sealed class OneWayHost : ComponentBase
    {
        [Parameter] public EmailTemplateDocument Document { get; set; } = new();

        [Parameter] public List<EmailSaveRequest> Requests { get; set; } = new();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmEmailTemplateEditor>(0);
            builder.AddComponentParameter(1, nameof(TmEmailTemplateEditor.Document), Document);
            builder.AddComponentParameter(2, nameof(TmEmailTemplateEditor.OnSave),
                (Func<EmailSaveRequest, Task<EmailSaveResult>>)(request =>
                {
                    Requests.Add(request);
                    return Task.FromResult(EmailSaveResult.Saved());
                }));
            // EventCallback.Create re-renders this host after the handler returns — exactly how a host that
            // only listens to IsDirtyChanged keeps its editor in sync.
            builder.AddComponentParameter(3, nameof(TmEmailTemplateEditor.IsDirtyChanged),
                EventCallback.Factory.Create<bool>(this, _ => { }));
            builder.CloseComponent();
        }
    }

    [Fact]
    public void TypingTheSavedValueBack_ByHand_MakesTheDocumentCleanAgain()
    {
        // Not an undo: the user edits the subject and then types the original text back. The dirty flag is
        // about CONTENT (it was before the history carried versions, and it must stay so): the document equals
        // what the host last confirmed, so there is nothing to save and no reason to warn on leave.
        var document = new EmailTemplateDocument { Subject = "Hello" };
        var changes = new List<bool>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.IsDirtyChanged, (bool value) => changes.Add(value))
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved())));

        cut.Find("[data-tm-prop='Subject'] input").Change("Hello!");
        cut.Instance.IsDirty.Should().BeTrue();

        cut.Find("[data-tm-prop='Subject'] input").Change("Hello");

        cut.Instance.IsDirty.Should().BeFalse("the content is the confirmed content again");
        changes.Should().Equal(true, false);
    }

    [Fact]
    public void AnEditThatChangesOnlyTheTimestamp_IsNotADirtyDocument()
    {
        // The commit stamps UpdatedAt; a timestamp is not content (the old signature cleared it too).
        var cut = RenderEditor(DocWithTextBlock(out _), EmailSaveResult.Saved());
        cut.Find("[data-tm-save]").Click();
        cut.Instance.IsDirty.Should().BeFalse();

        cut.Find("[data-tm-prop='Subject'] input").Change("Hello");   // same text as the document already has

        cut.Instance.IsDirty.Should().BeFalse();
    }
}