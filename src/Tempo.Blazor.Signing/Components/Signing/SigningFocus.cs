using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// Best-effort focus moves for the signing surfaces. A view whose JS circuit is already gone
/// keeps the interop failure local instead of crashing the host — the same contract the shared
/// overflow menu's focus moves follow.
/// </summary>
internal static class SigningFocus
{
    /// <summary>
    /// Focuses <paramref name="element"/> and swallows the interop failures a disposed or
    /// never-interactive view produces (<see cref="JSException"/>,
    /// <see cref="ObjectDisposedException"/>, <see cref="TaskCanceledException"/>,
    /// <see cref="InvalidOperationException"/> — no live JS runtime behind the element reference).
    /// </summary>
    public static async Task TryFocusAsync(ElementReference element)
    {
        try
        {
            await element.FocusAsync();
        }
        catch (JSException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (TaskCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
            // No live JS runtime behind the element reference (disposed view / static render).
        }
    }
}
