using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

public class TmRichEditorSimpleTests : LocalizationTestBase
{
    [Fact]
    public void RichEditorSimple_RendersContainer()
    {
        var cut = RenderComponent<TmRichEditorSimple>();

        cut.FindAll(".tm-rich-editor-simple").Should().NotBeEmpty();
    }

    [Fact]
    public void RichEditorSimple_RendersContentEditable()
    {
        var cut = RenderComponent<TmRichEditorSimple>();

        // Contenteditable element should exist
        var editor = cut.Find(".tm-rte-editor-content");
        editor.Should().NotBeNull();
        // When enabled, contenteditable attribute is present (may be empty or "true")
        editor.HasAttribute("contenteditable").Should().BeTrue();
    }

    [Fact]
    public void RichEditorSimple_RendersToolbar()
    {
        var cut = RenderComponent<TmRichEditorSimple>();

        cut.FindAll(".tm-rte-toolbar").Should().NotBeEmpty();
    }

    [Fact]
    public void RichEditorSimple_Placeholder_Displayed()
    {
        var cut = RenderComponent<TmRichEditorSimple>(p => p
            .Add(c => c.Placeholder, "Write something..."));

        var editor = cut.Find(".tm-rte-editor-content");
        editor.GetAttribute("data-placeholder").Should().Be("Write something...");
    }

    [Fact]
    public void RichEditorSimple_Disabled_State()
    {
        var cut = RenderComponent<TmRichEditorSimple>(p => p
            .Add(c => c.IsDisabled, true));

        var editor = cut.Find(".tm-rte-editor-content");
        // When disabled, contenteditable="false" or attribute is removed
        var value = editor.GetAttribute("contenteditable");
        (value == null || value == "false").Should().BeTrue();
    }

    [Fact]
    public void RichEditorSimple_MaxLength_ShowsCounter()
    {
        var cut = RenderComponent<TmRichEditorSimple>(p => p
            .Add(c => c.MaxLength, 200));

        cut.FindAll(".tm-rte-char-count").Should().NotBeEmpty();
    }
}
