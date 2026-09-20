namespace Tempo.Blazor.Components.Overlay;

/// <summary>Preferred side of the anchor a <see cref="TmOverlayPanel"/> opens on.</summary>
public enum OverlayPlacement
{
    /// <summary>Below the anchor (flips above when there is not enough room).</summary>
    Bottom,

    /// <summary>Above the anchor (flips below when there is not enough room).</summary>
    Top,

    /// <summary>To the left of the anchor (flips right when there is not enough room).</summary>
    Left,

    /// <summary>To the right of the anchor (flips left when there is not enough room).</summary>
    Right
}

/// <summary>Cross-axis alignment of a <see cref="TmOverlayPanel"/> against its anchor.</summary>
public enum OverlayAlign
{
    /// <summary>Panel start edge aligns with the anchor start edge (left edge for top/bottom placement).</summary>
    Start,

    /// <summary>Panel is centred on the anchor on the cross axis.</summary>
    Center,

    /// <summary>Panel end edge aligns with the anchor end edge (right edge for top/bottom placement).</summary>
    End
}
