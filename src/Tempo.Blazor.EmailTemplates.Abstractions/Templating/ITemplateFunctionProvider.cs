namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>A function a provider exposes, for validation and docs.</summary>
/// <param name="Name">The script name of the function (it matches <c>^[a-z][a-z0-9_]*$</c>).</param>
/// <param name="Description">Optional human-readable description; never used by the renderer.</param>
public sealed record TemplateFunctionInfo(string Name, string? Description);

/// <summary>
/// Host-supplied template functions under one namespace. A provider lets an application add its own
/// filters (for example currency formatting) without forking the engine: the functions are registered
/// under <see cref="Namespace"/> and called from a template as <c>{{ value | app.my_filter }}</c>.
/// </summary>
/// <remarks>
/// <para>
/// The engine snapshots the provider once, when it is constructed, so a later mutation of the returned
/// dictionary cannot change a render. Register providers as
/// <see cref="ITemplateFunctionProvider"/> services; <c>AddTempoEmailTemplateEngine</c> forwards every
/// registration to <c>ScribanTemplateEngine</c>.
/// </para>
/// <para>
/// Security: a provider can never override a Scriban builtin namespace (<c>string</c>, <c>array</c>,
/// <c>object</c>, <c>math</c>, <c>date</c>, <c>html</c>, <c>regex</c>, <c>timespan</c>, …) nor another
/// provider — such a registration throws <see cref="ArgumentException"/> when the engine is
/// constructed. The delegates are validated too: only synchronous delegates whose parameters and
/// return value are <see cref="string"/>, <see cref="bool"/>, a numeric type, <see cref="decimal"/>,
/// <see cref="DateTime"/>, <see cref="DateTimeOffset"/> or an
/// <see cref="IEnumerable{T}"/> of those are accepted, so a provider cannot hand out a
/// <see cref="Type"/>, a delegate or a value of a forbidden namespace. A function result still runs
/// through the same sandbox checks and the same automatic HTML escaping as any other expression.
/// </para>
/// <para>
/// Register the provider as a <b>singleton</b> (<c>AddSingleton&lt;ITemplateFunctionProvider, MyProvider&gt;()</c>):
/// the engine snapshots the functions once when it is constructed, so a scoped or transient registration
/// is refused with an <see cref="InvalidOperationException"/> instead of silently capturing the root
/// instance. Because the engine itself is a singleton, the same delegate instance can be invoked by
/// concurrent renders — a provider must be stateless or take care of its own synchronization.
/// </para>
/// </remarks>
public interface ITemplateFunctionProvider
{
    /// <summary>Namespace the functions are imported under, for example <c>app</c> (matches <c>^[a-z][a-z0-9_]*$</c>).</summary>
    string Namespace { get; }

    /// <summary>
    /// Function name → delegate. Delegates are <b>synchronous</b>; names match
    /// <c>^[a-z][a-z0-9_]*$</c>; parameters and return types are <see cref="string"/>,
    /// <see cref="bool"/>, a numeric type, <see cref="decimal"/>,
    /// <see cref="DateTime"/>, <see cref="DateTimeOffset"/> or an <see cref="IEnumerable{T}"/> of
    /// those. Results run inside the same member filter and limits as everything else.
    /// </summary>
    IReadOnlyDictionary<string, Delegate> GetFunctions();

    /// <summary>
    /// Optional documentation of the exposed functions (name plus description). It must describe the
    /// functions <see cref="GetFunctions"/> actually exposes — a documented name that is not exposed
    /// fails the registration.
    /// </summary>
    IReadOnlyList<TemplateFunctionInfo> Describe();
}
