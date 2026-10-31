using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
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

    /// <summary>Phase 6: a file larger than <c>MaxFileSize</c> shows the localized limit error and
    /// never joins the attachments — the rejection happens before any host round-trip.</summary>
    [Fact]
    public void AttachmentStep_ExceedsMaxFileSize_ShowsLocalizedError()
    {
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Photo", Type = SigningFieldType.Image })
            .Add(p => p.MaxFileSize, 10));

        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(
            InputFileContent.CreateFromText("way more than ten bytes", "big.png", contentType: "image/png"));

        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("larger than");
        cut.FindAll(".tm-signing-attachment-step__item").Should().BeEmpty(
            "a rejected file never joins the attachments");
    }

    /// <summary>Phase 6: <c>MaxFiles</c> caps the picked files with a localized message — the
    /// first files stay, the overflow is rejected. The direct event path exercises the PLATFORM
    /// overflow semantics: with a pick beyond the read budget NOTHING is readable, so the pick is
    /// rejected wholesale with the localized message (a bUnit <c>UploadFiles</c> pick — where the
    /// budget is not enforced by the platform fake — keeps flowing through the per-file limit
    /// check, which the legacy regression below pins).</summary>
    [Fact]
    public async Task AttachmentStep_ExceedsMaxFiles_ShowsLocalizedError()
    {
        IReadOnlyList<TmSigningStepAttachment>? published = null;
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Files", Type = SigningFieldType.File })
            .Add(p => p.AllowMultiple, true)
            .Add(p => p.MaxFiles, 1)
            .Add(p => p.AttachmentsChanged, EventCallback.Factory
                .Create<IReadOnlyList<TmSigningStepAttachment>>(this, value => published = value)));

        await cut.InvokeAsync(() => cut.FindComponent<TmFileDropZone>().Instance.OnFilesSelected.InvokeAsync(
            new InputFileChangeEventArgs(
            [
                new TestBrowserFile("one.pdf", "application/pdf", [0x31]),
                new TestBrowserFile("two.pdf", "application/pdf", [0x32])
            ])));

        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("up to");
        published.Should().BeNull(
            "a pick beyond the read budget is rejected wholesale — nothing was readable");
    }

    /// <summary>LEGACY regression for the MaxFiles per-file limit: when the pick FITS the read
    /// budget but the EXISTING attachments already fill the limit, the overflow file is rejected
    /// with the localized message and the accepted files append. (The bUnit UploadFiles fake does
    /// not complete async OnChange chains in this bUnit version, so the platform-event path is
    /// exercised through the same seam the runner uses.)</summary>
    [Fact]
    public async Task AttachmentStep_MaxFiles_PerFileLimit_KeepsTheFirstFiles()
    {
        IReadOnlyList<TmSigningStepAttachment>? published = null;
        var existing = new TmSigningStepAttachment { Uuid = "file-1", Name = "one.pdf", Size = 1 };
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Files", Type = SigningFieldType.File })
            .Add(p => p.AllowMultiple, true)
            .Add(p => p.MaxFiles, 2)
            .Add(p => p.Attachments, [existing])
            .Add(p => p.AttachmentsChanged, EventCallback.Factory
                .Create<IReadOnlyList<TmSigningStepAttachment>>(this, value => published = value)));

        await cut.InvokeAsync(() => cut.FindComponent<TmFileDropZone>().Instance.OnFilesSelected.InvokeAsync(
            new InputFileChangeEventArgs(
            [
                new TestBrowserFile("two.pdf", "application/pdf", [0x32]),
                new TestBrowserFile("three.pdf", "application/pdf", [0x33])
            ])));

        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("up to");
        published.Should().NotBeNull().And.HaveCount(2, "the first pick appends, the overflow is rejected");
        published!.Select(item => item.Name).Should().ContainInOrder("one.pdf", "two.pdf");
    }

    /// <summary>
    /// Phase 6: the LOCAL image preview flows through <c>RequestImageFileAsync</c> — no custom
    /// JS — best-effort by contract. bUnit CANNOT exercise the real decoder: the core
    /// <c>BrowserFileExtensions.RequestImageFileAsync</c> refuses custom
    /// <see cref="IBrowserFile"/> implementations (only the runtime's own browser files work), so
    /// this test pins the resilience contract — the refused preview never breaks the pick and
    /// fabricates no thumbnail. The rendered data-url preview itself is covered by the phase-6
    /// E2E suite against the real browser pipeline.
    /// </summary>
    [Fact]
    public async Task AttachmentStep_ImagePreview_FromRequestImageFileAsync()
    {
        IReadOnlyList<TmSigningStepAttachment>? published = null;
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Photo", Type = SigningFieldType.Image })
            .Add(p => p.AttachmentsChanged, EventCallback.Factory
                .Create<IReadOnlyList<TmSigningStepAttachment>>(this, value => published = value)));

        await cut.InvokeAsync(() => cut.FindComponent<TmFileDropZone>().Instance.OnFilesSelected.InvokeAsync(
            new InputFileChangeEventArgs([new TestBrowserFile("photo.png", "image/png", TestBrowserFile.PngBytes)])));

        // The runner would feed the raised attachments back as the parameter — do the same.
        published.Should().NotBeNull().And.ContainSingle();
        cut.Render(parameters => parameters.Add(p => p.Attachments, published!));

        // The decoder is unavailable in bUnit (the platform guard): no thumbnail is fabricated,
        // the pick still lands, and nothing crashed.
        cut.FindAll("img.tm-signing-attachment-step__preview").Should().BeEmpty(
            "bUnit cannot run the real RequestImageFileAsync decoder — no preview is fabricated");
        cut.Find(".tm-signing-attachment-step__name").TextContent.Should().Be("photo.png",
            "the pick itself still lands without the preview");
    }

    /// <summary>The host resolver wins over the local preview: with a resolver wired the card's
    /// url IS the host-returned one (the upload contract of phase 6).</summary>
    [Fact]
    public async Task AttachmentStep_HostUrlResolver_WinsOverTheLocalPreview()
    {
        const string hostUrl = "https://host.test/uploads/photo.png";
        IReadOnlyList<TmSigningStepAttachment>? published = null;
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Photo", Type = SigningFieldType.Image })
            .Add(p => p.UploadUrlResolver, _ => Task.FromResult<string?>(hostUrl))
            .Add(p => p.AttachmentsChanged, EventCallback.Factory
                .Create<IReadOnlyList<TmSigningStepAttachment>>(this, value => published = value)));

        await cut.InvokeAsync(() => cut.FindComponent<TmFileDropZone>().Instance.OnFilesSelected.InvokeAsync(
            new InputFileChangeEventArgs([new TestBrowserFile("photo.png", "image/png", TestBrowserFile.PngBytes)])));

        published.Should().NotBeNull().And.ContainSingle();
        cut.Render(parameters => parameters.Add(p => p.Attachments, published!));

        cut.Find("img.tm-signing-attachment-step__preview").GetAttribute("src").Should().Be(hostUrl);
    }

    /// <summary>The optional <c>capture</c> attribute rides the native image input (mobile camera
    /// capture) and only for Image fields.</summary>
    [Fact]
    public void AttachmentStep_Capture_RidesTheImageInput()
    {
        var image = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Photo", Type = SigningFieldType.Image })
            .Add(p => p.Capture, "environment"));

        image.Find("input[type='file']").GetAttribute("capture").Should().Be("environment");

        var file = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Files", Type = SigningFieldType.File })
            .Add(p => p.Capture, "environment"));

        file.Find("input[type='file']").HasAttribute("capture").Should().BeFalse(
            "capture is an image-only affordance");
    }

    /// <summary>Phase 6 review item 2: a host resolver returning NULL rejects the file — it never
    /// joins the attachments and the localized "could not be uploaded" message shows.</summary>
    [Fact]
    public async Task AttachmentStep_HostResolverReturnsNull_RejectsFile_WithLocalizedError()
    {
        IReadOnlyList<TmSigningStepAttachment>? published = null;
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Photo", Type = SigningFieldType.Image, Required = true })
            .Add(p => p.UploadUrlResolver, _ => Task.FromResult<string?>(null))
            .Add(p => p.AttachmentsChanged, EventCallback.Factory
                .Create<IReadOnlyList<TmSigningStepAttachment>>(this, value => published = value)));

        await cut.InvokeAsync(() => cut.FindComponent<TmFileDropZone>().Instance.OnFilesSelected.InvokeAsync(
            new InputFileChangeEventArgs([new TestBrowserFile("photo.png", "image/png", TestBrowserFile.PngBytes)])));

        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("could not be uploaded",
            "the localized upload-failure message shows");
        published.Should().NotBeNull().And.BeEmpty("a null host result rejects the file");
    }

    /// <summary>Phase 6 review item 2: a THROWING host rejects the file with the localized error —
    /// no crash, no attachment.</summary>
    [Fact]
    public async Task AttachmentStep_HostResolverThrows_ShowsError_NoCrash()
    {
        IReadOnlyList<TmSigningStepAttachment>? published = null;
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Photo", Type = SigningFieldType.Image, Required = true })
            .Add(p => p.UploadUrlResolver, _ => throw new InvalidOperationException("storage down"))
            .Add(p => p.AttachmentsChanged, EventCallback.Factory
                .Create<IReadOnlyList<TmSigningStepAttachment>>(this, value => published = value)));

        await cut.InvokeAsync(() => cut.FindComponent<TmFileDropZone>().Instance.OnFilesSelected.InvokeAsync(
            new InputFileChangeEventArgs([new TestBrowserFile("photo.png", "image/png", TestBrowserFile.PngBytes)])));

        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("could not be uploaded");
        published.Should().NotBeNull().And.BeEmpty("a failing host rejects the file");
        // The zone is usable again after the failure.
        cut.Find("input[type='file']").HasAttribute("disabled").Should().BeFalse(
            "the busy state is cleared in finally");
    }

    /// <summary>Phase 6 review item 2 (single mode): picking an OVERSIZED replacement keeps the
    /// PRIOR file — a rejected pick never drops the existing attachment, and the rejected pick is
    /// not published back to the host (the value list stays untouched).</summary>
    [Fact]
    public async Task AttachmentStep_SingleMode_OversizedReplacement_KeepsPriorFile()
    {
        var publishedCount = 0;
        var prior = new TmSigningStepAttachment { Uuid = "prior-1", Name = "photo.png", Size = 100 };
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Photo", Type = SigningFieldType.Image })
            .Add(p => p.Attachments, [prior])
            .Add(p => p.MaxFileSize, 10)
            .Add(p => p.AttachmentsChanged, EventCallback.Factory
                .Create<IReadOnlyList<TmSigningStepAttachment>>(this, _ => publishedCount++)));

        await cut.InvokeAsync(() => cut.FindComponent<TmFileDropZone>().Instance.OnFilesSelected.InvokeAsync(
            new InputFileChangeEventArgs([new TestBrowserFile("bigger.png", "image/png", TestBrowserFile.PngBytes)])));

        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("larger than");
        publishedCount.Should().Be(0, "a fully rejected pick does not publish a new attachment list");
        cut.Find(".tm-signing-attachment-step__name").TextContent.Should().Be("photo.png",
            "the prior file survives the rejected replacement");
    }

    /// <summary>Phase 6 review item 2: while the host upload runs, the drop zone is DISABLED and
    /// the polite live region announces "Uploading {name}…"; the busy state clears in finally.</summary>
    [Fact]
    public async Task AttachmentStep_Uploading_DisablesZone_AnnouncesPolitely_AndClears()
    {
        var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        IReadOnlyList<TmSigningStepAttachment>? published = null;
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Photo", Type = SigningFieldType.Image })
            .Add(p => p.UploadUrlResolver, _ => gate.Task)
            .Add(p => p.AttachmentsChanged, EventCallback.Factory
                .Create<IReadOnlyList<TmSigningStepAttachment>>(this, value => published = value)));

        var selection = cut.InvokeAsync(() => cut.FindComponent<TmFileDropZone>().Instance.OnFilesSelected.InvokeAsync(
            new InputFileChangeEventArgs([new TestBrowserFile("photo.png", "image/png", TestBrowserFile.PngBytes)])));

        cut.WaitForAssertion(() =>
        {
            cut.Find("input[type='file']").HasAttribute("disabled").Should().BeTrue(
                "the drop zone is disabled while the upload runs");
            cut.Find(".tm-signing-attachment-step__uploading").TextContent.Should().Contain("Uploading photo.png");
            cut.Find("[role='status'][aria-live='polite']").TextContent.Should().Contain("Uploading photo.png",
                "the busy state is announced politely");
        });

        gate.SetResult("https://host.test/photo.png");
        await selection;

        cut.Find("input[type='file']").HasAttribute("disabled").Should().BeFalse(
            "the busy state clears in finally");
        published.Should().NotBeNull().And.ContainSingle();
        published![0].Url.Should().Be("https://host.test/photo.png");
    }

    /// <summary>Phase 6 review item 2: the LOCAL preview is presentation-only — the persisted
    /// attachment Url stays NULL even for an image pick (the legacy path kept Url null, and no
    /// preview is ever written into the persisted value). bUnit cannot run the real
    /// <c>RequestImageFileAsync</c> decoder (the platform refuses custom
    /// <see cref="IBrowserFile"/> implementations), so no thumbnail is fabricated here — the
    /// rendered data-url preview is covered by the phase-6 E2E suite.</summary>
    [Fact]
    public async Task AttachmentStep_LocalPreview_IsPresentationOnly_UrlStaysNull()
    {
        IReadOnlyList<TmSigningStepAttachment>? published = null;
        var cut = Render<TmSigningAttachmentStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Photo", Type = SigningFieldType.Image })
            .Add(p => p.AttachmentsChanged, EventCallback.Factory
                .Create<IReadOnlyList<TmSigningStepAttachment>>(this, value => published = value)));

        await cut.InvokeAsync(() => cut.FindComponent<TmFileDropZone>().Instance.OnFilesSelected.InvokeAsync(
            new InputFileChangeEventArgs([new TestBrowserFile("photo.png", "image/png", TestBrowserFile.PngBytes)])));

        published.Should().NotBeNull().And.ContainSingle();
        published![0].Url.Should().BeNull(
            "the local preview is never persisted into the attachment Url");
        cut.Render(parameters => parameters.Add(p => p.Attachments, published!));

        cut.FindAll("img.tm-signing-attachment-step__preview").Should().BeEmpty(
            "bUnit cannot run the real decoder — no preview is fabricated");
        cut.Find(".tm-signing-attachment-step__name").TextContent.Should().Be("photo.png",
            "the pick itself still lands without the preview");
    }
}
