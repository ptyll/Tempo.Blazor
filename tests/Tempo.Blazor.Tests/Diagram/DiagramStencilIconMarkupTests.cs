using FluentAssertions;
using Tempo.Blazor.Components.Diagram;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

public class DiagramStencilIconMarkupTests
{
    [Theory]
    // Stereotypes/captions longer than two characters are illegible in a 32–40px preview: removed.
    [InlineData("<rect/><text x='1'>&lt;&lt;artifact&gt;&gt;</text>", "<rect/>")]
    // Single-glyph markers are the icon itself and stay.
    [InlineData("<text font-size='12'>T</text>", "<text font-size='12'>T</text>")]
    // "sd" (two chars) stays, the caption goes.
    [InlineData("<text>sd</text><text>Value Proposition</text>", "<text>sd</text>")]
    public void ForPreview_StripsOnlyLongTextElements(string iconSvg, string expected)
    {
        DiagramStencilIconMarkup.ForPreview(iconSvg).Should().Be(expected);
    }
}
