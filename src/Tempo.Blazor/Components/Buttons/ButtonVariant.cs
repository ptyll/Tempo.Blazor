namespace Tempo.Blazor.Components.Buttons;

/// <summary>Visual style variants for TmButton.</summary>
public enum ButtonVariant
{
    /// <summary>Primary.</summary>
    Primary,
    /// <summary>Secondary.</summary>
    Secondary,
    /// <summary>Ghost.</summary>
    Ghost,
    /// <summary>Danger.</summary>
    Danger,
    /// <summary>Outline.</summary>
    Outline,
    /// <summary>Primary outline. Transparent fill, primary border and ink.</summary>
    OutlinePrimary,
    /// <summary>Link.</summary>
    Link,
    /// <summary>Default.</summary>
    Default,
    /// <summary>Neutral outline.</summary>
    OutlineSecondary,
    /// <summary>Warning.</summary>
    Warning,
    /// <summary>Warning outline.</summary>
    OutlineWarning,
    /// <summary>Primary-subtle fill with primary ink (2.12.0). The low-key emphasis a toolbar
    /// or a panel of quiet actions uses instead of a full primary fill.</summary>
    Soft,
    /// <summary>Quiet destructive outline (2.12.0). Transparent fill, danger-text ink and a soft danger border,
    /// with a danger-subtle wash on hover. The row-level Delete action that should not shout like <see cref="Danger"/>.
    /// Both themes are covered by tokens that flip with the theme.</summary>
    OutlineDanger
}
