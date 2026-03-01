using Bunit;
using FluentAssertions;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Buttons;

/// <summary>TDD tests for TmCopyButton.</summary>
public class TmCopyButtonTests : LocalizationTestBase
{
    [Fact]
    public void CopyButton_Renders()
    {
        var cut = RenderComponent<TmCopyButton>(p => p
            .Add(x => x.Text, "copy me"));

        cut.Find(".tm-copy-button").Should().NotBeNull();
    }

    [Fact]
    public void CopyButton_HasAriaLabel()
    {
        var cut = RenderComponent<TmCopyButton>(p => p
            .Add(x => x.Text, "copy me"));

        cut.Find(".tm-copy-button").GetAttribute("aria-label").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CopyButton_HasCopyIcon()
    {
        var cut = RenderComponent<TmCopyButton>(p => p
            .Add(x => x.Text, "copy me"));

        // Should have an SVG icon
        cut.Find(".tm-copy-button svg").Should().NotBeNull();
    }

    [Fact]
    public void CopyButton_CustomClass_IsApplied()
    {
        var cut = RenderComponent<TmCopyButton>(p => p
            .Add(x => x.Text, "copy me")
            .Add(x => x.Class, "my-copy"));

        cut.Find(".tm-copy-button").ClassList.Should().Contain("my-copy");
    }

    [Fact]
    public void CopyButton_Click_ShowsSuccessState()
    {
        // Use bUnit's JSInterop to mock clipboard call
        JSInterop.SetupVoid("navigator.clipboard.writeText", "copy me").SetVoidResult();

        var cut = RenderComponent<TmCopyButton>(p => p
            .Add(x => x.Text, "copy me"));

        cut.Find(".tm-copy-button").Click();

        // After click, should show success state
        cut.Find(".tm-copy-button").ClassList.Should().Contain("tm-copy-button--copied");
    }
}
