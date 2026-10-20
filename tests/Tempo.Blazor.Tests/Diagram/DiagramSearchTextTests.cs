using FluentAssertions;
using Tempo.Blazor.Components.Diagram;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

public class DiagramSearchTextTests
{
    [Theory]
    [InlineData("sablona", "Šablona", true)]
    [InlineData("OBDELNIK", "Obdélník", true)]
    [InlineData("xyz", "Obdélník", false)]
    [InlineData("", "anything", true)]
    public void MatchesAny_FoldsCaseAndDiacritics(string query, string candidate, bool expected)
    {
        DiagramSearchText.MatchesAny(query, candidate).Should().Be(expected);
    }

    [Fact]
    public void MatchesAny_SkipsNullAndEmptyCandidates()
    {
        DiagramSearchText.MatchesAny("ab", null, "", "xabx").Should().BeTrue();
        DiagramSearchText.MatchesAny("ab", null, "").Should().BeFalse();
    }
}
