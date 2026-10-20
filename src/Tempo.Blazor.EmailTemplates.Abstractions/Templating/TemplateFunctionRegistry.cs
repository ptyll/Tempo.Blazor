using System.Text.RegularExpressions;
using Scriban;
using Scriban.Runtime;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// The snapshot of every registered <see cref="ITemplateFunctionProvider"/>: a validated
/// namespace → function map. It is built once, when the engine is constructed, so the host cannot
/// change the registry behind a live render, and it is shared by every render of that engine.
/// </summary>
/// <remarks>
/// Registration is fail-fast on purpose: an invalid namespace or function name, a namespace that would
/// shadow a Scriban builtin or another provider, an async delegate, an unsupported delegate signature
/// and documentation of a function that is not exposed all throw <see cref="ArgumentException"/> when
/// the engine is constructed — i.e. when the host's DI container resolves
/// <see cref="Templating.ITemplateEngine"/>, not in the middle of a render.
/// </remarks>
internal sealed partial class TemplateFunctionRegistry
{
    /// <summary>The registry of an engine without a single provider.</summary>
    public static readonly TemplateFunctionRegistry Empty = new(
        new Dictionary<string, IReadOnlyDictionary<string, SandboxedTemplateFunction>>(StringComparer.Ordinal),
        []);

    private TemplateFunctionRegistry(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, SandboxedTemplateFunction>> functions,
        IReadOnlyList<string> namespaces)
    {
        Functions = functions;
        NamespaceNames = namespaces;
    }

    /// <summary>Gets the registered namespaces, in registration order.</summary>
    public IReadOnlyList<string> NamespaceNames { get; }

    /// <summary>Gets the validated functions per namespace.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, SandboxedTemplateFunction>> Functions { get; }

    /// <summary>Gets a value indicating whether the engine has no host functions at all.</summary>
    public bool IsEmpty => NamespaceNames.Count == 0;

    /// <summary>Validates every provider and snapshots its functions.</summary>
    /// <param name="providers">The registered providers (zero to many).</param>
    /// <param name="onFunctionFailure">
    /// Optional callback that receives a function name and the exception its delegate threw; the host
    /// logs it, while the template only ever learns the function name.
    /// </param>
    /// <exception cref="ArgumentException">A provider cannot be exposed to templates.</exception>
    public static TemplateFunctionRegistry Create(
        IEnumerable<ITemplateFunctionProvider>? providers,
        Action<string, Exception>? onFunctionFailure = null)
    {
        if (providers is null)
        {
            return Empty;
        }

        var namespaces = new List<string>();
        var functions = new Dictionary<string, IReadOnlyDictionary<string, SandboxedTemplateFunction>>(StringComparer.Ordinal);

        foreach (var provider in providers)
        {
            if (provider is null)
            {
                continue;
            }

            var @namespace = provider.Namespace;
            ValidateNamespace(@namespace, functions.Keys);

            var exposed = provider.GetFunctions()
                ?? throw new ArgumentException(
                    $"Template function provider '{@namespace}' returned no function dictionary.",
                    nameof(providers));

            var snapshot = new Dictionary<string, SandboxedTemplateFunction>(StringComparer.Ordinal);
            foreach (var (name, function) in exposed)
            {
                ValidateName(@namespace, name);
                if (function is null)
                {
                    throw new ArgumentException(
                        $"Template function '{@namespace}.{name}' has no delegate.", nameof(providers));
                }

                TemplateFunctionResultPolicy.ValidateSignature(@namespace, name, function);
                snapshot[name] = new SandboxedTemplateFunction(@namespace, name, function, onFunctionFailure);
            }

            ValidateDescription(@namespace, provider, snapshot);

            functions.Add(@namespace, snapshot);
            namespaces.Add(@namespace);
        }

        return namespaces.Count == 0 ? Empty : new TemplateFunctionRegistry(functions, namespaces);
    }

    /// <summary>
    /// Builds a fresh <see cref="ScriptObject"/> of the namespace, so every render gets its own scope
    /// (the function objects inside are immutable and therefore shared).
    /// </summary>
    public ScriptObject CreateScope(string @namespace)
    {
        var scope = new ScriptObject();
        foreach (var (name, function) in Functions[@namespace])
        {
            // Read-only: a template must not be able to replace a host function.
            scope.SetValue(name, function, readOnly: true);
        }

        return scope;
    }

    private static void ValidateNamespace(
        string? @namespace,
        IEnumerable<string> alreadyRegistered)
    {
        if (@namespace is null || !NamespacePattern().IsMatch(@namespace))
        {
            throw new ArgumentException(
                $"Template function namespace '{@namespace}' is not valid. A namespace must match " +
                "^[a-z][a-z0-9_]*$ (for example 'app').",
                nameof(ITemplateFunctionProvider.Namespace));
        }

        if (ScribanKeywords.Contains(@namespace))
        {
            throw new ArgumentException(
                $"Template function namespace '{@namespace}' is a Scriban keyword and could never be " +
                "called from a template. Pick a namespace that matches ^[a-z][a-z0-9_]*$ and is not a keyword.",
                nameof(ITemplateFunctionProvider.Namespace));
        }

        if (BuiltinNamespaces.Contains(@namespace))
        {
            throw new ArgumentException(
                $"Template function namespace '{@namespace}' collides with a Scriban builtin namespace " +
                $"({string.Join(", ", BuiltinNamespaces.OrderBy(n => n, StringComparer.Ordinal))}). " +
                "A host function namespace must never override a builtin.",
                nameof(ITemplateFunctionProvider.Namespace));
        }

        if (alreadyRegistered.Contains(@namespace, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Template function namespace '{@namespace}' is already registered by another provider. " +
                "Two providers must not share a namespace.",
                nameof(ITemplateFunctionProvider.Namespace));
        }
    }

    private static void ValidateName(string @namespace, string? name)
    {
        if (name is null || !NamePattern().IsMatch(name))
        {
            throw new ArgumentException(
                $"Template function name '{@namespace}.{name}' is not valid. A function name must match " +
                "^[a-z][a-z0-9_]*$ (for example 'format_currency').",
                nameof(ITemplateFunctionProvider.GetFunctions));
        }
    }

    private static void ValidateDescription(
        string @namespace,
        ITemplateFunctionProvider provider,
        IReadOnlyDictionary<string, SandboxedTemplateFunction> snapshot)
    {
        var described = provider.Describe();
        if (described is null)
        {
            return;
        }

        foreach (var info in described)
        {
            if (info is null)
            {
                continue;
            }

            ValidateName(@namespace, info.Name);
            if (!snapshot.ContainsKey(info.Name))
            {
                throw new ArgumentException(
                    $"Template function documentation of '{@namespace}.{info.Name}' describes a function " +
                    "that GetFunctions() does not expose.",
                    nameof(ITemplateFunctionProvider.Describe));
            }
        }
    }

    /// <summary>
    /// Words the Scriban parser treats as keywords (statements, literals and loop pseudo-objects). A
    /// namespace with such a name could never be reached from a template, so it is refused loudly at
    /// registration instead of silently doing nothing.
    /// </summary>
    private static readonly HashSet<string> ScribanKeywords = new(StringComparer.Ordinal)
    {
        "for", "this", "if", "end", "func", "null", "true", "false", "in", "while", "case", "when",
        "else", "ret", "break", "continue", "capture", "readonly", "import", "with", "wrap", "tablerow",
        "empty",
    };

    /// <summary>
    /// The namespaces Scriban ships. They are listed explicitly (and unioned with the namespaces a live
    /// <see cref="TemplateContext"/> reports) so a Scriban upgrade can never let a host namespace
    /// override a builtin.
    /// </summary>
    private static readonly HashSet<string> BuiltinNamespaces = CreateBuiltinNamespaces();

    private static HashSet<string> CreateBuiltinNamespaces()
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal)
        {
            "array", "blank", "date", "empty", "html", "include", "include_join", "math", "object",
            "regex", "string", "timespan",
        };

        foreach (var name in new TemplateContext().BuiltinObject.Keys)
        {
            namespaces.Add(name);
        }

        return namespaces;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NamespacePattern();

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NamePattern();
}
