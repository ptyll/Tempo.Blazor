using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// TmSigningAttachmentStep on the Tm components (Signing redesign phase 4): the upload is a core
/// <c>TmFileDropZone</c> (Accept / Multiple / InputAriaLabel / InputTestId / OnFilesSelected) with a
/// custom icon ChildContent instead of the default emoji, and the picked files render as cards with
/// a preview or type icon, a culturally formatted "size · type" line and a danger-ghost Remove.
/// </summary>
public class TmSigningAttachmentStepTests : LocalizationTestBase
{
    [Fact]
    public void AttachmentStep_UsesTmFileDropZone_WithIconContent_AndFileCards()
    {
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Files", Type = SigningFieldType.File })
            .Add(p => p.AllowMultiple, true)
            .Add(p => p.Accept, ".pdf"));

        cut.Find(".tm-file-drop-zone").Should().NotBeNull("the upload is the core TmFileDropZone");

        // The custom ChildContent replaced the default emoji with the TmIcon.
        cut.Find(".tm-signing-attachment-step__drop-icon .tm-icon").Should().NotBeNull();
        cut.Find(".tm-file-drop-zone").TextContent.Should().NotContain("📁");

        var input = cut.Find("input[type='file']");
        input.HasAttribute("multiple").Should().BeTrue();
        input.GetAttribute("accept").Should().Be(".pdf");
        input.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace("the file input carries an accessible name");
        input.GetAttribute("data-testid").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AttachmentStep_ShowsSizeAndType()
    {
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Files", Type = SigningFieldType.File })
            .Add(p => p.Attachments, [new TmSigningStepAttachment
            {
                Uuid = "file-1",
                Name = "contract.pdf",
                ContentType = "application/pdf",
                Size = 1_258_291
            }])
            .Add(p => p.Culture, "cs-CZ"));

        cut.Find(".tm-signing-attachment-step__files-title").TextContent.Should().Contain("Attached files (1)");
        var meta = cut.Find(".tm-signing-attachment-step__meta");
        meta.TextContent.Should().Contain("1,2 MB", "cs-CZ formats the decimal comma");
        meta.TextContent.Should().Contain("application/pdf");
    }

    [Fact]
    public void Render_ImageAttachment_ShowsPreview()
    {
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Photo", Type = SigningFieldType.Image })
            .Add(p => p.Attachments, [new TmSigningStepAttachment { Name = "photo.png", Url = "/photo.png" }]));

        cut.Find("img.tm-signing-attachment-step__preview").GetAttribute("src").Should().Be("/photo.png");
    }

    [Fact]
    public void Render_NonImageAttachment_ShowsTypeIcon()
    {
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Files", Type = SigningFieldType.File })
            .Add(p => p.Attachments, [new TmSigningStepAttachment { Name = "contract.pdf", Size = 1024 }]));

        cut.Find(".tm-signing-attachment-step__file-icon .tm-icon").Should().NotBeNull();
    }

    [Fact]
    public void RemoveAttachment_InvokesAttachmentsChanged()
    {
        IReadOnlyList<TmSigningStepAttachment>? captured = null;
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Files", Type = SigningFieldType.File })
            .Add(p => p.Attachments, [new TmSigningStepAttachment { Uuid = "file-1", Name = "contract.pdf" }])
            .Add(p => p.AttachmentsChanged, EventCallback.Factory.Create<IReadOnlyList<TmSigningStepAttachment>>(this, value => captured = value)));

        cut.Find(".tm-signing-attachment-step__remove").Click();

        captured.Should().BeEmpty();
    }

    /// <summary>
    /// The drop zone's OWN file list stays hidden (phase 4 review): the step renders its cards, so
    /// a picked file must not show TWICE.
    /// </summary>
    [Fact]
    public void PickedFiles_ShowOnlyOnce_TheDropZoneListStaysHidden()
    {
        IReadOnlyList<TmSigningStepAttachment>? picked = null;
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Files", Type = SigningFieldType.File })
            .Add(p => p.AllowMultiple, true)
            .Add(p => p.AttachmentsChanged, EventCallback.Factory
                .Create<IReadOnlyList<TmSigningStepAttachment>>(this, value => picked = value)));

        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(
            InputFileContent.CreateFromText("payload", "contract.pdf", contentType: "application/pdf"));

        // The runner would feed the raised attachments back as the parameter — do the same.
        picked.Should().NotBeNull();
        cut.Render(parameters => parameters.Add(p => p.Attachments, picked!));

        cut.FindAll(".tm-file-drop-zone__file-list").Should().BeEmpty("the zone's built-in list is suppressed");
        cut.Find(".tm-signing-attachment-step__item").Should().NotBeNull("the step's own card shows the file");
    }

    /// <summary>
    /// The FILE INPUT (the focusable element) carries the description and the invalid state
    /// through the core InputAttributes splat (phase 4 review) — not the zone wrapper.
    /// </summary>
    [Fact]
    public void FileInput_CarriesAriaDescribedByAndInvalidState()
    {
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Files", Type = SigningFieldType.File, Required = true })
            .Add(p => p.Attachments, [new TmSigningStepAttachment { Uuid = "file-1", Name = "contract.pdf" }])
            .Add(p => p.AttachmentsChanged, EventCallback.Factory
                .Create<IReadOnlyList<TmSigningStepAttachment>>(this, _ => { })));

        // Removing the only attachment of a required field fails the step → the shell alert shows.
        cut.Find(".tm-signing-attachment-step__remove").Click();

        var input = cut.Find("input[type='file']");
        input.GetAttribute("aria-invalid").Should().Be("true");
        var describedBy = input.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrWhiteSpace();
        cut.Find($"#{describedBy}").TextContent.Should().Contain("Upload an attachment");
    }

    /// <summary>The remove action names the FILE ("Remove contract.pdf"); the preview is decorative.</summary>
    [Fact]
    public void RemoveButton_NamesTheFile_PreviewIsEmptyAlt()
    {
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Photo", Type = SigningFieldType.Image })
            .Add(p => p.Attachments, [new TmSigningStepAttachment { Uuid = "file-1", Name = "photo.png", Url = "/photo.png" }]));

        cut.Find(".tm-signing-attachment-step__remove")
            .GetAttribute("aria-label")
            .Should().Be("Remove photo.png");
        cut.Find("img.tm-signing-attachment-step__preview").GetAttribute("alt").Should().BeEmpty(
            "the card's text already carries the file name — a repeated alt reads twice");
    }

    [Fact]
    public void Render_Stamp_ShowsPlaceholderOrValue()
    {
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Stamp", Type = SigningFieldType.Stamp })
            .Add(p => p.StampValue, "STAMP-001"));

        cut.Find(".tm-signing-attachment-step__stamp").TextContent.Should().Contain("STAMP-001");
    }
}
