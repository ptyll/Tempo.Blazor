using FluentAssertions;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Editing;

/// <summary>
/// The transport-free save contract (Fáze 7, §3.2, D1/D3/D16): one request carrying the snapshot,
/// the trigger, the revision it was taken at and the editor's cancellation token, and one answer the
/// host reports through <see cref="EmailSaveStatus"/> — <c>Saved</c>, <c>Failed</c> or <c>Conflict</c>.
/// <para>
/// The editor may never claim success on its own: only <c>EmailSaveResult.Saved()</c> produces
/// <see cref="EmailSaveResult.IsSaved"/> <c>true</c>, and <c>IsSaved</c> is a named property (not a
/// constructor flag), so the host cannot construct "saved" and "failed" at the same time.
/// </para>
/// </summary>
public class EmailSaveContractTests
{
    [Fact]
    public void Trigger_HasTheFourDocumentedValues()
    {
        Enum.GetValues<EmailSaveTrigger>().Should().BeEquivalentTo(
            [EmailSaveTrigger.Manual, EmailSaveTrigger.Shortcut, EmailSaveTrigger.AutoSave, EmailSaveTrigger.Programmatic]);
    }

    [Fact]
    public void Status_HasTheThreeDocumentedValues()
    {
        Enum.GetValues<EmailSaveStatus>().Should().BeEquivalentTo(
            [EmailSaveStatus.Saved, EmailSaveStatus.Failed, EmailSaveStatus.Conflict]);
    }

    [Fact]
    public void Saved_IsTheOnlyResultThatReportsSuccess()
    {
        var saved = EmailSaveResult.Saved();
        saved.Status.Should().Be(EmailSaveStatus.Saved);
        saved.IsSaved.Should().BeTrue();
        saved.Message.Should().BeNull();
    }

    [Fact]
    public void Failed_CarriesTheHostMessage_AndIsNotSaved()
    {
        var failed = EmailSaveResult.Failed("disk full");

        failed.Status.Should().Be(EmailSaveStatus.Failed);
        failed.IsSaved.Should().BeFalse("only a confirmed host write may report success");
        failed.Message.Should().Be("disk full");
        EmailSaveResult.Failed().Message.Should().BeNull();
    }

    [Fact]
    public void Conflict_CarriesTheHostMessage_AndIsNotSaved()
    {
        var conflict = EmailSaveResult.Conflict("someone else saved first");

        conflict.Status.Should().Be(EmailSaveStatus.Conflict);
        conflict.IsSaved.Should().BeFalse();
        conflict.Message.Should().Be("someone else saved first");
        EmailSaveResult.Conflict().Message.Should().BeNull();
    }

    [Fact]
    public void Request_CarriesTheSnapshotTriggerRevisionAndToken()
    {
        var document = new EmailTemplateDocument { Subject = "Hi {{ name }}" };
        using var cts = new CancellationTokenSource();

        var request = new EmailSaveRequest(document, EmailSaveTrigger.Shortcut, 7, cts.Token);

        request.Document.Should().BeSameAs(document);
        request.Trigger.Should().Be(EmailSaveTrigger.Shortcut);
        request.Revision.Should().Be(7);
        request.CancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public void Request_IsARecord_TheHostCanDestructure()
    {
        var request = new EmailSaveRequest(new EmailTemplateDocument(), EmailSaveTrigger.AutoSave, 3, default);

        var (document, trigger, revision, cancellationToken) = request;

        document.Should().NotBeNull();
        trigger.Should().Be(EmailSaveTrigger.AutoSave);
        revision.Should().Be(3);
        cancellationToken.Should().Be(CancellationToken.None);
    }
}
