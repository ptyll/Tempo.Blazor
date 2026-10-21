using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Templating;

/// <summary>
/// Fáze 7A (contrat §3.10, D8): <see cref="TemplateVariableInfo"/> carries an optional
/// <c>Description</c> and <c>Group</c> supplied by the HOST; variables extracted from a document stay
/// undescribed, and the two properties take part in record equality (B9).
/// </summary>
public class TemplateVariableMetadataTests
{
    [Fact]
    public void Description_and_group_default_to_null()
    {
        var info = new TemplateVariableInfo("user.first_name", VariableKind.Scalar);

        info.Description.Should().BeNull();
        info.Group.Should().BeNull();
    }

    [Fact]
    public void Description_and_group_are_init_only()
    {
        typeof(TemplateVariableInfo).GetProperty(nameof(TemplateVariableInfo.Description))!
            .SetMethod.Should().NotBeNull();
        typeof(TemplateVariableInfo).GetProperty(nameof(TemplateVariableInfo.Description))!
            .SetMethod!.ReturnParameter.GetRequiredCustomModifiers()
            .Should().Contain(typeof(System.Runtime.CompilerServices.IsExternalInit),
                "the metadata is immutable once constructed — the host builds the catalogue, nobody mutates it");
    }

    [Fact]
    public void Two_variables_with_different_descriptions_are_not_equal()
    {
        var first = new TemplateVariableInfo("user.first_name", VariableKind.Scalar) { Description = "First name" };
        var second = new TemplateVariableInfo("user.first_name", VariableKind.Scalar) { Description = "Surname" };

        first.Should().NotBe(second, "B9: Description/Group are part of Equals/GetHashCode");
        first.GetHashCode().Should().NotBe(second.GetHashCode());
    }

    [Fact]
    public void Two_variables_with_different_groups_are_not_equal()
    {
        var first = new TemplateVariableInfo("total", VariableKind.Scalar) { Group = "Order" };
        var second = new TemplateVariableInfo("total", VariableKind.Scalar) { Group = "Invoice" };

        first.Should().NotBe(second);
    }

    [Fact]
    public void Identical_metadata_is_equal()
    {
        var first = new TemplateVariableInfo("total", VariableKind.Scalar) { Description = "Total", Group = "Order" };
        var second = new TemplateVariableInfo("total", VariableKind.Scalar) { Description = "Total", Group = "Order" };

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Extracted_document_variables_carry_no_host_metadata()
    {
        var document = new EmailTemplateDocument { Subject = "Hi {{ first_name }}", Preheader = "{{ company.name }}" };

        var variables = EmailDocumentVariableExtractor.Extract(document);

        variables.Should().NotBeEmpty();
        variables.Where(v => v.Description is not null || v.Group is not null).Should().BeEmpty(
            "the document only knows paths; descriptions come from the host's catalogue");
    }
}
