using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>Phase 2 / DE-2.1: the shared debug modal frame and its section element.</summary>
public sealed class TmDocumentDebugModalTests : LocalizationTestBase
{
    private static RenderFragment Section(string title, string testId, string contentTestId, string? content) => builder =>
    {
        builder.OpenComponent<TmDocumentDebugSection>(0);
        builder.AddAttribute(1, nameof(TmDocumentDebugSection.Title), title);
        builder.AddAttribute(2, nameof(TmDocumentDebugSection.SectionTestId), testId);
        builder.AddAttribute(3, nameof(TmDocumentDebugSection.ContentTestId), contentTestId);
        builder.AddAttribute(4, nameof(TmDocumentDebugSection.Content), content);
        builder.CloseComponent();
    };

    [Fact]
    public void Frame_WhenClosed_RendersNothing()
    {
        var cut = Render<TmDocumentDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, false)
            .Add(p => p.Title, "Debug")
            .Add(p => p.ModalTestId, "debug-modal"));

        cut.Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public void Frame_WhenOpen_IsAModalDialogNamedByItsTitle_WithTheRequestedTestId()
    {
        var cut = Render<TmDocumentDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Title, "Debug title")
            .Add(p => p.ModalTestId, "debug-modal"));

        var dialog = cut.Find("[data-testid='debug-modal']");
        dialog.GetAttribute("role").Should().Be("dialog");
        dialog.GetAttribute("aria-modal").Should().Be("true");
        dialog.GetAttribute("aria-label").Should().Be("Debug title");
        cut.Find(".tm-document-json-debug-modal__title").TextContent.Should().Be("Debug title");
    }

    [Fact]
    public void Frame_AppliesTheTestIdPrefix_ToItsOwnIds()
    {
        var cut = Render<TmDocumentDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Title, "Debug")
            .Add(p => p.ModalTestId, "debug-modal")
            .Add(p => p.CloseTestId, "debug-close")
            .Add(p => p.TestIdPrefix, "ed"));

        cut.FindAll("[data-testid='ed-debug-modal']").Should().ContainSingle();
        cut.FindAll("[data-testid='ed-debug-close']").Should().ContainSingle();
    }

    [Fact]
    public void Copy_IsOfferedOnlyWithADelegate()
    {
        var without = Render<TmDocumentDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Title, "Debug")
            .Add(p => p.ModalTestId, "debug-modal")
            .Add(p => p.CopyTestId, "debug-copy"));
        without.FindAll("[data-testid='debug-copy']").Should().BeEmpty();

        var copied = 0;
        var with = Render<TmDocumentDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Title, "Debug")
            .Add(p => p.ModalTestId, "debug-modal")
            .Add(p => p.CopyTestId, "debug-copy")
            .Add(p => p.OnCopy, EventCallback.Factory.Create(this, () => copied++)));
        with.Find("[data-testid='debug-copy']").GetAttribute("aria-label").Should().Be("Copy");
        with.Find("[data-testid='debug-copy']").Click();
        copied.Should().Be(1);
    }

    [Fact]
    public void Close_AndBackdrop_RaiseOnClose()
    {
        var closed = 0;
        var cut = Render<TmDocumentDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Title, "Debug")
            .Add(p => p.ModalTestId, "debug-modal")
            .Add(p => p.CloseTestId, "debug-close")
            .Add(p => p.OnClose, EventCallback.Factory.Create(this, () => closed++)));

        cut.Find("[data-testid='debug-close']").Click();
        cut.Find(".tm-document-json-debug-modal__backdrop").Click();

        closed.Should().Be(2);
    }

    [Fact]
    public void Section_WithContent_ShowsThePreAndNoEmptyNotice()
    {
        var cut = Render<TmDocumentDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Title, "Debug")
            .Add(p => p.ModalTestId, "debug-modal")
            .Add(p => p.ChildContent, Section("Canonical", "canonical", "canonical-content", """{"a":1}""")));

        cut.Find("[data-testid='canonical']").Should().NotBeNull();
        cut.Find("[data-testid='canonical-content']").TextContent.Should().Contain("\"a\"");
        cut.FindAll(".tm-document-json-debug-modal__empty").Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Section_WithEmptyContent_ShowsTheNoDataNotice_AndKeepsItsTestIds(string? content)
    {
        var cut = Render<TmDocumentDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Title, "Debug")
            .Add(p => p.ModalTestId, "debug-modal")
            .Add(p => p.ChildContent, Section("Warnings", "warnings", "warnings-content", content)));

        cut.Find("[data-testid='warnings']").QuerySelector(".tm-document-json-debug-modal__empty")!
            .TextContent.Trim().Should().Be("No data");
        cut.Find("[data-testid='warnings-content']").TextContent.Trim().Should().BeEmpty();
    }
}
