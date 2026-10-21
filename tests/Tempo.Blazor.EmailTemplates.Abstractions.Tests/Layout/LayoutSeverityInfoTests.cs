using Tempo.Blazor.EmailTemplates.Abstractions.Layout;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Layout;

/// <summary>
/// Fáze 7A (contrat §3.10, D9/D19): <see cref="LayoutSeverity.Info"/> is a new, additive severity with a
/// frozen numeric value, an explicit display rank, and exactly ONE recommending rule — a preheader longer
/// than 100 characters. The existing values must keep the numbers they shipped with, because hosts
/// serialize findings across the API boundary.
/// </summary>
public class LayoutSeverityInfoTests
{
    [Fact]
    public void Existing_severities_keep_their_shipped_numeric_values()
    {
        ((int)LayoutSeverity.Warning).Should().Be(0,
            "hosts persist findings; re-numbering warning would silently swap it with another severity");
        ((int)LayoutSeverity.Error).Should().Be(1);
    }

    [Fact]
    public void Info_is_appended_as_value_two()
    {
        ((int)LayoutSeverity.Info).Should().Be(2);
        Enum.GetValues<LayoutSeverity>().Should().HaveCount(3);
    }

    [Fact]
    public void Rank_orders_findings_error_then_warning_then_info()
    {
        LayoutSeverityRank.Of(LayoutSeverity.Error).Should().Be(0,
            "the display order is Error, Warning, Info — never the numeric enum order");
        LayoutSeverityRank.Of(LayoutSeverity.Warning).Should().Be(1);
        LayoutSeverityRank.Of(LayoutSeverity.Info).Should().Be(2);
    }

    [Fact]
    public void Rank_is_stable_and_total()
    {
        var ranked = Enum.GetValues<LayoutSeverity>()
            .Select(severity => (Severity: severity, Rank: LayoutSeverityRank.Of(severity)))
            .OrderBy(pair => pair.Rank)
            .Select(pair => pair.Severity)
            .ToArray();

        ranked.Should().Equal(LayoutSeverity.Error, LayoutSeverity.Warning, LayoutSeverity.Info);
    }

    [Fact]
    public void Preheader_longer_than_the_limit_is_an_Info_finding()
    {
        var document = DocWithPreheader(new string('x', EmailDocumentValidator.MaxPreheaderLength + 1));

        var finding = new EmailDocumentValidator().Validate(document)
            .Should().ContainSingle(m => m.Key == DocumentValidationKeys.PreheaderTooLong).Subject;

        finding.Severity.Should().Be(LayoutSeverity.Info,
            "a long preheader is a recommendation, not a broken document");
        finding.Path.Should().Be(document.Id.ToString(), "the finding navigates to the document");
    }

    [Fact]
    public void Preheader_at_the_limit_is_not_reported()
    {
        var document = DocWithPreheader(new string('x', EmailDocumentValidator.MaxPreheaderLength));

        new EmailDocumentValidator().Validate(document)
            .Should().NotContain(m => m.Key == DocumentValidationKeys.PreheaderTooLong);
    }

    [Fact]
    public void Missing_preheader_is_not_reported_by_the_recommending_rule()
    {
        var document = DocWithPreheader(null);

        new EmailDocumentValidator().Validate(document)
            .Should().NotContain(m => m.Key == DocumentValidationKeys.PreheaderTooLong,
                "only ONE recommending rule was approved (D9) — the missing preheader is a different rule");
    }

    [Fact]
    public void The_limit_is_a_public_constant_so_a_host_can_explain_it()
    {
        EmailDocumentValidator.MaxPreheaderLength.Should().Be(100);
    }

    // ── The rule measures the SOURCE template in GRAPHEMES (review fix) ──────────────────────

    [Fact]
    public void The_rule_counts_text_elements_so_a_hundred_skin_toned_thumbs_are_at_the_limit()
    {
        // "👍🏽" is two code points (emoji + modifier) but ONE grapheme; a code-point count reported the
        // source as 200 characters long and flagged a preheader that fits.
        var preheader = string.Concat(Enumerable.Repeat("👍🏽", EmailDocumentValidator.MaxPreheaderLength));

        var findings = new EmailDocumentValidator().Validate(DocWithPreheader(preheader));

        findings.Should().NotContain(m => m.Key == DocumentValidationKeys.PreheaderTooLong,
            "100 graphemes is exactly the limit, whatever their UTF-16 length");
    }

    [Fact]
    public void One_grapheme_over_the_limit_is_reported()
    {
        var preheader = string.Concat(Enumerable.Repeat("👍🏽", EmailDocumentValidator.MaxPreheaderLength + 1));

        new EmailDocumentValidator().Validate(DocWithPreheader(preheader))
            .Should().Contain(m => m.Key == DocumentValidationKeys.PreheaderTooLong);
    }

    private static EmailTemplateDocument DocWithPreheader(string? preheader)
    {
        var document = new EmailTemplateDocument { Subject = "Hi", Preheader = preheader };
        var column = new EmailColumn();
        column.Blocks.Add(new EmailTextBlock { Content = "Hello" });
        var section = new EmailSection();
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }
}
