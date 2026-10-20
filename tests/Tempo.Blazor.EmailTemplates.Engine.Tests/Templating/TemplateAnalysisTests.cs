using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Templating;

public class TemplateAnalysisTests
{
    private static readonly ScribanTemplateEngine Engine = new();

    [Fact]
    public void Validate_ValidTemplate_IsValid()
    {
        Engine.Validate("Hi {{ name }}").IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_SyntaxError_ReportsLineAndColumn()
    {
        var result = Engine.Validate("{{ if x }}no end");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        result.Errors[0].Line.Should().BeGreaterThan(0);
        result.Errors[0].Column.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ExtractVariables_SimpleNestedAndDedup()
    {
        var vars = Engine.ExtractVariables("{{ name }} {{ user.address.city }} {{ name }}");

        vars.Should().Contain("name");
        vars.Should().Contain("user");
        vars.Should().Contain("user.address.city");
        vars.Count(v => v == "name").Should().Be(1); // deduped
    }

    [Fact]
    public void ExtractVariables_FromConditionsAndLoops_IgnoresLoopLocal()
    {
        var vars = Engine.ExtractVariables("{{ if is_member }}{{ for item in items }}{{ item.name }}{{ end }}{{ end }}");

        vars.Should().Contain("is_member");
        vars.Should().Contain("items");
        vars.Should().NotContain("item");       // loop variable excluded
        vars.Should().NotContain("item.name");
    }

    [Fact]
    public void ExtractInfos_MarksLoopIteratorAsCollection()
    {
        var infos = ScribanTemplateVariableExtractor.Instance.ExtractInfos("{{ for o in orders }}{{ o }}{{ end }}{{ customer_name }}");

        infos.Should().Contain(i => i.Path == "orders" && i.Kind == VariableKind.Collection);
        infos.Should().Contain(i => i.Path == "customer_name" && i.Kind == VariableKind.Scalar);
    }

    [Fact]
    public void DocumentExtraction_ScansSubjectPreheaderBlocksAndRaw()
    {
        var doc = new EmailTemplateDocument
        {
            Subject = "Hello {{ first_name }}",
            Preheader = "Your order {{ order_id }}",
        };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = "Dear {{ first_name }}, thanks!", VisibleWhen = "is_member" });
        col.Blocks.Add(new EmailButtonBlock { Text = "View", Href = "https://x/{{ order_id }}" });
        col.Blocks.Add(new EmailRawBlock { Content = "<p>{{ tracking_url }}</p>" });
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var paths = EmailDocumentVariableExtractor.Extract(doc).Select(i => i.Path).ToList();

        paths.Should().Contain(new[] { "first_name", "order_id", "is_member", "tracking_url" });
    }

    [Fact]
    public void SampleDataGenerator_ProducesDataThatRendersWithoutLeftovers()
    {
        const string template = "{{ first_name }} - {{ user.email }} - {{ for o in orders }}{{ o }}{{ end }}";
        var infos = ScribanTemplateVariableExtractor.Instance.ExtractInfos(template);

        var sample = SampleDataGenerator.Generate(infos);
        var rendered = Engine.Render(template, sample);

        rendered.IsSuccess.Should().BeTrue();
        rendered.Value.Should().NotContain("{{");
        rendered.Value.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SampleDataGenerator_ScalarsSatisfyStrictRender()
    {
        const string template = "{{ first_name }} lives in {{ user.address.city }}";
        var strict = new ScribanTemplateEngine(new TemplateSecurityOptions { StrictVariables = true });

        var sample = SampleDataGenerator.Generate(ScribanTemplateVariableExtractor.Instance.ExtractInfos(template));

        strict.Render(template, sample).IsSuccess.Should().BeTrue();
    }
}
