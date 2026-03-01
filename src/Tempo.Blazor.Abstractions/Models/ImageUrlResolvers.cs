using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Models;

/// <summary>
/// Factory methods and built-in implementations of IImageUrlResolver.
/// </summary>
public static class ImageUrlResolvers
{
    /// <summary>
    /// Returns a resolver that treats the imageId as a direct URL (no API call).
    /// Use when IGalleryImage.Url is already set and accessible.
    /// </summary>
    public static IImageUrlResolver Direct() => DirectImageUrlResolver.Instance;

    /// <summary>
    /// Wraps an async function as an IImageUrlResolver.
    /// Useful for inline resolver definitions without creating a separate class.
    /// </summary>
    /// <example>
    /// <code>
    /// UrlResolver="@ImageUrlResolvers.FromFunc(id => api.GetTicketUrlAsync(id))"
    /// </code>
    /// </example>
    public static IImageUrlResolver FromFunc(Func<string, Task<string>> resolver) =>
        new FuncImageUrlResolver((id, _) => resolver(id));

    /// <summary>
    /// Wraps an async function with cancellation support as an IImageUrlResolver.
    /// </summary>
    public static IImageUrlResolver FromFunc(Func<string, CancellationToken, Task<string>> resolver) =>
        new FuncImageUrlResolver(resolver);
}

/// <summary>
/// Resolver that treats imageId as a direct URL (no API call).
/// Default when no UrlResolver is provided to gallery components.
/// </summary>
public sealed class DirectImageUrlResolver : IImageUrlResolver
{
    /// <summary>Singleton instance.</summary>
    public static readonly DirectImageUrlResolver Instance = new();

    private DirectImageUrlResolver() { }

    public Task<string> ResolveAsync(string imageId, CancellationToken ct = default) =>
        Task.FromResult(imageId);
}

/// <summary>
/// Resolver backed by a delegate function. Created via ImageUrlResolvers.FromFunc().
/// </summary>
public sealed class FuncImageUrlResolver : IImageUrlResolver
{
    private readonly Func<string, CancellationToken, Task<string>> _func;

    internal FuncImageUrlResolver(Func<string, CancellationToken, Task<string>> func) =>
        _func = func;

    public Task<string> ResolveAsync(string imageId, CancellationToken ct = default) =>
        _func(imageId, ct);
}
