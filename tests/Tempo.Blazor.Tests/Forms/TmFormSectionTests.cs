using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Forms;

public class TmFormSectionTests : LocalizationTestBase
{
    [Fact]
    public void FormSection_RendersTitle()
    {
        var cut = RenderComponent<TmFormSection>(p => p.Add(c => c.Title, "Personal Info"));

        cut.Find(".tm-form-section-title").TextContent.Should().Contain("Personal Info");
    }

    [Fact]
    public void FormSection_RendersDescription()
    {
        var cut = RenderComponent<TmFormSection>(p => p
            .Add(c => c.Title,       "Address")
            .Add(c => c.Description, "Enter your mailing address."));

        cut.Find(".tm-form-section-desc").TextContent.Should().Contain("Enter your mailing address.");
    }

    [Fact]
    public void FormSection_RendersChildContent()
    {
        var cut = RenderComponent<TmFormSection>(p => p
            .Add(c => c.Title, "Fields")
            .AddChildContent("<input class='test-input' />"));

        cut.Find(".test-input").Should().NotBeNull();
    }

    [Fact]
    public void FormSection_Collapsible_TogglesContent()
    {
        var cut = RenderComponent<TmFormSection>(p => p
            .Add(c => c.Title,      "Collapsible")
            .Add(c => c.Collapsible, true)
            .AddChildContent("<p class='inner'>Content</p>"));

        // initially expanded
        cut.FindAll(".inner").Should().HaveCount(1);

        // click header to collapse
        cut.Find(".tm-form-section-header").Click();
        cut.FindAll(".inner").Should().BeEmpty();

        // click again to expand
        cut.Find(".tm-form-section-header").Click();
        cut.FindAll(".inner").Should().HaveCount(1);
    }
}
