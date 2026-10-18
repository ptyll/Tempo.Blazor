using FluentAssertions;
using Tempo.Blazor.Components.Diagram.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

/// <summary>
/// The shared display-label extraction (connect dialog title, document search priority) and its
/// grapheme-safe truncation.
/// </summary>
public class DiagramNodeTextTests
{
    [Fact]
    public void GetDisplayLabel_PrefersLabelOverNameOverTextOverTitle()
    {
        var node = new DiagramNode
        {
            Data = new Dictionary<string, object>
            {
                ["label"] = "From label",
                ["name"] = "From name",
                ["text"] = "From text",
                ["title"] = "From title"
            }
        };

        Tempo.Blazor.Components.Diagram.DiagramNodeText.GetDisplayLabel(node).Should().Be("From label");

        node.Data.Remove("label");
        Tempo.Blazor.Components.Diagram.DiagramNodeText.GetDisplayLabel(node).Should().Be("From name");

        node.Data.Remove("name");
        Tempo.Blazor.Components.Diagram.DiagramNodeText.GetDisplayLabel(node).Should().Be("From text");

        node.Data.Remove("text");
        Tempo.Blazor.Components.Diagram.DiagramNodeText.GetDisplayLabel(node).Should().Be("From title");
    }

    [Fact]
    public void GetDisplayLabel_SkipsWhitespaceAndNonStringValues()
    {
        var node = new DiagramNode
        {
            Data = new Dictionary<string, object>
            {
                ["label"] = "   ",
                ["name"] = 42,
                ["title"] = "  Title  "
            }
        };

        Tempo.Blazor.Components.Diagram.DiagramNodeText.GetDisplayLabel(node).Should().Be("Title");
    }

    [Fact]
    public void GetDisplayLabel_ReadsJsonElementString()
    {
        var node = new DiagramNode
        {
            Data = new Dictionary<string, object>
            {
                ["label"] = System.Text.Json.JsonDocument.Parse("\"Json text\"").RootElement
            }
        };

        Tempo.Blazor.Components.Diagram.DiagramNodeText.GetDisplayLabel(node).Should().Be("Json text");
    }

    [Fact]
    public void GetDisplayLabel_ReturnsNullWhenTheNodeHasNoText()
    {
        var node = new DiagramNode { Data = new Dictionary<string, object> { ["count"] = 3 } };

        Tempo.Blazor.Components.Diagram.DiagramNodeText.GetDisplayLabel(node).Should().BeNull();
    }

    [Fact]
    public void Truncate_KeepsTextWithinTheLimit()
    {
        var text = new string('a', 40);

        Tempo.Blazor.Components.Diagram.DiagramNodeText.Truncate(text).Should().Be(text, "exactly 40 text elements fit without an ellipsis");
        Tempo.Blazor.Components.Diagram.DiagramNodeText.Truncate(text + "b").Should().Be(new string('a', 39) + "…");
    }

    [Fact]
    public void Truncate_NeverSplitsSurrogatePairs()
    {
        // 45 emoji (each one text element, two UTF-16 units): a char-based cut would leave a
        // lone surrogate in the middle of a pair.
        var text = string.Concat(Enumerable.Repeat("📦", 45));

        var cut = Tempo.Blazor.Components.Diagram.DiagramNodeText.Truncate(text);

        cut.Should().EndWith("…");
        CountTextElements(cut).Should().Be(40, "39 boxes plus the ellipsis");
        cut.Contains('\uD800').Should().BeFalse("no high surrogate may dangle");
    }

    [Fact]
    public void Truncate_KeepsCombiningSequencesTogether()
    {
        // "e" + combining acute accent repeated: one char-based cut would split the base letter
        // from its combining mark.
        var text = string.Concat(Enumerable.Repeat("e\u0301", 45));

        var cut = Tempo.Blazor.Components.Diagram.DiagramNodeText.Truncate(text);

        CountTextElements(cut).Should().Be(40);
        System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(cut)).Should().Be(cut,
            "the result is valid Unicode — no split combining sequence");
    }

    private static int CountTextElements(string text)
    {
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        var count = 0;
        while (enumerator.MoveNext()) count++;
        return count;
    }
}
