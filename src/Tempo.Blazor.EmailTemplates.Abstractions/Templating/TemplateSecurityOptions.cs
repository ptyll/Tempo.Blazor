namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// Sandbox limits applied when rendering user-authored templates, bounding loops, recursion, output
/// size, string conversion, regular expressions and wall-clock time so a malicious or buggy template
/// cannot hang, exhaust memory or trigger catastrophic regex backtracking. Every limit must be
/// positive; <see cref="Validate"/> rejects anything else with an
/// <see cref="ArgumentOutOfRangeException"/> (zero never means "unlimited" here — that would be a
/// fail-open configuration).
/// </summary>
public sealed class TemplateSecurityOptions
{
    /// <summary>Maximum total loop iterations before rendering aborts (default 5000).</summary>
    public int LoopLimit { get; set; } = 5000;

    /// <summary>Maximum recursion depth before rendering aborts (default 100).</summary>
    public int RecursiveLimit { get; set; } = 100;

    /// <summary>Maximum length of the rendered output in characters (default 5,000,000).</summary>
    public int MaxOutputLength { get; set; } = 5_000_000;

    /// <summary>
    /// Maximum length of a single string produced by converting a value to text, in UTF-16 characters
    /// (default 1,048,576). Exceeding it aborts the render instead of silently truncating.
    /// </summary>
    public int LimitToString { get; set; } = 1_048_576;

    /// <summary>
    /// Maximum recursion depth while converting an object graph to text (default 20), guarding deep or
    /// self-referencing values handed to the renderer as the model.
    /// </summary>
    public int ObjectRecursionLimit { get; set; } = 20;

    /// <summary>Maximum wall-clock time for a single render (default 5 seconds, cooperative).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Timeout applied to every regular expression evaluated by the <c>regex</c> filters (default one
    /// second). Protects against catastrophic backtracking in a user-authored pattern.
    /// <see cref="System.Text.RegularExpressions.Regex.InfiniteMatchTimeout"/> is rejected.
    /// </summary>
    public TimeSpan RegexTimeOut { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// When <see langword="true"/>, referencing an undefined variable is an error; otherwise it
    /// renders as empty (the default, lenient behaviour).
    /// </summary>
    public bool StrictVariables { get; set; }

    /// <summary>
    /// Namespaces of the registered <see cref="ITemplateFunctionProvider"/>s (D6). The engine adds every
    /// provider namespace to its own copy, so a host-side template validator can tell a host function
    /// namespace (<c>app.format_currency</c>) from a data path and tolerate the unknown members of
    /// exactly these namespaces. It never widens the sandbox: the namespaces are validated when the
    /// engine is constructed and the member filter, the limits and the escaping keep applying.
    /// </summary>
    public ISet<string> FunctionNamespaces { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Creates the engine's private snapshot of the options, so mutating the instance a host passed to
    /// the constructor can never weaken a later render. Reference-typed members (for example
    /// <see cref="FunctionNamespaces"/>) are copied deeply, never shared.
    /// </summary>
    internal TemplateSecurityOptions Clone()
    {
        var clone = (TemplateSecurityOptions)MemberwiseClone();
        clone.FunctionNamespaces = FunctionNamespaces is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(FunctionNamespaces, StringComparer.Ordinal);
        return clone;
    }

    /// <summary>
    /// Rejects every non-positive limit and every <see cref="TimeSpan"/> that is not strictly
    /// positive. Called when the engine is constructed and when the engine is registered in DI, so a
    /// fail-open configuration cannot reach a render.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A limit is zero or negative.</exception>
    internal void Validate()
    {
        RequirePositive(LoopLimit, nameof(LoopLimit));
        RequirePositive(RecursiveLimit, nameof(RecursiveLimit));
        RequirePositive(MaxOutputLength, nameof(MaxOutputLength));
        RequirePositive(LimitToString, nameof(LimitToString));
        RequirePositive(ObjectRecursionLimit, nameof(ObjectRecursionLimit));
        RequirePositive(Timeout, nameof(Timeout));
        RequirePositive(RegexTimeOut, nameof(RegexTimeOut));
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                name, value, $"Template sandbox limit '{name}' must be greater than zero.");
        }
    }

    private static void RequirePositive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                name, value, $"Template sandbox limit '{name}' must be greater than zero.");
        }
    }
}
