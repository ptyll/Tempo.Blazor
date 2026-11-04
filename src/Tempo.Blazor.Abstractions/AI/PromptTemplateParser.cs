using System.Text;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.AI.Models;

/// <summary>
/// The pure assembly engine of a prompt library: finds the <c>{{variable}}</c> placeholders of a
/// <see cref="PromptTemplate"/>, and substitutes values into its body.
/// <para>
/// The parser is deliberately small and boring. It substitutes TEXT and nothing else: <c>{{1 + 2}}</c> is not an
/// expression (it is literal text, because it is not a variable name), a value that happens to contain
/// <c>{{name}}</c> is never re-expanded, and no culture, path, JSON or template engine is involved. A placeholder
/// the template does not declare is literal text too, so an explicit
/// <see cref="PromptTemplate.Variables"/> declaration is authoritative.
/// </para>
/// <para>
/// Two escapes let a body SHOW a placeholder instead of filling one: <c>{{{{</c> renders <c>{{</c> and
/// <c>}}}}</c> renders <c>}}</c>. A single, unmatched pair stays literal.
/// </para>
/// <para>
/// The assembly is always produced — a prompt with an unfilled required variable keeps the literal placeholder —
/// and the reasons it is incomplete travel next to it as <see cref="PromptTemplateBuildResult.Errors"/>. The
/// parser owns no wording and no localization; the prompt library turns the facts into messages.
/// </para>
/// </summary>
public static class PromptTemplateParser
{
    /// <summary>
    /// A placeholder name: a letter, a digit or an underscore, then letters, digits, underscores, dots and hyphens.
    /// Unicode letters are allowed, so a Czech template can spell <c>{{téma}}</c>.
    /// </summary>
    private static readonly Regex NamePattern = new(
        @"^[\p{L}\p{N}_][\p{L}\p{N}_.\-]*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly IReadOnlyDictionary<string, string?> NoValues =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>
    /// The variables a body spells, in order of first appearance, one per distinct name (a repeated placeholder is
    /// the same variable). Each derived variable is required, single-line and labelled by its name — the shape a
    /// template without an explicit declaration gets.
    /// </summary>
    /// <param name="body">The template body. Null or blank yields no variables.</param>
    /// <returns>The derived variables; never null.</returns>
    public static IReadOnlyList<PromptVariable> ExtractVariables(string? body)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in Tokenize(body ?? string.Empty))
        {
            if (token.Name is { } name && seen.Add(name))
            {
                names.Add(name);
            }
        }

        // The derived label is the name itself, spelled out, so the variable's Label is never null and a host can
        // persist the derived declaration and read the same thing back.
        return names.ConvertAll(name => new PromptVariable(name, name));
    }

    /// <summary>
    /// The variables an assembly uses: the template's own declaration when it has one, otherwise the ones derived
    /// from its body. The declaration WINS entirely — a placeholder it does not name is literal text.
    /// </summary>
    /// <param name="template">The template. A null argument throws.</param>
    /// <returns>The variables, in declaration (or first-appearance) order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> is null.</exception>
    public static IReadOnlyList<PromptVariable> ResolveVariables(PromptTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return template.Variables is { Count: > 0 } declared ? declared : ExtractVariables(template.Body);
    }

    /// <summary>
    /// Assembles the template with the supplied values.
    /// </summary>
    /// <param name="template">The template to assemble. A null argument throws.</param>
    /// <param name="values">The reader's values by variable name, or null for none. A null/whitespace value counts
    /// as empty, so the default (if any) is used. A name the template does not declare is ignored. Matching is
    /// ordinal (case-sensitive), exactly like the placeholder.</param>
    /// <returns>The assembly: the runs, the prompt, the effective values and every validation error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> is null.</exception>
    public static PromptTemplateBuildResult Build(
        PromptTemplate template,
        IReadOnlyDictionary<string, string?>? values)
    {
        ArgumentNullException.ThrowIfNull(template);

        values ??= NoValues;

        var variables = ResolveVariables(template);
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var effective = new Dictionary<string, string?>(StringComparer.Ordinal);
        var errors = new List<PromptTemplateValidationError>();

        foreach (var variable in variables)
        {
            if (!declared.Add(variable.Name))
            {
                // A duplicate declaration is one variable; the FIRST one is what the reader sees and fills.
                continue;
            }

            string? value = null;
            if (values.TryGetValue(variable.Name, out var supplied) && !string.IsNullOrWhiteSpace(supplied))
            {
                value = supplied;
            }
            else if (!string.IsNullOrWhiteSpace(variable.DefaultValue))
            {
                value = variable.DefaultValue;
            }

            if (value is null)
            {
                if (variable.IsRequired)
                {
                    errors.Add(new PromptTemplateValidationError(
                        variable.Name, PromptTemplateValidationErrorKind.MissingRequired));
                }

                continue;
            }

            effective[variable.Name] = value;

            if (variable.Kind == PromptVariableKind.Select
                && variable.Options is { Count: > 0 } options
                && !options.Contains(value, StringComparer.Ordinal))
            {
                errors.Add(new PromptTemplateValidationError(
                    variable.Name, PromptTemplateValidationErrorKind.NotAnOption));
            }
        }

        var segments = new List<PromptTemplateSegment>();
        var prompt = new StringBuilder();
        foreach (var token in Tokenize(template.Body))
        {
            PromptTemplateSegment segment;
            if (token.Name is { } name && declared.Contains(name))
            {
                segment = effective.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
                    ? PromptTemplateSegment.Variable(name, value, isEmpty: false)
                    : PromptTemplateSegment.Variable(name, token.Raw, isEmpty: true);
            }
            else
            {
                segment = new PromptTemplateSegment(token.Raw);
            }

            segments.Add(segment);
            prompt.Append(segment.Text);
        }

        return new PromptTemplateBuildResult
        {
            Variables = variables,
            Segments = segments,
            Prompt = prompt.ToString(),
            Values = effective,
            Errors = errors,
        };
    }

    /// <summary>
    /// Splits a body into literal and placeholder runs. The escapes are resolved here, so a literal run already
    /// carries the single braces the reader should see.
    /// </summary>
    /// <param name="body">The template body.</param>
    /// <returns>The runs, in reading order.</returns>
    private static List<Token> Tokenize(string body)
    {
        var tokens = new List<Token>(4);
        var literal = new StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length == 0)
            {
                return;
            }

            tokens.Add(new Token(literal.ToString(), null));
            literal.Clear();
        }

        var index = 0;
        while (index < body.Length)
        {
            if (body[index] == '{' && index + 1 < body.Length && body[index + 1] == '{')
            {
                // `{{{{` is the escape for a literal `{{`; it is checked FIRST, so an escaped pair can never be
                // mistaken for the opening of a placeholder.
                if (index + 3 < body.Length && body[index + 2] == '{' && body[index + 3] == '{')
                {
                    literal.Append("{{");
                    index += 4;
                    continue;
                }

                var close = body.IndexOf("}}", index + 2, StringComparison.Ordinal);
                if (close > index + 2)
                {
                    var name = body[(index + 2)..close].Trim();
                    if (NamePattern.IsMatch(name))
                    {
                        FlushLiteral();
                        tokens.Add(new Token(body[index..(close + 2)], name));
                        index = close + 2;
                        continue;
                    }
                }

                literal.Append("{{");
                index += 2;
                continue;
            }

            if (body[index] == '}' && index + 3 < body.Length
                && body[index + 1] == '}' && body[index + 2] == '}' && body[index + 3] == '}')
            {
                literal.Append("}}");
                index += 4;
                continue;
            }

            literal.Append(body[index]);
            index++;
        }

        FlushLiteral();
        return tokens;
    }

    /// <summary>One lexical run: its exact source text and, for a placeholder, the variable name.</summary>
    /// <param name="Raw">The source text: the literal run, or the placeholder exactly as the body spelled it.
    /// </param>
    /// <param name="Name">The placeholder name, or null for a literal run.</param>
    private readonly record struct Token(string Raw, string? Name);
}
