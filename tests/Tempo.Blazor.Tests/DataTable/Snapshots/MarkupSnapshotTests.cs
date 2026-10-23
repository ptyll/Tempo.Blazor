namespace Tempo.Blazor.Tests.DataTable.Snapshots;

/// <summary>Pins the void-element canonicalisation of <see cref="MarkupSnapshot"/>.</summary>
public class MarkupSnapshotTests
{
    [Fact]
    public void NormalizeVoidElements_QuotedGreaterThanInAttribute_DoesNotEndTheTagEarly()
        => MarkupSnapshot.NormalizeVoidElements("<input value=\"a>b\"></input>")
            .Should().Be("<input value=\"a>b\" />");

    [Fact]
    public void NormalizeVoidElements_SingleQuotedGreaterThanInAttribute_IsKept()
        => MarkupSnapshot.NormalizeVoidElements("<input value='a>b'>")
            .Should().Be("<input value='a>b' />");

    [Theory]
    [InlineData("<br>", "<br />")]
    [InlineData("<br/>", "<br />")]
    [InlineData("<br />", "<br />")]
    [InlineData("<br></br>", "<br />")]
    [InlineData("<input type=\"text\"></input>", "<input type=\"text\" />")]
    public void NormalizeVoidElements_WritesOneCanonicalForm(string markup, string expected)
        => MarkupSnapshot.NormalizeVoidElements(markup).Should().Be(expected);

    [Theory]
    [InlineData("<colgroup><col></colgroup>", "<colgroup><col /></colgroup>")]
    [InlineData("<colgroup>", "<colgroup>")]
    [InlineData("<link-card>", "<link-card>")]
    [InlineData("<link-card href=\"x\"></link-card>", "<link-card href=\"x\"></link-card>")]
    [InlineData("<input-group></input-group>", "<input-group></input-group>")]
    public void NormalizeVoidElements_TagNamesThatMerelyStartWithAVoidName_AreUntouched(string markup, string expected)
        => MarkupSnapshot.NormalizeVoidElements(markup).Should().Be(expected);
}
