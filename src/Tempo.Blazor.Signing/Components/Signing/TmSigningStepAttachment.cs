namespace Tempo.Blazor.Components.Signing;

/// <summary>Uploaded attachment value used by signing attachment steps.</summary>
public class TmSigningStepAttachment
{
    /// <summary>Stable attachment identifier.</summary>
    public string Uuid { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Original file name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Host-provided download/storage URL (the upload result); never a client-generated preview.</summary>
    public string? Url { get; set; }

    /// <summary>Optional content type.</summary>
    public string? ContentType { get; set; }

    /// <summary>File size in bytes.</summary>
    public long Size { get; set; }
}
