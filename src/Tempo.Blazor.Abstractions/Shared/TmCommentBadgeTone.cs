namespace Tempo.Blazor.Abstractions.Shared;

/// <summary>Visual tone of a comment badge. Mapped to a badge variant by the rendering component.</summary>
public enum TmCommentBadgeTone
{
    /// <summary>Neutral tone. The default.</summary>
    Neutral,

    /// <summary>Informational tone.</summary>
    Info,

    /// <summary>Positive or completed tone.</summary>
    Success,

    /// <summary>Cautionary tone.</summary>
    Warning,

    /// <summary>Error or destructive tone.</summary>
    Danger,

    /// <summary>Emphasised tone that is not a status.</summary>
    Accent
}
