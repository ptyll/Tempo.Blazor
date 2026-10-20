using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Templating;

public class ScribanRenderTests
{
    private static readonly ScribanTemplateEngine Engine = new();

    [Fact]
    public void Render_SubstitutesSimpleVariable()
    {
        var result = Engine.Render("Hello {{ name }}!", new { Name = "World" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Hello World!");
    }

    [Fact]
    public void Render_AccessesPascalCaseAsSnakeCase()
    {
        var result = Engine.Render("{{ first_name }} {{ last_name }}", new { FirstName = "Ada", LastName = "Lovelace" });
        result.Value.Should().Be("Ada Lovelace");
    }

    [Fact]
    public void Render_NestedProperties()
    {
        var model = new { User = new { Address = new { City = "Prague" } } };
        Engine.Render("{{ user.address.city }}", model).Value.Should().Be("Prague");
    }

    [Fact]
    public void Render_DictionaryModel()
    {
        var model = new Dictionary<string, object?> { ["name"] = "Bob", ["age"] = 30 };
        Engine.Render("{{ name }} is {{ age }}", model).Value.Should().Be("Bob is 30");
    }

    [Fact]
    public void Render_NestedDictionaryAndList_WithIndexing()
    {
        var model = new Dictionary<string, object?>
        {
            ["items"] = new List<object?>
            {
                new Dictionary<string, object?> { ["name"] = "Apple" },
                new Dictionary<string, object?> { ["name"] = "Pear" },
            }
        };
        Engine.Render("{{ items[0].name }}-{{ items[1].name }}", model).Value.Should().Be("Apple-Pear");
    }

    [Fact]
    public void Render_MissingVariable_DefaultsToEmpty_WithWarning()
    {
        var result = Engine.Render("Hi {{ missing }}.", new { });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Hi .");
    }

    [Fact]
    public void Render_MissingVariable_StrictMode_IsError()
    {
        var strict = new ScribanTemplateEngine(new TemplateSecurityOptions { StrictVariables = true });

        var result = strict.Render("Hi {{ missing }}.", new { });

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Render_SyntaxError_ReturnsFailure_NotThrow()
    {
        var act = () => Engine.Render("{{ if x }}unclosed", new { });
        act.Should().NotThrow();
        Engine.Render("{{ if x }}unclosed", new { }).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Render_UnicodeAndDiacritics_Survive()
    {
        Engine.Render("{{ greeting }}", new { Greeting = "Příliš žluťoučký 🐎" }).Value
            .Should().Be("Příliš žluťoučký 🐎");
    }

    [Fact]
    public void Render_JsonElementModel_IteratesAndAccessesMembers()
    {
        // Models coming from JSON deserialize to JsonElement; the engine must normalize them.
        var model = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(
            "{\"name\":\"Ada\",\"items\":[{\"title\":\"A\"},{\"title\":\"B\"}]}");

        var result = Engine.Render("{{ name }}:{{ for i in items }}{{ i.title }}{{ end }}", model);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Ada:AB");
    }
}
