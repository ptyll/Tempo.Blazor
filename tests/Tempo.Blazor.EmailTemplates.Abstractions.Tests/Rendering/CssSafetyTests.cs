using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// Fáze 10 security re-review (item 4, minor): the CSS payload rule has to read the stylesheet the way a
/// browser does. <c>ex/**&#47;*&#47;pression(1)</c> and <c>behavior :url(x.htc)</c> used to pass because the
/// rule looked for the construct in the text with a SPACE where a comment was and with the author's
/// whitespace intact. The check therefore runs on a COMPACTED copy — comments removed with an empty string and
/// every whitespace character dropped — both on the raw text and on the decoded escapes
/// (<see cref="CssNormalizer"/>), so neither a comment nor a CSS escape can hide a construct.
/// </summary>
public class CssSafetyTests
{
    [Theory]
    [InlineData(".x{width:ex/**/pression(1)}")]
    [InlineData(".x{width:ex/**/ pression (1)}")]
    [InlineData(".x{behavior :url(x.htc)}")]
    [InlineData(".x{BEHAVIOR: url(#default#time2)}")]
    [InlineData(".x{-moz-binding : url(https://x/x.xml)}")]
    [InlineData("@IMPORT url(https://x/y.css);")]
    [InlineData(".x{width:\\65 xpression(1)}")]
    [InlineData(".x{width:\\65 xpression (1)}")]
    public void IsSafe_CommentedSpacedOrEscapedPayload_IsRefused(string css)
        => CssSafety.IsSafe(css).Should().BeFalse();

    [Theory]
    [InlineData(".x{color:red}")]
    [InlineData(".x{font-family:'Open Sans', Arial}")]
    [InlineData(".x{background:url(https://example.com/a.png)}")]
    [InlineData("@media (max-width:480px){.x{padding:0}}")]
    public void IsSafe_OrdinaryStylesheet_IsAccepted(string css)
        => CssSafety.IsSafe(css).Should().BeTrue();
}
