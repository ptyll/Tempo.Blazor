using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// Fáze 10 security re-review (item 4, minor): the CSS rule of <see cref="CssSafety"/> is part of the MARKUP
/// guard too. A <c>style</c> block or a <c>style</c> attribute the compiler writes may not carry
/// <c>@import</c>, <c>expression(…)</c>, <c>behavior:</c> or <c>-moz-binding</c> even when its URL passes the
/// scheme allow-list (an <c>https</c> stylesheet, a <c>.htc</c> URL), and the removal has to be reported so the
/// render warns instead of silently rewriting the message.
/// </summary>
public class MarkupGuardCssSafetyTests
{
    [Theory]
    [InlineData("<style>@IMPORT url(https://x/y.css)</style>", "@import")]
    [InlineData("<style>.x{width:ex/**/pression(1)}</style>", "pression(1)")]
    [InlineData("<style>.x{behavior :url(x.htc)}</style>", "behavior")]
    [InlineData("<p style=\"width:expression(1)\">x</p>", "expression(")]
    [InlineData("<p style=\"-moz-binding:url(https://x/x.xml)\">x</p>", "-moz-binding")]
    public void ApplyToHtml_CssThePipelineRefuses_IsRemovedAndReported(string markup, string forbidden)
    {
        var result = MarkupGuard.ApplyToHtml(markup);

        result.Output.Should().NotContain(forbidden);
        result.RemovedElements.Should().Contain("style",
            "a refused CSS payload is a removal the render has to report as a warning");
    }

    [Fact]
    public void ApplyToHtml_StyleAttribute_KeepsTheUnaffectedDeclarations()
    {
        var result = MarkupGuard.ApplyToHtml("<p style=\"color:red;width:expression(1)\">x</p>");

        result.Output.Should().Contain("color:red", "only the refused declaration goes");
        result.Output.Should().NotContain("expression(");
        result.RemovedElements.Should().Contain("style");
    }

    [Theory]
    [InlineData("<style>.x{color:red;font-family:'Open Sans', Arial}</style>")]
    [InlineData("<p style=\"padding:0;color:#333\">x</p>")]
    public void ApplyToHtml_OrdinaryCss_IsUnchanged(string markup)
    {
        var result = MarkupGuard.ApplyToHtml(markup);

        result.Output.Should().Be(markup);
        result.RemovedElements.Should().BeEmpty();
    }

    [Fact]
    public void ApplyToHtml_ImportThePipelineItselfWrote_IsKept()
    {
        // The compiler turns every mj-font into a <link> PLUS this @import, from the href the generator already
        // passed through the URL allow-list; the renderer names those targets as permitted. A payload stays
        // untouched only when the import matches exactly.
        const string markup = "<style>@import url(https://f/r.css);</style>";

        var result = MarkupGuard.ApplyToHtml(markup, ["https://f/r.css"]);

        result.Output.Should().Be(markup);
        result.RemovedElements.Should().BeEmpty();
    }

    [Theory]
    [InlineData("<style>@import url(https://evil.example/x.css);</style>")]
    [InlineData("<style>@import url(https://f/r.css);@import url(https://evil.example/x.css);</style>")]
    public void ApplyToHtml_ImportOutsideThePermittedSet_IsStillRemoved(string markup)
        => MarkupGuard.ApplyToHtml(markup, ["https://f/r.css"]).Output.Should().NotContain("@import");
}
