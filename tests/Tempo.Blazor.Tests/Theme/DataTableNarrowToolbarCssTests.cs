using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Phase 13 (found by the 390px E2E): in a narrow table the right toolbar group
/// (Choose columns, Views, Sort, Filters, Export) is <c>flex: 0 0 auto</c>, so five controls ran
/// off the card and Sort/Filters/Export were clipped. A narrow container must let the group wrap.
/// </summary>
public sealed class DataTableNarrowToolbarCssTests
{
    private static string Css() => ThemeCss.ComponentCss("_data-table.css");

    /// <summary>The body of every at-rule block whose header matches <paramref name="header"/>, brace-matched.</summary>
    private static IEnumerable<string> Blocks(string css, string header)
    {
        foreach (Match match in Regex.Matches(css, header, RegexOptions.None, TimeSpan.FromSeconds(5)))
        {
            var depth = 1;
            var index = match.Index + match.Length;
            var start = index;
            while (index < css.Length && depth > 0)
            {
                depth += css[index] == '{' ? 1 : css[index] == '}' ? -1 : 0;
                index++;
            }

            yield return css[start..(index - 1)];
        }
    }

    [Fact]
    public void RightToolbarGroup_WrapsInANarrowContainer()
    {
        var wraps = Blocks(Css(), @"@container\s+tm-data-table\s*\(width\s*<\s*640px\)\s*\{").Any(block =>
            Regex.IsMatch(block, @"\.tm-data-table-toolbar-right\s*\{[^}]*flex-wrap:\s*wrap", RegexOptions.None, TimeSpan.FromSeconds(5)));

        wraps.Should().BeTrue(
            "the container query is where the table's own width is known; the group has to wrap there, not overflow the card");
    }

    [Fact]
    public void RightToolbarGroup_WrapsBelowTheViewportBreakpointToo()
    {
        Blocks(Css(), @"@media\s*\(width\s*<\s*768px\)\s*\{").Any(block =>
                Regex.IsMatch(block, @"\.tm-data-table-toolbar-right\s*\{[^}]*flex-wrap:\s*wrap", RegexOptions.None, TimeSpan.FromSeconds(5)))
            .Should().BeTrue("the default (non-container) table has the same five controls in a narrow viewport");
    }
}
