using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>Redesign plan 146c7e1b, phase 6 (DE-2.5): "You are viewing version X - Return to current / Restore" with an inline confirmation.</summary>
public sealed class TmDocumentVersionPreviewStripTests : LocalizationTestBase
{
    private static DocumentVersion Version(string label = "1.3", string id = "v1") => new()
    {
        Id = id,
        Label = label,
        Kind = DocumentVersionKind.Minor,
        Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" }
    };

    [Fact]
    public void NoPreview_RendersNothing()
    {
        Render<TmDocumentVersionPreviewStrip>().FindAll("[data-testid='document-version-preview-state']").Should().BeEmpty();
    }

    [Fact]
    public void Strip_NamesTheVersion_AndOffersReturnAndRestore()
    {
        var returned = 0;
        var cut = Render<TmDocumentVersionPreviewStrip>(parameters => parameters
            .Add(p => p.PreviewVersion, Version())
            .Add(p => p.OnReturnToCurrent, () => returned++));

        var strip = cut.Find("[data-testid='document-version-preview-state']");
        strip.TextContent.Should().Contain("You are viewing version 1.3");
        strip.QuerySelector(".tm-icon").Should().NotBeNull();
        cut.Find("[data-testid='document-version-current']").TextContent.Trim().Should().Be("Return to current");
        cut.Find("[data-testid='document-version-restore']").TextContent.Trim().Should().Be("Restore");

        cut.Find("[data-testid='document-version-current']").Click();
        returned.Should().Be(1);
    }

    [Fact]
    public void Restore_ConfirmsInline_AndOnlyTheConfirmRaisesTheRestore()
    {
        DocumentVersion? restored = null;
        var cut = Render<TmDocumentVersionPreviewStrip>(parameters => parameters
            .Add(p => p.PreviewVersion, Version())
            .Add(p => p.OnRestoreVersion, version => restored = version));

        cut.Find("[data-testid='document-version-restore']").Click();

        restored.Should().BeNull("one press only asks");
        cut.Find("[data-testid='document-version-restore-confirm']").TextContent.Should().Contain("Restore this version as the current draft?");
        cut.FindAll("[data-testid='document-version-restore']").Should().BeEmpty("the question replaces the two buttons");

        cut.Find("[data-testid='document-version-restore-confirm-button']").Click();

        restored!.Id.Should().Be("v1");
        cut.FindAll("[data-testid='document-version-restore-confirm']").Should().BeEmpty();
    }

    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private string? FocusedElementReferenceId() => JSInterop.Invocations
        .Where(invocation => invocation.Identifier == FocusInvocation)
        .Select(invocation => (invocation.Arguments[0] as Microsoft.AspNetCore.Components.ElementReference?)?.Id)
        .LastOrDefault();

    [Fact]
    public void OpeningTheQuestion_FocusesConfirm_AndCancelHandsTheFocusBackToRestore()
    {
        var cut = Render<TmDocumentVersionPreviewStrip>(parameters => parameters.Add(p => p.PreviewVersion, Version()));

        cut.Find("[data-testid='document-version-restore']").Click();

        var confirm = cut.Find("[data-testid='document-version-restore-confirm-button']");
        cut.WaitForAssertion(() => FocusedElementReferenceId().Should().Be(confirm.GetAttribute("blazor:elementreference"), "the pressed button left the DOM: the confirm button takes the focus"));
        var group = cut.Find("[data-testid='document-version-restore-confirm']");
        group.GetAttribute("role").Should().Be("group");
        var question = cut.Find(".tm-document-version-preview-strip__question");
        group.GetAttribute("aria-labelledby").Should().Be(question.Id);

        cut.Find("[data-testid='document-version-restore-cancel']").Click();

        var restore = cut.Find("[data-testid='document-version-restore']");
        cut.WaitForAssertion(() => FocusedElementReferenceId().Should().Be(restore.GetAttribute("blazor:elementreference"), "Cancel returns the focus to the button that asked"));
    }

    [Fact]
    public void Cancel_ReturnsToTheTwoButtons()
    {
        var cut = Render<TmDocumentVersionPreviewStrip>(parameters => parameters.Add(p => p.PreviewVersion, Version()));

        cut.Find("[data-testid='document-version-restore']").Click();
        cut.Find("[data-testid='document-version-restore-cancel']").Click();

        cut.FindAll("[data-testid='document-version-restore-confirm']").Should().BeEmpty();
        cut.Find("[data-testid='document-version-restore']").Should().NotBeNull();
    }

    [Fact]
    public void PreviewingAnotherVersion_DropsAPendingConfirmation()
    {
        var cut = Render<TmDocumentVersionPreviewStrip>(parameters => parameters.Add(p => p.PreviewVersion, Version("1.3", "v1")));
        cut.Find("[data-testid='document-version-restore']").Click();
        cut.Find("[data-testid='document-version-restore-confirm']").Should().NotBeNull();

        cut.Render(parameters => parameters.Add(p => p.PreviewVersion, Version("1.2", "v2")));

        cut.FindAll("[data-testid='document-version-restore-confirm']").Should().BeEmpty();
        cut.Find("[data-testid='document-version-preview-state']").TextContent.Should().Contain("1.2");
    }

    [Fact]
    public void WithoutALabel_TheStripNamesTheKind()
    {
        var version = Version();
        version.Label = null;
        var cut = Render<TmDocumentVersionPreviewStrip>(parameters => parameters.Add(p => p.PreviewVersion, version));

        cut.Find("[data-testid='document-version-preview-state']").TextContent.Should().Contain("Minor version");
    }
}
