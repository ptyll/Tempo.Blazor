using FluentAssertions;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Templating;

/// <summary>
/// The BCL baseline extractor that makes the variable picker and sample data work in the editor
/// without the Engine package (D11/§3.0). It understands <c>{{ path }}</c> tokens and
/// <c>for … in &lt;path&gt;</c> collections — the authoritative Scriban AST extractor lives in Engine
/// and only adds detection of filters and loop-local variables, never a security decision.
/// </summary>
public class RegexVariableExtractorTests
{
    private static ITemplateVariableExtractor Extractor => RegexTemplateVariableExtractor.Instance;

    [Fact]
    public void Extract_ReturnsTheScalarPath()
    {
        Extractor.Extract("Hello {{ customer_name }}!").Should().Equal("customer_name");
    }

    [Fact]
    public void Extract_ReportsRootAndFullPathForAMemberChain()
    {
        Extractor.Extract("{{ order.total | math.format \"N2\" }}").Should().Equal("order", "order.total");
    }

    [Fact]
    public void Extract_ReportsAForIteratorAsACollection()
    {
        var infos = Extractor.ExtractInfos("{% for item in items %}{{ item.name }}{% endfor %}");

        infos.Should().ContainSingle(i => i.Path == "items" && i.Kind == VariableKind.Collection);
        infos.Should().NotContain(i => i.Path == "item" || i.Path.StartsWith("item.", StringComparison.Ordinal),
            "a loop-local variable is not data the host has to supply");
    }

    [Fact]
    public void Extract_HandlesTheInlineForForm()
    {
        Extractor.ExtractInfos("{{ for o in orders }}{{ o }}{{ end }}")
            .Should().ContainSingle(i => i.Path == "orders" && i.Kind == VariableKind.Collection);
    }

    [Fact]
    public void Extract_IgnoresScribanKeywordsAndLiterals()
    {
        Extractor.Extract("{% if a %}{{ b }}{% else %}{{ 42 }}{{ \"lit\" }}{% end %}").Should().Equal("a", "b");
    }

    [Fact]
    public void Extract_IsDeduplicated()
    {
        Extractor.Extract("{{ a }} {{ a }} {{ a.b }} {{ a.b }}").Should().Equal("a", "a.b");
    }

    [Fact]
    public void Extract_EmptyOrMalformedInput_IsEmptyRatherThanThrowing()
    {
        Extractor.Extract(string.Empty).Should().BeEmpty();
        Extractor.Extract("{{ }}").Should().BeEmpty();
        Extractor.Extract("no tokens at all").Should().BeEmpty();
        Extractor.ExtractInfos("{%%}").Should().BeEmpty();
    }

    [Fact]
    public void EmailDocumentVariableExtractor_AcceptsAnExplicitExtractor()
    {
        var document = new Abstractions.Model.EmailTemplateDocument
        {
            Subject = "{{ subject_var }}",
            Preheader = "{{ preheader_var }}",
        };
        var column = new Abstractions.Model.EmailColumn();
        column.Blocks.Add(new Abstractions.Model.Blocks.EmailTextBlock { Content = "{{ body_var }}" });
        var section = new Abstractions.Model.EmailSection();
        section.Columns.Add(column);
        document.Sections.Add(section);

        var paths = EmailDocumentVariableExtractor.Extract(document, RegexTemplateVariableExtractor.Instance)
            .Select(i => i.Path)
            .ToList();

        paths.Should().Contain(["subject_var", "preheader_var", "body_var"]);
    }

    [Fact]
    public void EmailDocumentVariableExtractor_DefaultOverloadStillWorks()
    {
        var document = new Abstractions.Model.EmailTemplateDocument { Subject = "{{ name }}" };

        EmailDocumentVariableExtractor.Extract(document).Select(i => i.Path).Should().Equal("name");
    }
}
