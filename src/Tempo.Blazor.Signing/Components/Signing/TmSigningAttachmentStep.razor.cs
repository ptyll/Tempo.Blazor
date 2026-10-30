using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Renders image, file, and generated stamp signing steps.</summary>
/// <remarks>
/// KNOWN LIMITATION (cosmetic in this package): uploads stay in the browser — the picked files are
/// handed to <see cref="AttachmentsChanged"/> as client-side metadata/preview only, and nothing is
/// persisted or virus-scanned server-side. Treat the step as complete presentation state, not as
/// proof of an upload, until a host-provided storage contract lands (Signing redesign phase 6).
/// </remarks>
public partial class TmSigningAttachmentStep
{
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

    /// <summary>Culture used to resolve localized field text.</summary>
    [Parameter] public string? Culture { get; set; }

    /// <summary>Fallback culture used when localized field text is missing.</summary>
    [Parameter] public string? FallbackCulture { get; set; }

    /// <summary>Additional CSS classes for the shell element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the shell element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    private string ShellClass => string.Join(" ", new[] { "tm-signing-attachment-step", Class }.Where(item => !string.IsNullOrWhiteSpace(item)));

    private string StampPlaceholderText => SigningLocalizationResolver.ResolveFieldPlaceholder(Field, Culture, FallbackCulture, Loc["TmSigningStep_StampPlaceholder"]);

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
