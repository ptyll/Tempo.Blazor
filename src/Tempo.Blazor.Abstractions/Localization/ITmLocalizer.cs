namespace Tempo.Blazor.Localization;

/// <summary>
/// Localization abstraction for Tempo.Blazor components.
/// All built-in component strings go through this interface.
///
/// Registration: AddTempoBlazor() registers DefaultTmLocalizer automatically.
///
/// Override: Implement this interface and register it AFTER AddTempoBlazor() to
/// replace all component strings with your own translations:
/// <code>
/// builder.Services.AddTempoBlazor();
/// builder.Services.AddSingleton&lt;ITmLocalizer, MyCustomTmLocalizer&gt;();
/// </code>
/// </summary>
public interface ITmLocalizer
{
    /// <summary>Gets a localized string by resource key.</summary>
    string this[string key] { get; }

    /// <summary>Gets a formatted localized string with arguments.</summary>
    string this[string key, params object[] arguments] { get; }
}
