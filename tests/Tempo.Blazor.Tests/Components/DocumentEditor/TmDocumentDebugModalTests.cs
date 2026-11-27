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

        // Phase 5 (G6): the frame is the core TmModal - the test id rides the overlay (the scrim), the dialog is the focus scope inside it
        // and is named by the visible title through aria-labelledby.
        var overlay = cut.Find("[data-testid='debug-modal']");
        overlay.ClassList.Should().Contain("tm-modal-overlay");
        var dialog = overlay.QuerySelector("[role='dialog']")!;
        dialog.GetAttribute("aria-modal").Should().Be("true");
        var title = cut.Find($"[id='{dialog.GetAttribute("aria-labelledby")}']");
        title.TextContent.Should().Be("Debug title");
        title.ClassList.Should().Contain("tm-modal-title");
        overlay.ClassList.Should().Contain("tm-document-debug-modal").And.Contain("tm-document-json-debug-modal", "the phase-2 BEM name stays as an alias");
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
    public void Close_FooterClose_AndScrim_RaiseOnClose()
    {
        var closed = 0;
        var cut = Render<TmDocumentDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Title, "Debug")
            .Add(p => p.ModalTestId, "debug-modal")
            .Add(p => p.CloseTestId, "debug-close")
            .Add(p => p.OnClose, EventCallback.Factory.Create(this, () => closed++)));

        cut.Find("[data-testid='debug-close']").Click();
        cut.Find("[data-testid='debug-close-footer']").Click();
        cut.Find("[data-testid='debug-modal']").Click();

        closed.Should().Be(3, "the header X, the footer Close and a click on the scrim all close");
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
        cut.FindAll(".tm-document-debug-modal__empty").Should().BeEmpty();
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

        cut.Find("[data-testid='warnings']").QuerySelector(".tm-document-debug-modal__empty.tm-document-json-debug-modal__empty")!
            .TextContent.Trim().Should().Be("No data");
        cut.Find("[data-testid='warnings-content']").TextContent.Trim().Should().BeEmpty();
    }
}
