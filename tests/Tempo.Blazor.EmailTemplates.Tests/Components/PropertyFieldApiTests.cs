using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Components;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7A review fix — the inspector's field API is narrowed to what the renderer actually uses:
/// <c>GetChoice</c> (an alias of <c>GetText</c>) and the static <c>PropertyReflection.IsRequiredLength</c>
/// (a wrapper over the same instance member) are gone, so there is exactly one way to ask each question.
/// <c>IsNullable()</c> is the general "may be empty" predicate that both the length and the colour editors
/// share, and <c>AllowsNegative</c> is the one per-rule exception (letter spacing).
/// </summary>
public class PropertyFieldApiTests
{
    private static PropertyField Field(object target, string name)
        => PropertyReflection.GetFields(target).Single(field => field.Name == name);

    [Fact]
    public void GetChoice_is_removed_so_GetText_is_the_only_value_reader()
        => typeof(PropertyField).GetMethod("GetChoice").Should().BeNull("an alias with a second name is a trap, not API");

    [Fact]
    public void The_redundant_static_IsRequiredLength_is_removed()
        => typeof(PropertyReflection).GetMethods()
            .Select(method => method.Name)
            .Should().NotContain("IsRequiredLength");

    [Fact]
    public void IsNullable_reads_the_nullable_annotation_of_a_string_property()
    {
        Field(new EmailSection(), "BackgroundColor").IsNullable().Should().BeTrue();
        Field(new EmailTextBlock(), "Color").IsNullable().Should().BeFalse("a non-nullable string is required");
    }

    [Fact]
    public void IsNullable_is_false_for_a_value_type_property()
        => Field(new EmailTableRow(), "IsHeader").IsNullable().Should().BeFalse();

    [Fact]
    public void SetChoice_rejects_a_value_outside_the_option_set_and_keeps_the_model()
    {
        var divider = new EmailDividerBlock { BorderStyle = "solid" };
        var field = Field(divider, "BorderStyle");

        var act = () => field.SetChoice("3d");

        act.Should().Throw<ArgumentException>();
        divider.BorderStyle.Should().Be("solid");
    }

    [Fact]
    public void SetChoice_writes_a_known_value()
    {
        var divider = new EmailDividerBlock { BorderStyle = "solid" };

        Field(divider, "BorderStyle").SetChoice("dotted");

        divider.BorderStyle.Should().Be("dotted");
    }

    [Fact]
    public void SetLength_composes_and_validates_the_value()
    {
        var text = new EmailTextBlock { FontSize = "13px" };
        var field = Field(text, "FontSize");

        field.SetLength(16, "px");
        text.FontSize.Should().Be("16px");

        var act = () => field.SetLength(1, "bogus");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AllowsNegative_is_true_for_letter_spacing_only()
    {
        Field(new EmailTextBlock(), "LetterSpacing").AllowsNegative.Should().BeTrue(
            "tight tracking is a negative letter spacing");
        Field(new EmailTextBlock(), "FontSize").AllowsNegative.Should().BeFalse();
        Field(new EmailTextBlock(), "Height").AllowsNegative.Should().BeFalse();
    }
}
