using Microsoft.AspNetCore.Components.Web;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Keyboard flags that differ across the supported target frameworks.
/// </summary>
internal static class KeyboardEventArgsExtensions
{
    /// <summary>
    /// True while an IME composition is in progress. The <see cref="KeyboardEventArgs.IsComposing"/>
    /// flag does not exist in the 8.x framework assemblies, so on net8.0 this is always
    /// <c>false</c> — an IME's commit-Enter there still activates (accepted, undetectable; the same
    /// is true of Safari's keyCode-229 commit-Enter on every target).
    /// </summary>
    public static bool IsImeComposing(this KeyboardEventArgs e) =>
#if NET9_0_OR_GREATER
        e.IsComposing;
#else
        false;
#endif
}
