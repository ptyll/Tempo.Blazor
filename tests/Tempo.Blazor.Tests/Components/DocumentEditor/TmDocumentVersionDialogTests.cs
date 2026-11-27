using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>Redesign plan 146c7e1b, phase 6 (DE-2.5): the version dialog is a real form.</summary>
public sealed class TmDocumentVersionDialogTests : LocalizationTestBase
{
    [Fact]
    public void Body_IsAFormWithTheFields_AndTheFooterSubmitButtonIsBoundToIt()
    {
        var cut = Render<TmDocumentVersionDialog>();

        var form = cut.Find("form.tm-document-version-dialog__form");
        form.QuerySelector("[data-testid='document-version-kind']").Should().NotBeNull();
        form.QuerySelector("[data-testid='document-version-label']").Should().NotBeNull();
        form.QuerySelector("[data-testid='document-version-description']").Should().NotBeNull();

        var submit = cut.Find("[data-testid='document-version-create-submit']");
        submit.GetAttribute("type").Should().Be("submit");
        submit.GetAttribute("form").Should().Be(form.Id);
        submit.ClassList.Should().Contain("tm-btn-primary");
        cut.Find(".tm-modal-footer").Contains(submit.Closest(".tm-modal-footer")!).Should().BeTrue();
    }

    [Fact]
    public void SubmittingTheForm_CreatesTheVersion_WithTheTypedValues()
    {
        DocumentVersionDialogResult? result = null;
        var cut = Render<TmDocumentVersionDialog>(parameters => parameters
            .Add(p => p.OnCreate, EventCallback.Factory.Create<DocumentVersionDialogResult>(this, r => result = r)));

        cut.Find("[data-testid='document-version-label']").Input("  Release 2  ");
        cut.Find("[data-testid='document-version-description']").Input("Contract signed");
        cut.Find("form").Submit();

        result.Should().NotBeNull();
        result!.Label.Should().Be("Release 2");
        result.Description.Should().Be("Contract signed");
        result.Kind.Should().Be(DocumentVersionKind.Minor);
    }

    [Fact]
    public void SubmittingTheForm_DoesNotNavigate_AndIsGuardedWhileBusy()
    {
        var created = 0;
        var cut = Render<TmDocumentVersionDialog>(parameters => parameters
            .Add(p => p.IsBusy, true)
            .Add(p => p.OnCreate, EventCallback.Factory.Create<DocumentVersionDialogResult>(this, _ => created++)));

        cut.Find("form").GetAttribute("novalidate").Should().NotBeNull();
        cut.Find("[data-testid='document-version-create-submit']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("form").Submit();

        created.Should().Be(0, "an Enter during a running create cannot start a second one");
    }

    [Fact]
    public void MajorWithoutADescription_IsRefused_OnSubmit_AndAcceptedOnceDescribed()
    {
        var created = 0;
        var cut = Render<TmDocumentVersionDialog>(parameters => parameters
            .Add(p => p.RequireMajorDescription, true)
            .Add(p => p.OnCreate, EventCallback.Factory.Create<DocumentVersionDialogResult>(this, _ => created++)));

        cut.Find("[data-testid='document-version-kind']").Change(DocumentVersionKind.Major.ToString());
        cut.Find("form").Submit();

        created.Should().Be(0);
        cut.Find("[data-testid='document-version-error']").TextContent.Should().Contain("description");
        cut.Find("[data-testid='document-version-description']").GetAttribute("aria-invalid").Should().Be("true");
        cut.Find("[data-testid='document-version-description']").GetAttribute("aria-describedby")
            .Should().Be(cut.Find("[data-testid='document-version-error']").Id);

        cut.Find("[data-testid='document-version-description']").Input("Why it is major");
        cut.Find("form").Submit();
        created.Should().Be(1);
    }

    [Fact]
    public void FocusRestore_StillTargetsTheGivenElement()
    {
        var cut = Render<TmDocumentVersionDialog>(parameters => parameters.Add(p => p.RestoreFocusTargetId, "canvas-input"));

        cut.FindComponent<Tempo.Blazor.Components.Feedback.TmModal>().Instance.RestoreFocusTargetId.Should().Be("canvas-input");
    }

    [Fact]
    public void Dialog_KeepsItsCoreModalContract()
    {
        var cut = Render<TmDocumentVersionDialog>();

        var modal = cut.FindComponent<Tempo.Blazor.Components.Feedback.TmModal>().Instance;
        modal.Size.Should().Be(Tempo.Blazor.Components.Feedback.ModalSize.Small);
        modal.MobilePresentation.Should().Be(Tempo.Blazor.Components.Feedback.MobilePresentation.Auto);
        cut.Find("[data-testid='document-version-cancel']").Should().NotBeNull();
    }
}
