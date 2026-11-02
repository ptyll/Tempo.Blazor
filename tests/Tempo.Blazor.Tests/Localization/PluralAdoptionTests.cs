using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Models.Diffs;
using Tempo.Blazor.Components.Approvals;
using Tempo.Blazor.Components.AuditLog;
using Tempo.Blazor.Components.Chat;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.ImportExport;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Diff;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// B16: counts in the document-review summary use the grammatical form of the count
/// ("1 čekající změna", "2 čekající změny", "5 čekajících změn") instead of the neutral
/// "Čekající změny: N" stop-gap. The modeling issue panel count was later rewritten the OTHER
/// way (Modeling redesign plan, phase 1) — its header is neutral on purpose, and this class
/// freezes that contract too.
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
    [InlineData(1, "Nálezy: 1")]
    [InlineData(4, "Nálezy: 4")]
    [InlineData(6, "Nálezy: 6")]
    public void Czech_modeling_issue_header_uses_the_neutral_count_form(int count, string expected)
    {
        // The Modeling issue count was rewritten (Modeling redesign plan, phase 1) to the neutral
        // B16 formulation: the header states the count without forcing Czech to pick a plural form.
        // Phase 2 moved that formulation onto the badge's aria-label — the visible badge text
        // is the bare count (gap #12).
        UseCzechLocalization();
        var issues = Enumerable.Range(1, count)
            .Select(i => new ModelingIssueDto { Id = $"i{i}", Category = "Validation", Severity = ModelingIssueSeverity.Warning, Message = $"m{i}" })
            .ToList();

        var cut = Render<TmModelingIssuePanel>(p => p.Add(c => c.Issues, issues));

        var badge = cut.Find("[data-testid='modeling-issue-count']");
        // Phase 2 moved the neutral formulation from the aria-label onto a visually-hidden
        // span (real content = accessible name): the visible badge text is the bare count.
        badge.QuerySelector("span[aria-hidden='true']")!.TextContent.Trim().Should()
            .Be(count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        badge.QuerySelector(".tm-sr-only")!.TextContent.Trim().Should().Be(expected);
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

    // ── Fáze 4: the TmInlineDiff bulk decision reads as a sentence ───────────────────────────────

    /// <summary>
    /// A diff with exactly <paramref name="changes"/> reviewable changes: the word "keep" between two
    /// changes stays equal, so each rewrite is its OWN hunk instead of one merged replacement.
    /// </summary>
    private static (string OldText, string NewText) DiffWithChanges(int changes)
    {
        var words = new[] { "a", "b", "c", "d", "e", "f", "g" }.Take(changes).ToArray();
        return (
            string.Join(' ', words.Select(word => "keep " + word)),
            string.Join(' ', words.Select(word => "keep " + word.ToUpperInvariant())));
    }

    private IRenderedComponent<TmInlineDiff> RenderReviewDiff(string oldText, string newText)
        => Render<TmInlineDiff>(parameters =>
        {
            parameters.Add(diff => diff.OldText, oldText);
            parameters.Add(diff => diff.NewText, newText);
            parameters.Add(diff => diff.Mode, InlineDiffMode.Review);
        });

    [Theory]
    [InlineData(1, "1 změna přijata.")]
    [InlineData(2, "Všechny 2 změny přijaty.")]
    [InlineData(3, "Všechny 3 změny přijaty.")]
    [InlineData(5, "Všech 5 změn přijato.")]
    [InlineData(7, "Všech 7 změn přijato.")]
    public void Czech_bulk_accept_agrees_with_the_count(int changes, string expected)
    {
        UseCzechLocalization();

        var (oldText, newText) = DiffWithChanges(changes);
        var cut = RenderReviewDiff(oldText, newText);

        cut.FindAll("[data-testid='inline-diff-change']").Should().HaveCount(changes,
            "the fixture must produce exactly {0} reviewable changes", changes);

        cut.Find("[data-testid='inline-diff-accept-all']").Click();

        cut.Find("[data-testid='inline-diff-announcements']").TextContent.Trim().Should().Be(expected);
    }

    [Theory]
    [InlineData(2, "Všechny 2 změny odmítnuty.")]
    [InlineData(5, "Všech 5 změn odmítnuto.")]
    public void Czech_bulk_reject_agrees_with_the_count(int changes, string expected)
    {
        UseCzechLocalization();

        var (oldText, newText) = DiffWithChanges(changes);
        var ids = TextDiff.Words(oldText, newText).Changes.Select(hunk => hunk.Id).ToList();
        ids.Should().HaveCount(changes, "the fixture must produce exactly {0} reviewable changes", changes);

        var cut = Render<TmInlineDiff>(parameters =>
        {
            parameters.Add(diff => diff.OldText, oldText);
            parameters.Add(diff => diff.NewText, newText);
            parameters.Add(diff => diff.Mode, InlineDiffMode.Review);
            parameters.Add(diff => diff.AcceptedHunkIds, ids);
        });

        cut.Find("[data-testid='inline-diff-reject-all']").Click();

        cut.Find("[data-testid='inline-diff-announcements']").TextContent.Trim().Should().Be(expected);
    }
}