using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Extension methods for safe JS interop calls that handle prerendering,
/// circuit disconnection, and component disposal gracefully.
/// </summary>
internal static class JsInteropGuard
{
    /// <summary>
    /// Invokes a JS void function, swallowing exceptions that occur during
    /// prerendering, circuit disconnect, or component disposal.
    /// </summary>
    internal static async ValueTask SafeInvokeVoidAsync(
        this IJSRuntime js, string identifier, params object?[] args)
    {
        try
        {
            await js.InvokeVoidAsync(identifier, args);
        }
        catch (InvalidOperationException) { }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }

    /// <summary>
    /// Invokes a JS function with a return value, returning the fallback
    /// on prerendering, circuit disconnect, or component disposal.
    /// </summary>
    internal static async ValueTask<T> SafeInvokeAsync<T>(
        this IJSRuntime js, string identifier, T fallback, params object?[] args)
    {
        try
        {
            return await js.InvokeAsync<T>(identifier, args);
        }
        catch (InvalidOperationException) { return fallback; }
        catch (JSDisconnectedException) { return fallback; }
        catch (TaskCanceledException) { return fallback; }
    }
}
