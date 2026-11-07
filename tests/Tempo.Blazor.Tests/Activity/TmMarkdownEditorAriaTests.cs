using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

/// <summary>
/// Phase-5 review (U2): the markdown editor's content surface is a real <c>textbox</c> to assistive
/// technology and carries the four optional ARIA parameters a schema-driven host needs — the name
/// (<c>aria-labelledby</c>), the description ids (<c>aria-describedby</c>) and the
/// invalid/required state. All four default to "absent", so an existing editor renders exactly as
/// before.
/// </summary>
public sealed class TmMarkdownEditorAriaTests : LocalizationTestBase
{
    [Fact]
    public void The_content_surface_is_a_multiline_textbox()
    {
        using var cut = Render<TmMarkdownEditor>(parameters => parameters.Add(p => p.Value, "Hello"));

        var content = cut.Find(".tm-mde-content");
        content.GetAttribute("role").Should().Be("textbox");
        content.GetAttribute("aria-multiline").Should().Be("true");
        content.GetAttribute("aria-labelledby").Should().BeNull("nothing names it by default");
        content.GetAttribute("aria-describedby").Should().BeNull();
        content.GetAttribute("aria-invalid").Should().BeNull();
        content.GetAttribute("aria-required").Should().BeNull();
    }

    [Fact]
    public void The_aria_parameters_reach_the_content_surface()
    {
        using var cut = Render<TmMarkdownEditor>(parameters => parameters
            .Add(p => p.Value, "Hello")
            .Add(p => p.AriaLabelledBy, "field-label")
            .Add(p => p.AriaDescribedBy, "field-help field-error")
            .Add(p => p.AriaInvalid, true)
            .Add(p => p.AriaRequired, true));

        var content = cut.Find(".tm-mde-content");
        content.GetAttribute("aria-labelledby").Should().Be("field-label");
        content.GetAttribute("aria-describedby").Should().Be("field-help field-error");
        content.GetAttribute("aria-invalid").Should().Be("true");
        content.GetAttribute("aria-required").Should().Be("true");
    }
}
