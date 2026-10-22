using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Engine.Tests.Rendering;

/// <summary>
/// <see cref="LocalEmailPreviewRenderer"/> is the default <see cref="IEmailPreviewRenderer"/> (§3.4/D17):
/// it maps the render result onto the preview contract so the editor can tell "the template failed" from
/// "the renderer is gone", never receives a null string, keeps warnings as a successful render (D23) and
/// can render WITHOUT substituting variables for an HTML export.
/// </summary>
public class LocalEmailPreviewRendererTests
{
    private static LocalEmailPreviewRenderer Local(IEmailTemplateRenderer? renderer = null)
        => new(renderer ?? new EmailTemplateRenderer(
            new ScribanTemplateEngine(),
            new MjmlGenerator(AngleSharpHtmlContentSanitizer.Instance),
            new MjmlNetCompiler(),
            new TextVersionGenerator()), NullLogger<LocalEmailPreviewRenderer>.Instance);

    private static EmailPreviewRequest Request(
        string subject = "Hi {{ name }}",
        string content = "Hello {{ name }}",
        string? variablesJson = """{"name":"World"}""",
        bool substitute = true,
        string? cultureName = null)
        => new()
        {
            Document = RegistrationSplitTests.Document(subject, content),
            VariablesJson = variablesJson,
            SubstituteVariables = substitute,
            CultureName = cultureName,
        };

    [Fact]
    public async Task Rendered_CarriesHtmlTextSubjectAndPreheader()
    {
        var result = await Local().RenderAsync(Request());

        result.Status.Should().Be(EmailPreviewStatus.Rendered);
        result.Subject.Should().Be("Hi World");
        result.Html.Should().Contain("Hello World");
        result.Text.Should().Contain("Hello World");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task NoSubstitution_KeepsTheTemplateTokens()
    {
        var result = await Local().RenderAsync(Request(substitute: false));

        result.Status.Should().Be(EmailPreviewStatus.Rendered);
        result.Subject.Should().Be("Hi {{ name }}", "the export keeps the token");
        result.Html.Should().Contain("{{ name }}");
        result.Text.Should().Contain("{{ name }}");
    }

    [Fact]
    public async Task Warnings_KeepTheRenderSuccessful()
    {
        // A blocked URL scheme is a Warning: the HTML is complete, so the status stays Rendered (D23).
        // The URL has to come from a block attribute the sanitizer does not pre-clean (text-block HTML is
        // sanitized before the guard, so its unsafe href never reaches the allow-list).
        var document = RegistrationSplitTests.Document("S", "text");
        document.Sections[0].Columns[0].Blocks.Add(
            new Abstractions.Model.Blocks.EmailButtonBlock { Text = "Go", Href = "javascript:alert(1)" });

        var result = await Local().RenderAsync(new EmailPreviewRequest { Document = document });

        result.Status.Should().Be(EmailPreviewStatus.Rendered);
        result.Errors.Should().Contain(e => e.Severity == RenderErrorSeverity.Warning);
        result.Html.Should().NotBeEmpty();
    }

    [Fact]
    public async Task TemplateError_IsRenderFailedWithEmptyStrings()
    {
        // An unresolvable construct fails the render (never a silent empty string).
        var result = await Local().RenderAsync(Request(content: "{{ user.get_type }}"));

        result.Status.Should().Be(EmailPreviewStatus.RenderFailed);
        result.Html.Should().BeEmpty().And.NotBeNull();
        result.Text.Should().BeEmpty().And.NotBeNull();
        result.Subject.Should().BeEmpty().And.NotBeNull();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RendererFailure_IsUnavailable()
    {
        var renderer = Substitute.For<IEmailTemplateRenderer>();
        renderer.RenderAsync(
                Arg.Any<Abstractions.Model.EmailTemplateDocument>(),
                Arg.Any<object?>(),
                Arg.Any<TemplateRenderOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<RenderResult>>(_ => throw new HttpRequestException("server is down"));

        var result = await Local(renderer).RenderAsync(Request());

        result.Status.Should().Be(EmailPreviewStatus.Unavailable);
        result.Html.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Key.Should().Be(RenderKeys.PreviewUnavailable);
    }

    [Fact]
    public async Task Cancellation_IsNeverSwallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var renderer = Substitute.For<IEmailTemplateRenderer>();
        renderer.RenderAsync(
                Arg.Any<Abstractions.Model.EmailTemplateDocument>(),
                Arg.Any<object?>(),
                Arg.Any<TemplateRenderOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<RenderResult>>(_ => throw new OperationCanceledException());

        var act = () => Local(renderer).RenderAsync(Request(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "the editor cancels a superseded render and must not be told the preview is unavailable");
    }

    [Fact]
    public async Task CultureName_ReachesTheFormatter()
    {
        var result = await Local().RenderAsync(Request(content: "{{ price }}", variablesJson: """{"price":1234.5}""", cultureName: "cs-CZ"));

        result.Html.Should().Contain("1234,5");
        result.Html.Should().NotContain("1234.5");
    }

    [Fact]
    public async Task InvalidVariablesJson_DoesNotFailThePreview()
    {
        var result = await Local().RenderAsync(Request(variablesJson: "{not json"));

        result.Status.Should().Be(EmailPreviewStatus.Rendered);
        result.Html.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UnknownCulture_FallsBackInsteadOfFailing()
    {
        var result = await Local().RenderAsync(Request(content: "{{ price }}", variablesJson: """{"price":1234.5}""", cultureName: "not-a-culture"));

        result.Status.Should().Be(EmailPreviewStatus.Rendered);
        result.Html.Should().Contain("1234.5");
    }

    /// <summary>
    /// Fáze 10 review (security 5): the HTML EXPORT is the same pipeline as the preview with
    /// <see cref="EmailPreviewRequest.SubstituteVariables"/> = <see langword="false"/>, so it has to be hostile
    /// to a hostile document for the same reasons — a raw block's <c>script</c>, an imported extra
    /// <c>background</c>/<c>thumbnails-src</c> and the MJML head attributes travel through it. The assertion is
    /// on the PARSED DOM, because the words of a payload may legitimately appear inside escaped text.
    /// </summary>
    [Fact]
    public async Task HostileDocument_ExportRequest_HtmlCarriesNoScriptHandlerOrJavascriptUrl()
    {
        var document = new Abstractions.Model.EmailTemplateDocument { Name = "Hostile", Subject = "Hi {{ name }}" };
        var section = new Abstractions.Model.EmailSection();
        var column = new Abstractions.Model.EmailColumn();

        column.Blocks.Add(new Abstractions.Model.Blocks.EmailRawBlock
        {
            Content = "<script>alert(1)</script><iframe src=\"javascript:alert(2)\"></iframe><p>body</p>",
        });

        var text = new Abstractions.Model.Blocks.EmailTextBlock { Content = "text" };
        text.ExtraAttributes["background"] = "javascript:alert(3)";
        text.ExtraAttributes["onclick"] = "alert(4)";
        text.ExtraAttributes["srcdoc"] = "<script>alert(5)</script>";
        column.Blocks.Add(text);

        var carousel = new Abstractions.Model.Blocks.EmailCarouselBlock();
        carousel.Images.Add(new Abstractions.Model.Blocks.EmailCarouselImage
        {
            Src = "https://example.com/a.png",
            ThumbnailsSrc = "javascript:alert(6)",
        });
        column.Blocks.Add(carousel);

        section.Columns.Add(column);
        document.Sections.Add(section);

        var selector = new Abstractions.Model.MjHtmlSelector { Path = ".promo" };
        selector.Attributes["onclick"] = "alert(7)";
        selector.Attributes["href"] = "javascript:alert(8)";
        document.Styles.HtmlAttributes.Add(selector);
        document.Styles.Attributes.PerTag["mj-text"] = new()
        {
            ["color"] = "red;background-image:url(https://tracker)",
        };
        document.Styles.Fonts.Add(new Abstractions.Model.EmailFont { Name = "Evil", Href = "javascript:alert(9)" });

        var result = await Local().RenderAsync(new EmailPreviewRequest
        {
            Document = document,
            SubstituteVariables = false,
        });

        result.Status.Should().Be(EmailPreviewStatus.Rendered);
        var html = result.Html;
        html.Should().NotContain("<script");
        html.Should().NotContain("javascript:");
        html.Should().NotContain("url(https://tracker)");

        var parsed = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);
        parsed.QuerySelectorAll("script, iframe, frame, frameset, object, embed, applet, base, form, portal")
            .Should().BeEmpty();
        foreach (var element in parsed.All)
        {
            foreach (var attribute in element.Attributes)
            {
                attribute.Name.Should().NotStartWith("on");
                attribute.Name.Should().NotBe("srcdoc");
                attribute.Value.Should().NotContain("javascript:");
            }
        }
    }

    [Fact]
    public async Task ForeignRenderer_CannotKeepTokens_IsRenderFailedAndNeverSilentlySubstituted()
    {
        // §3.4: SubstituteVariables = false is the HTML export contract ("keep the {{ }} tokens"). A custom
        // IEmailTemplateRenderer cannot honour it, so substituting anyway would hand the host an export with
        // the tokens already resolved — silently wrong. Fail closed instead of guessing.
        var renderer = Substitute.For<IEmailTemplateRenderer>();
        renderer.RenderAsync(
                Arg.Any<Abstractions.Model.EmailTemplateDocument>(),
                Arg.Any<object?>(),
                Arg.Any<TemplateRenderOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(new RenderResult("<html>ok</html>", "subject", null, "text", []));

        var result = await Local(renderer).RenderAsync(Request(substitute: false));

        result.Status.Should().Be(EmailPreviewStatus.RenderFailed);
        result.Html.Should().BeEmpty();
        result.Text.Should().BeEmpty();
        result.Subject.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Key.Should().Be(RenderKeys.PreviewUnavailable);
        await renderer.DidNotReceive().RenderAsync(
            Arg.Any<Abstractions.Model.EmailTemplateDocument>(),
            Arg.Any<object?>(),
            Arg.Any<TemplateRenderOptions?>(),
            Arg.Any<CancellationToken>());
    }
}
