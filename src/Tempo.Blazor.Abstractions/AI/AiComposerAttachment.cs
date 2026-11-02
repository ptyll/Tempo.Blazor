namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One file the reader attached to a prompt in a composer. The record carries the metadata a submit
/// contract needs (a backend can render a chip and a size without Blazor) plus an opaque
/// <see cref="Content"/> slot the host uses to keep the browser file, a stream or a blob handle.
/// </summary>
/// <remarks>
/// The composer never reads <see cref="Content"/>: it is the host's payload, so the same record can be
/// produced by a WebAssembly host (an <c>IBrowserFile</c>), a Server host (a buffered stream) or a test
/// (any object). Reading the bytes, uploading them and deciding what to do with the file is the host's
/// job — the composer only enforces <c>MaxAttachments</c> and <c>MaxFileSize</c> on the metadata it can
/// see and renders the chips.
/// </remarks>
public sealed class AiComposerAttachment
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;

    /// <summary>Stable attachment identifier; the chip's Remove control addresses it.</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>File name shown on the chip and used for the Remove control's accessible name.</summary>
    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    /// <summary>File size in bytes, as reported by the browser.</summary>
    public long SizeBytes { get; set; }

    /// <summary>MIME content type when the browser reported one.</summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Optional preview or download URL. A host that uploads the file immediately can point the chip at
    /// the stored asset instead of keeping the bytes.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Opaque host payload — typically the <c>IBrowserFile</c> the reader picked, or an already-open
    /// stream. The composer copies it into <see cref="AiComposerSubmit.Attachments"/> untouched and never
    /// inspects it.
    /// </summary>
    public object? Content { get; set; }
}
