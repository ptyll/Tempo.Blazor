namespace Tempo.Blazor.Components.Inputs;

/// <summary>Which characters <see cref="TmOtpInput"/> accepts per cell and the keyboard it presents.</summary>
public enum OtpInputKind
{
    /// <summary>ASCII digits 0–9 only. Presents the numeric keyboard (<c>inputmode="numeric"</c>).</summary>
    Numeric,

    /// <summary>ASCII letters (normalized to uppercase) and digits. Presents the standard keyboard.</summary>
    Alphanumeric
}
