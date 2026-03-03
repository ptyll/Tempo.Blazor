using FluentAssertions;
using Tempo.Blazor.Helpers;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.DataTable;

public class FilterOperatorParserTests
{
    [Theory]
    [InlineData("contains", FilterOperator.Contains)]
    [InlineData("notcontains", FilterOperator.NotContains)]
    [InlineData("equals", FilterOperator.Equals)]
    [InlineData("notequals", FilterOperator.NotEquals)]
    [InlineData("greaterthan", FilterOperator.GreaterThan)]
    [InlineData("lessthan", FilterOperator.LessThan)]
    [InlineData("greaterorequal", FilterOperator.GreaterOrEqual)]
    [InlineData("lessorequal", FilterOperator.LessOrEqual)]
    [InlineData("between", FilterOperator.Between)]
    [InlineData("isempty", FilterOperator.IsEmpty)]
    [InlineData("isnotempty", FilterOperator.IsNotEmpty)]
    [InlineData("in", FilterOperator.In)]
    [InlineData("notin", FilterOperator.NotIn)]
    public void Parse_KnownOperator_ReturnsExpected(string input, FilterOperator expected)
    {
        FilterOperatorParser.Parse(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("CONTAINS", FilterOperator.Contains)]
    [InlineData("Contains", FilterOperator.Contains)]
    [InlineData("EQUALS", FilterOperator.Equals)]
    [InlineData("NotEquals", FilterOperator.NotEquals)]
    [InlineData("GreaterThan", FilterOperator.GreaterThan)]
    [InlineData("BETWEEN", FilterOperator.Between)]
    [InlineData("IsEmpty", FilterOperator.IsEmpty)]
    [InlineData("IN", FilterOperator.In)]
    [InlineData("NotIn", FilterOperator.NotIn)]
    public void Parse_CaseInsensitive_ReturnsExpected(string input, FilterOperator expected)
    {
        FilterOperatorParser.Parse(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("foobar")]
    [InlineData("startswith")]
    [InlineData("endswith")]
    public void Parse_UnknownOperator_ReturnsContainsDefault(string input)
    {
        FilterOperatorParser.Parse(input).Should().Be(FilterOperator.Contains);
    }

    [Fact]
    public void Parse_Null_ReturnsContainsDefault()
    {
        FilterOperatorParser.Parse(null).Should().Be(FilterOperator.Contains);
    }
}
