using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

public class EmailTemplateRendererTests
{
    private static IEmailTemplateRenderer CreateRenderer()
        => new EmailTemplateRenderer(
            new ScribanTemplateEngine(), new MjmlGenerator(AngleSharpHtmlContentSanitizer.Instance), new MjmlNetCompiler(), new TextVersionGenerator());

    private static EmailTemplateDocument DocWith(string subject, string? preheader, string textContent)
    {
        var doc = new EmailTemplateDocument { Subject = subject, Preheader = preheader };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = textContent });
        section.Columns.Add(col);
        doc.Sections.Add(section);
        return doc;
    }

    [Fact]
    public async Task RenderAsync_SubstitutesVariablesInSubjectPreheaderAndBody()
    {
        var doc = DocWith("Welcome {{ first_name }}", "Order {{ order_id }}", "Hi {{ first_name }}, thanks!");
        var model = new { FirstName = "Ada", OrderId = "A-100" };

        var result = await CreateRenderer().RenderAsync(doc, model);

        result.Success.Should().BeTrue();
        result.Subject.Should().Be("Welcome Ada");
        result.Preheader.Should().Be("Order A-100");
        result.Html.Should().Contain("Hi Ada, thanks!");
        result.TextVersion.Should().Contain("Hi Ada, thanks!");
    }

    [Fact]
    public async Task RenderAsync_ResolvesVisibleWhenConditions()
    {
        var doc = new EmailTemplateDocument { Subject = "S" };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = "members only", VisibleWhen = "is_member" });
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var shown = await CreateRenderer().RenderAsync(doc, new { IsMember = true });
        var hidden = await CreateRenderer().RenderAsync(doc, new { IsMember = false });

        shown.Html.Should().Contain("members only");
        hidden.Html.Should().NotContain("members only");
    }

    [Fact]
    public async Task RenderAsync_WithoutModel_RendersWithEmptyVariables_NoCrash()
    {
        var doc = DocWith("Hi {{ name }}", null, "Dear {{ name }}");

        var result = await CreateRenderer().RenderAsync(doc, model: null);

        result.Should().NotBeNull();
        result.Subject.Should().Be("Hi ");
        result.Html.Should().Contain("Dear");
        result.Html.Should().NotContain("{{");
    }

    [Fact]
    public async Task RenderAsync_TemplateSyntaxError_IsReportedNotThrown()
    {
        var doc = DocWith("{{ if x }}broken", null, "ok");

        var result = await CreateRenderer().RenderAsync(doc, new { });

        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RenderAsync_UnicodeAndEmoji_SurviveWholePipeline()
    {
        var doc = DocWith("Příliš {{ what }} 🐎", null, "Žluťoučký {{ what }} 🦄");

        var result = await CreateRenderer().RenderAsync(doc, new { What = "kůň" });

        result.Subject.Should().Be("Příliš kůň 🐎");
        result.Html.Should().Contain("Žluťoučký kůň 🦄");
    }

    [Fact]
    public async Task RenderAsync_AcceptsPerRenderOptions_WithoutChangingTheOutput()
    {
        // B4 signature: the options slot sits in front of the cancellation token.
        var doc = DocWith("Hi {{ name }}", null, "Dear {{ name }}");

        var result = await CreateRenderer().RenderAsync(
            doc, new { Name = "Ada" }, new TemplateRenderOptions());

        result.Success.Should().BeTrue();
        result.Subject.Should().Be("Hi Ada");
        result.Html.Should().Contain("Dear Ada");
    }

    [Fact]
    public async Task NoSubstitution_EncodesCodeSpanMarkup()
    {
        // RenderWithoutSubstitutionAsync keeps the {{ … }} tokens for the HTML export. A token whose text
        // carries markup must not reach the compiled HTML as live markup: one template with
        // `{{ "<script>alert(1)</script>" }}` used to emit a real <script> element.
        var doc = DocWith(
            "Subj {{ \"<script>alert(1)</script>\" }}",
            null,
            "{{ \"<script>alert(1)</script>\" }} " +
            "{{ \"<iframe src=//evil.example/x></iframe>\" }} " +
            "{{ \"<meta http-equiv=refresh content=0>\" }}");

        var local = new EmailTemplateRenderer(
            new ScribanTemplateEngine(), new MjmlGenerator(AngleSharpHtmlContentSanitizer.Instance),
            new MjmlNetCompiler(), new TextVersionGenerator());
        var result = await local.RenderWithoutSubstitutionAsync(doc);

        result.Success.Should().BeTrue();
        result.Html.Should().NotContainEquivalentOf("<script", "the token of an unsubstituted export is inert text");
        result.Html.Should().NotContainEquivalentOf("<iframe");
        // The token text stays visible (that is what "no substitution" means), the element does not: the
        // live tags would be "<meta http-equiv=refresh" / "<meta http-equiv="refresh"".
        result.Html.Should().NotContainEquivalentOf("<meta http-equiv=refresh",
            "a meta-refresh token must not reach the export as a live element");
        result.Html.Should().NotContainEquivalentOf("<meta http-equiv=\"refresh\"");
        result.Html.Should().Contain("{{ &quot;&lt;meta http-equiv=refresh",
            "the token itself still shows the meta-refresh text, as text");
        result.Html.Should().Contain("{{ &quot;&lt;script",
            "the span survives verbatim as *text*: the token is visible, the markup is encoded");
    }

    [Fact]
    public async Task RenderAsync_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var render = () => CreateRenderer().RenderAsync(DocWith("S", null, "x"), null, cancellationToken: cts.Token);

        await render.Should().ThrowAsync<OperationCanceledException>();
    }
}
