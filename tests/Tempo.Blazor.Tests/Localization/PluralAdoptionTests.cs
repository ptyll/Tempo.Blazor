using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.AuditLog;
using Tempo.Blazor.Components.Chat;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.ImportExport;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// B16: counts in the document-review summary and the modeling issue panel use the grammatical
/// form of the count ("1 čekající změna", "2 čekající změny", "5 čekajících změn") instead of the
/// neutral "Čekající změny: N" stop-gap.
/// </summary>
public class PluralAdoptionTests : LocalizationTestBase
{
    [Theory]
    [InlineData(1, 1, "1 čekající změna, 1 otevřený komentář")]
    [InlineData(2, 3, "2 čekající změny, 3 otevřené komentáře")]
    [InlineData(5, 7, "5 čekajících změn, 7 otevřených komentářů")]
    public void Czech_review_summary_agrees_with_both_counts(int revisions, int comments, string expected)
    {
        UseCzechLocalization();

        var cut = Render<TmDocumentReviewSummary>(p => p
            .Add(c => c.PendingRevisionCount, revisions)
            .Add(c => c.OpenCommentCount, comments));

        cut.Find(".tm-document-review-summary__text span").TextContent.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1 čekající změna")]
    [InlineData(2, "2 čekající změny")]
    [InlineData(9, "9 čekajících změn")]
    public void Czech_review_summary_buttons_agree_with_their_count(int revisions, string expected)
    {
        UseCzechLocalization();

        var cut = Render<TmDocumentReviewSummary>(p => p
            .Add(c => c.PendingRevisionCount, revisions)
            .Add(c => c.OpenCommentCount, 1));

        cut.Find("[data-testid='document-review-summary-revisions']").TextContent.Trim().Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1 pending change, 1 open comment")]
    [InlineData(3, "3 pending changes, 2 open comments")]
    public void English_review_summary_is_singular_and_plural(int revisions, string expected)
    {
        var cut = Render<TmDocumentReviewSummary>(p => p
            .Add(c => c.PendingRevisionCount, revisions)
            .Add(c => c.OpenCommentCount, revisions == 1 ? 1 : 2));

        cut.Find(".tm-document-review-summary__text span").TextContent.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1 nález")]
    [InlineData(4, "4 nálezy")]
    [InlineData(6, "6 nálezů")]
    public void Czech_modeling_issue_header_agrees_with_the_count(int count, string expected)
    {
        UseCzechLocalization();
        var issues = Enumerable.Range(1, count)
            .Select(i => new ModelingIssueDto { Id = $"i{i}", Category = "Validation", Severity = ModelingIssueSeverity.Warning, Message = $"m{i}" })
            .ToList();

        var cut = Render<TmModelingIssuePanel>(p => p.Add(c => c.Issues, issues));

        cut.Find(".tm-modeling-issue-panel__header span").TextContent.Should().Be(expected);
    }

    // ── 449c99df: three core counts read as grammatical plural sentences ────────────────────

    private static readonly ChatUser Current = new("u1", "Alice", avatar: "A", isOnline: true);
    private static readonly ChatUser Other = new("u2", "Bob", avatar: "B", isOnline: true);

    [Theory]
    [InlineData(1, "1 odpověď")]
    [InlineData(2, "2 odpovědi")]
    [InlineData(5, "5 odpovědí")]
    [InlineData(22, "22 odpovědí")]
    public void Czech_chat_reply_link_agrees_with_the_count(int replies, string expected)
    {
        UseCzechLocalization();

        var messages = new[]
        {
            new ChatMessage("m1", "Hello", Other, ChatMessageType.Incoming) { ThreadRootId = "m1", ReplyCount = replies },
        };

        var cut = Render<TmChat>(p => p
            .Add(c => c.Messages, messages)
            .Add(c => c.CurrentUser, Current));

        cut.Find("[data-testid='chat-thread-open-m1'] span").TextContent.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1 reply")]
    [InlineData(2, "2 replies")]
    public void English_chat_reply_link_is_singular_and_plural(int replies, string expected)
    {
        var messages = new[]
        {
            new ChatMessage("m1", "Hello", Other, ChatMessageType.Incoming) { ThreadRootId = "m1", ReplyCount = replies },
        };

        var cut = Render<TmChat>(p => p
            .Add(c => c.Messages, messages)
            .Add(c => c.CurrentUser, Current));

        cut.Find("[data-testid='chat-thread-open-m1'] span").TextContent.Should().Be(expected);
    }

    private static AuditLogEntry AuditEntry(int i) => new()
    {
        Id = $"e{i}",
        Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i),
        ActorId = "alice",
        ActorName = "Alice",
        Action = "document.updated",
        EntityType = "document",
        EntityId = $"ent-{i}",
        Description = $"Change number {i}",
    };

    [Theory]
    [InlineData(1, "1 událost")]
    [InlineData(2, "2 události")]
    [InlineData(5, "5 událostí")]
    public void Czech_audit_log_count_agrees_with_the_total(int total, string expected)
    {
        UseCzechLocalization();

        var provider = new InMemoryAuditLogProvider(Enumerable.Range(1, total).Select(AuditEntry).ToArray());
        var cut = Render<TmAuditLogViewer>(p =>
        {
            p.Add(x => x.Provider, provider);
            p.Add(x => x.Virtualized, false);
        });

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='audit-log-count']").TextContent.Trim().Should().Be(expected));
    }

    private static InMemoryDataImportTarget ImportTarget()
        => new(
            [
                new ImportTargetField("name", "Name", Required: true),
                new ImportTargetField("email", "Email", Required: true),
            ],
            row => (row.Values.GetValueOrDefault("email") ?? string.Empty).Contains('@')
                ? []
                : [new DataImportRowError { RowNumber = row.RowNumber, FieldKey = "email", Message = "Invalid e-mail" }]);

    [Fact]
    public void Czech_data_import_more_errors_agrees_with_the_count()
    {
        // 102 invalid rows − 100 shown = 2 more errors → the Czech few form ("2 chyby").
        UseCzechLocalization();

        var csv = "Name,Email\n" + string.Join("\n", Enumerable.Range(1, 102).Select(i => $"R{i},bad{i}"));
        var cut = Render<TmDataImport>(p => p.Add(x => x.Target, ImportTarget()));
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(csv, "contacts.csv"));
        cut.WaitForAssertion(() => cut.Find("[data-testid='di-preview']"));
        cut.Find("[data-testid='wizard-next'] button").Click(); // → mapping (auto-mapped by header)
        cut.WaitForElement("[data-testid='di-step-mapping']");
        cut.Find("[data-testid='wizard-next'] button").Click(); // → validation (dry-run runs)
        cut.WaitForAssertion(() => cut.Find("[data-testid='di-validation-summary']"));

        cut.Find(".tm-data-import__hint").TextContent.Trim()
            .Should().Be("… a ještě 2 chyby. Kompletní report je ke stažení níže.");
    }
}