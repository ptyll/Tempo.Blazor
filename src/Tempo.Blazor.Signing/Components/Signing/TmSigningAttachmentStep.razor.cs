using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// Renders image, file, and generated stamp signing steps. The upload is the core
/// <c>TmFileDropZone</c> with a signing icon content; picked files render as cards with a preview
/// or type icon, a culturally formatted "size · type" line and a danger-ghost remove button
/// (Signing redesign phase 4, SI1). Phase 6 (S2-5) adds the host upload contract
/// (<see cref="UploadUrlResolver"/>), the size/count limits and the local image preview.
/// </summary>
/// <remarks>
/// SECURITY NOTE (phase 6 contract): this component NEVER reads a picked file's content beyond the
/// optional downscaled image preview (<see cref="IBrowserFile.RequestImageFileAsync"/>, a local
/// data-url thumbnail). The upload itself is host-owned: the runner hands the
/// <see cref="IBrowserFile"/> to the host and stores the host-returned url on the attachment — on
/// Blazor Server the content must be consumed at the host because browser files stream over
/// SignalR with tight message limits. Without the resolver the picked files stay client-side
/// metadata (the documented placeholder behaviour).
/// </remarks>
public partial class TmSigningAttachmentStep
{
    // Per-instance accessible plumbing (Signing redesign phase 2): ids are owned by THIS component
    // instance and handed to the shell, so two rendered steps never share an id even when they
    // represent the same field uuid.
    private readonly string _titleId = $"tm-signing-step-title-{Guid.NewGuid():N}";
    private readonly string _descriptionId = $"tm-signing-step-description-{Guid.NewGuid():N}";
    private readonly string _validationId = $"tm-signing-step-validation-{Guid.NewGuid():N}";
    private string? _validationMessage;

    // Upload contract (phase 6 review): the busy state of the host round-trip and the LOCAL
    // image previews. The previews live in a COMPONENT-LOCAL dictionary keyed by the attachment
    // uuid and are NEVER written into the persisted attachment Url — navigating away (the runner
    // unmounts the step) drops them, exactly the presentation-only contract. The polite live
    // region announces the upload events ("Uploading {name}…"); always rendered (WCAG 4.1.3).
    private bool _uploading;
    private string? _uploadingFileName;
    private string? _announcement;
    private readonly Dictionary<string, string> _previews = new(StringComparer.Ordinal);

    /// <summary>Signing field represented by this step.</summary>
    [Parameter] public SigningField Field { get; set; } = new() { Type = SigningFieldType.File };

    /// <summary>Current uploaded attachments.</summary>
    [Parameter] public IReadOnlyList<TmSigningStepAttachment> Attachments { get; set; } = [];

    /// <summary>Callback invoked when attachments change.</summary>
    [Parameter] public EventCallback<IReadOnlyList<TmSigningStepAttachment>> AttachmentsChanged { get; set; }

    /// <summary>Generated stamp text or image identifier.</summary>
    [Parameter] public string? StampValue { get; set; }

    /// <summary>Short text describing where the field appears in the document.</summary>
    [Parameter] public string? AppearsOn { get; set; }

    /// <summary>Whether file input allows multiple files.</summary>
    [Parameter] public bool AllowMultiple { get; set; }

    /// <summary>Accepted file types for upload.</summary>
    [Parameter] public string? Accept { get; set; }

    /// <summary>
    /// Maximum accepted file size in bytes (phase 6). A larger file shows the localized
    /// "file is larger than …" message and never joins the attachments — the rejection happens
    /// BEFORE any host upload round-trip.
    /// </summary>
    [Parameter] public long? MaxFileSize { get; set; }

    /// <summary>
    /// Maximum number of attachments (phase 6). Overflowing files show the localized
    /// "up to {0} files" message and are rejected; the first files stay.
    /// </summary>
    [Parameter] public int? MaxFiles { get; set; }

    /// <summary>
    /// The native <c>capture</c> attribute for mobile camera capture (phase 6, optional) — for
    /// example <c>environment</c> or <c>user</c>. Splatted on the native file input of
    /// <see cref="SigningFieldType.Image"/> fields only.
    /// </summary>
    [Parameter] public string? Capture { get; set; }

    /// <summary>
    /// Host-provided upload resolver (phase 6): receives the picked file and returns the uploaded
    /// file's url, which the card then previews. <see cref="TmSigningFormRunner"/> passes its
    /// <c>OnAttachmentSelected</c> host hook through; a step without a resolver keeps the
    /// client-side-only placeholder behaviour.
    /// </summary>
    [Parameter] public Func<IBrowserFile, Task<string?>>? UploadUrlResolver { get; set; }

    /// <summary>Whether the controls are disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Culture used to resolve localized field text and to format file sizes.</summary>
    [Parameter] public string? Culture { get; set; }

    /// <summary>Fallback culture used when localized field text is missing.</summary>
    [Parameter] public string? FallbackCulture { get; set; }

    /// <summary>Additional CSS classes for the shell element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the shell element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The runner's step validation, cascaded by <see cref="TmSigningFormRunner"/> (absent for standalone steps).</summary>
    [CascadingParameter(Name = "TmSigningRunnerValidation")]
    private string? RunnerValidationMessage { get; set; }

    private string ShellClass => string.Join(" ", new[] { "tm-signing-attachment-step", Class }.Where(item => !string.IsNullOrWhiteSpace(item)));

    private bool HasDescription => !string.IsNullOrWhiteSpace(SigningTextResolver.FieldDescription(Field, Culture, FallbackCulture));

    /// <summary>The message the shell renders as the step's single alert (own message first, then the runner's).</summary>
    private string? EffectiveValidationMessage => !string.IsNullOrWhiteSpace(_validationMessage)
        ? _validationMessage
        : RunnerValidationMessage;

    private bool HasValidationMessage => !string.IsNullOrWhiteSpace(EffectiveValidationMessage);

    /// <summary>
    /// The ids the drop zone references — per-instance, and the SAME instances the shell renders
    /// (they are passed to the shell as parameters), so every reference resolves inside the
    /// rendered DOM (Signing redesign phase 2).
    /// </summary>
    private string? DescribedBy
    {
        get
        {
            var parts = new List<string>(2);
            if (HasDescription)
            {
                parts.Add(_descriptionId);
            }

            if (HasValidationMessage)
            {
                parts.Add(_validationId);
            }

            return parts.Count == 0 ? null : string.Join(" ", parts);
        }
    }

    /// <summary>DOM id of the attached-files heading, derived from this instance's description id.</summary>
    private string FilesTitleId => $"{_descriptionId}-files";

    /// <summary>
    /// The attributes the core file input carries through <c>TmFileDropZone.InputAttributes</c>
    /// (phase 4 review): the description / validation ids (while they render) as the description
    /// and the invalid state while the step shows a message. The zone wrapper no longer carries
    /// them — the focusable element is the input. Phase 6 adds the optional <c>capture</c>
    /// attribute for mobile camera capture, on Image fields only.
    /// </summary>
    private Dictionary<string, object> BuildInputAttributes()
    {
        var attributes = new Dictionary<string, object>
        {
            ["aria-invalid"] = BoolText(HasValidationMessage)
        };

        if (DescribedBy is { } describedBy)
        {
            attributes["aria-describedby"] = describedBy;
        }

        if (!string.IsNullOrWhiteSpace(Capture) && Field.Type == SigningFieldType.Image)
        {
            attributes["capture"] = Capture;
        }

        return attributes;
    }

    private string StampPlaceholderText => SigningLocalizationResolver.ResolveFieldPlaceholder(Field, Culture, FallbackCulture, Loc["TmSigningStep_StampPlaceholder"]);

    /// <summary>The culture-formatted "size · type" line of one attachment card.</summary>
    private string FormatCardMeta(TmSigningStepAttachment attachment)
    {
        var parts = new List<string>(2) { FormatSize(attachment.Size) };
        if (!string.IsNullOrWhiteSpace(attachment.ContentType))
        {
            parts.Add(attachment.ContentType);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Formats a file size with the field culture (cs-CZ renders "1,2 MB").</summary>
    private string FormatSize(long bytes)
    {
        var culture = ResolveCulture();
        const long Kilobyte = 1024;
        const long Megabyte = Kilobyte * 1024;

        return bytes switch
        {
            < Kilobyte => string.Create(culture, $"{bytes} B"),
            < Megabyte => string.Create(culture, $"{bytes / (double)Kilobyte:N1} KB"),
            _ => string.Create(culture, $"{bytes / (double)Megabyte:N1} MB")
        };
    }

    private CultureInfo ResolveCulture()
    {
        if (string.IsNullOrWhiteSpace(Culture))
        {
            return CultureInfo.InvariantCulture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(Culture);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    /// <summary>Helper properties for the busy state (phase 6 review): the drop zone and the
    /// remove actions disable while a host upload round-trip runs.</summary>
    private bool IsBusy => Disabled || _uploading;

    private async Task HandleFilesSelectedAsync(InputFileChangeEventArgs args)
    {
        if (IsBusy)
        {
            return;
        }

        // The read budget caps what the platform hands the handler (phase 6 review): MaxFiles
        // when set, the default 10 otherwise. An OVERFLOW pick (more files than the budget) is
        // refused by the platform itself — and NOTHING is then readable (even <c>args.File</c>
        // is a budget-of-one read), so the pick is rejected wholesale with the localized message.
        var fileLimit = Math.Max(MaxFiles ?? 10, 1);
        var errors = new List<string>();
        List<IBrowserFile> files;
        try
        {
            files = AllowMultiple
                ? [.. args.GetMultipleFiles(fileLimit)]
                : [args.GetMultipleFiles(1)[0]];
        }
        catch (InvalidOperationException)
        {
            // The platform's own overflow type for GetMultipleFiles.
            files = [];
            errors.Add(Loc["TmSigningStep_TooManyFiles", fileLimit]);
        }

        // Limits first (phase 6): a rejected file never reaches the host resolver, and the
        // localized messages accumulate so multiple bad picks read at once.
        var accepted = new List<IBrowserFile>();
        foreach (var file in files)
        {
            if (MaxFileSize is { } maxBytes && file.Size > maxBytes)
            {
                errors.Add(Loc["TmSigningStep_FileTooLarge", file.Name, FormatSize(maxBytes)]);
                continue;
            }

            if (AllowMultiple && MaxFiles is { } maxFiles && Attachments.Count + accepted.Count >= maxFiles)
            {
                errors.Add(Loc["TmSigningStep_TooManyFiles", maxFiles]);
                continue;
            }

            accepted.Add(file);
        }

        // NOTHING accepted (a bad pick only): the existing attachments stay — a single-mode
        // replacement must never drop the previous file (phase 6 review).
        if (accepted.Count == 0)
        {
            _validationMessage = errors.Count > 0 ? string.Join(" ", errors) : null;
            return;
        }

        _uploading = true;
        _uploadingFileName = accepted[0].Name;
        _announcement = Loc["TmSigningStep_Uploading", _uploadingFileName];
        // Render the busy state BEFORE the first await: an in-flight event handler does not
        // re-render by itself, so the disabled zone / "Uploading…" row would never appear.
        StateHasChanged();
        try
        {
            // Single mode REPLACES; multiple mode APPENDS to the existing list.
            var attachments = AllowMultiple ? Attachments.ToList() : [];
            foreach (var file in accepted)
            {
                // The upload is host-owned (phase 6): the host reads the content and returns the
                // stored url. A null result (or a failing host, cancellation aside) REJECTS the
                // file — it never joins the attachments, and the localized failure message shows.
                string? url = null;
                if (UploadUrlResolver is not null)
                {
                    try
                    {
                        url = await UploadUrlResolver(file);
                    }
                    catch (OperationCanceledException)
                    {
                        // The host cancelled the upload (runner disposed / host cancellation):
                        // the cancellation propagates instead of being dressed up as a per-file error.
                        throw;
                    }
                    catch (Exception)
                    {
                        url = null;
                    }

                    if (url is null)
                    {
                        errors.Add(Loc["TmSigningStep_UploadFailed", file.Name]);
                        continue;
                    }
                }

                // LOCAL preview only (phase 6, review-hardened): a downscaled thumbnail through
                // the core Blazor API — no custom JS — best-effort, for PNG/JPEG/GIF/WebP picks
                // without a host url. It goes into the COMPONENT-LOCAL preview map, never into
                // the persisted Url (the legacy path keeps Url null as before).
                var attachment = new TmSigningStepAttachment
                {
                    Name = file.Name,
                    ContentType = file.ContentType,
                    Size = file.Size,
                    Url = url
                };

                if (url is null && Field.Type == SigningFieldType.Image
                    && IsPreviewableImage(file.ContentType) && file.Size <= PreviewLimitBytes)
                {
                    var preview = await TryCreateImagePreviewAsync(file);
                    if (preview is not null)
                    {
                        _previews[attachment.Uuid] = preview;
                    }
                }

                attachments.Add(attachment);
            }

            _validationMessage = errors.Count > 0
                ? string.Join(" ", errors)
                : Field.Required && attachments.Count == 0 ? RequiredAttachmentMessage : null;
            await AttachmentsChanged.InvokeAsync(attachments);
        }
        finally
        {
            _uploading = false;
            _uploadingFileName = null;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>Preview budget (phase 6): images above this size never build a local thumbnail.</summary>
    private const long PreviewLimitBytes = 5 * 1024 * 1024;

    /// <summary>Preview thumbnail box (phase 6): the card renders a 48px square, so 320px keep ample retina headroom.</summary>
    private const int PreviewPixelLimit = 320;

    /// <summary>
    /// The raster types a local thumbnail may decode (phase 6 review): PNG/JPEG/GIF/WebP only —
    /// SVG stays excluded (a downscaled SVG is still active content).
    /// </summary>
    private static bool IsPreviewableImage(string? contentType)
    {
        return contentType?.ToLowerInvariant() is "image/png" or "image/jpeg" or "image/gif" or "image/webp";
    }

    /// <summary>
    /// The preview source of one attachment card: the COMPONENT-LOCAL thumbnail first, then the
    /// persisted url (the host upload result). Presentational only.
    /// </summary>
    private string? GetPreviewUrl(TmSigningStepAttachment attachment)
    {
        return _previews.TryGetValue(attachment.Uuid, out var preview) ? preview : attachment.Url;
    }

    private async Task<string?> TryCreateImagePreviewAsync(IBrowserFile file)
    {
        try
        {
            var image = await file.RequestImageFileAsync(file.ContentType, PreviewPixelLimit, PreviewPixelLimit);
            await using var stream = image.OpenReadStream(image.Size);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            // The data url names the DECODED image's content type (phase 6 review) — the
            // platform may hand back a normalized type.
            return $"data:{image.ContentType};base64,{Convert.ToBase64String(buffer.ToArray())}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or IOException or ArgumentException)
        {
            // The preview is presentation-only: a stream the platform refuses (or bUnit's fake —
            // BrowserFileExtensions refuse the real decoder for custom IBrowserFile
            // implementations) simply renders no thumbnail.
            return null;
        }
    }

    private async Task RemoveAttachmentAsync(string uuid)
    {
        var attachments = Attachments.Where(item => !string.Equals(item.Uuid, uuid, StringComparison.Ordinal)).ToArray();
        _validationMessage = Field.Required && attachments.Length == 0 ? RequiredAttachmentMessage : null;
        await AttachmentsChanged.InvokeAsync(attachments);
    }

    private string RequiredAttachmentMessage => SigningLocalizationResolver.ResolveValidationMessage(Field.Validation, Culture, FallbackCulture, Loc["TmSigningStep_RequiredAttachment"]);

    private static string BoolText(bool value) => value ? "true" : "false";
}
