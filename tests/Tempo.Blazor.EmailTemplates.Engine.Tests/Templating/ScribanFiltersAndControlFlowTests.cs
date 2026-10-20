using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Templating;

public class ScribanFiltersAndControlFlowTests
{
    private static readonly ScribanTemplateEngine Engine = new();

    private static string Render(string template, object? model = null)
        => Engine.Render(template, model ?? new { }).Value!;

    [Theory]
    [InlineData("{{ name | string.upcase }}", "ADA")]
    [InlineData("{{ name | string.downcase }}", "ada")]
    [InlineData("{{ name | string.capitalize }}", "Ada")]
    public void StringFilters(string template, string expected)
        => Render(template, new { Name = "Ada" }).Should().Be(expected);

    [Fact]
    public void TruncateFilter_ShortensAndAppendsEllipsis()
        => Render("{{ \"Hello World\" | string.truncate 5 }}").Should().Be("He...");

    [Fact]
    public void DefaultFilter_FallsBackWhenEmpty()
        => Render("{{ name | object.default \"Guest\" }}", new { Name = "" }).Should().Be("Guest");

    [Fact]
    public void StringReplaceSplitStrip()
    {
        Render("{{ \"a-b\" | string.replace \"-\" \"+\" }}").Should().Be("a+b");
        Render("{{ (\"a,b,c\" | string.split \",\").size }}").Should().Be("3");
        Render("{{ \"  x  \" | string.strip }}").Should().Be("x");
    }

    [Fact]
    public void ArrayJoinAndSize()
    {
        var model = new { Items = new[] { "a", "b", "c" } };
        Render("{{ items | array.join \", \" }}", model).Should().Be("a, b, c");
        Render("{{ items.size }}", model).Should().Be("3");
    }

    [Fact]
    public void MathFormatAndDate()
    {
        Render("{{ 1234.5 | math.format \"N2\" }}").Should().Contain("1");
        var model = new { Created = new DateTime(2026, 6, 11) };
        Render("{{ created | date.to_string \"%Y-%m-%d\" }}", model).Should().Be("2026-06-11");
    }

    [Fact]
    public void Conditions_IfElseIfElse()
    {
        const string t = "{{ if status == \"active\" }}A{{ else if status == \"pending\" }}P{{ else }}I{{ end }}";
        Render(t, new { Status = "active" }).Should().Be("A");
        Render(t, new { Status = "pending" }).Should().Be("P");
        Render(t, new { Status = "closed" }).Should().Be("I");
    }

    [Fact]
    public void LogicalAndComparisonOperators()
    {
        Render("{{ if a > 1 && b != 2 }}yes{{ end }}", new { A = 5, B = 3 }).Should().Be("yes");
        Render("{{ if !active || count >= 10 }}show{{ end }}", new { Active = false, Count = 0 }).Should().Be("show");
    }

    [Fact]
    public void Loops_WithIndexFirstLast()
    {
        var model = new { Items = new[] { "x", "y", "z" } };
        const string t = "{{ for i in items }}{{ for.index }}:{{ i }}{{ if for.first }}(f){{ end }}{{ if for.last }}(l){{ end }};{{ end }}";
        Render(t, model).Should().Be("0:x(f);1:y;2:z(l);");
    }
}
