namespace Tempo.Blazor.EmailTemplates;

/// <summary>
/// Client-side validation the editor applies to a picked image <b>before</b> it hands the file to
/// <c>OnImageUpload</c>, so a host never pays for an upload it would reject. A rejected file leaves the
/// block's <c>Src</c> untouched and shows a localized error in the property panel.
/// </summary>
public sealed class ImageUploadOptions
{
    /// <summary>
    /// Gets or sets the accepted MIME types, comma separated, with optional <c>*</c> wildcards
    /// (the <c>accept</c> attribute syntax). Defaults to <c>image/*</c>.
    /// </summary>
    public string Accept { get; set; } = "image/*";

    /// <summary>
    /// Gets or sets the largest accepted file size in bytes. Defaults to 5 MB — the same bound the
    /// template import uses.
    /// </summary>
    public long MaxBytes { get; set; } = 5 * 1024 * 1024;
}
