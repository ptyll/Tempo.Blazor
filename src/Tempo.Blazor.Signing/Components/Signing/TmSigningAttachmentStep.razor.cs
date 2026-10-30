using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// Renders image, file, and generated stamp signing steps. The upload is the core
/// <c>TmFileDropZone</c> with a signing icon content; picked files render as cards with a preview
/// or type icon, a culturally formatted "size · type" line and a danger-ghost remove button
/// (Signing redesign phase 4, SI1).
/// </summary>
/// <remarks>
/// KNOWN LIMITATION (cosmetic in this package): uploads stay in the browser — the picked files are
/// handed to <see cref="AttachmentsChanged"/> as client-side metadata/preview only, and nothing is
/// persisted or virus-scanned server-side. Treat the step as complete presentation state, not as
/// proof of an upload, until a host-provided storage contract lands (Signing redesign phase 6).
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
    /// them — the focusable element is the input.
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

    private async Task HandleFilesSelectedAsync(InputFileChangeEventArgs args)
    {
        var files = AllowMultiple ? args.GetMultipleFiles() : args.GetMultipleFiles(1);
        var attachments = Attachments.ToList();
        if (!AllowMultiple)
        {
            attachments.Clear();
        }

        attachments.AddRange(files.Select(file => new TmSigningStepAttachment
        {
            Name = file.Name,
            ContentType = file.ContentType,
            Size = file.Size
        }));

        _validationMessage = Field.Required && attachments.Count == 0 ? RequiredAttachmentMessage : null;
        await AttachmentsChanged.InvokeAsync(attachments);
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
