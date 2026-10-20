using System.Text.RegularExpressions;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// The BCL baseline <see cref="ITemplateVariableExtractor"/> (§3.0). It reads <c>{{ path }}</c>,
/// <c>{{ path.member }}</c> and <c>for … in &lt;path&gt;</c> tokens — the forms the editor's variable
/// picker and sample-data generator need — without any Scriban dependency, so the picker keeps working
/// in a WASM editor that has no Engine package. Loop-local variables are excluded and a collection is
/// reported as <see cref="VariableKind.Collection"/>.
/// </summary>
/// <remarks>
/// This is a detection heuristic, not a parser: it deliberately under-reports (a variable that appears
/// only as a filter argument is not found) and never throws. The authoritative extractor is
/// <c>ScribanTemplateVariableExtractor</c> in <c>Tempo.Blazor.EmailTemplates.Engine</c>, which walks
/// the Scriban AST.
/// </remarks>
public sealed partial class RegexTemplateVariableExtractor : ITemplateVariableExtractor
{
    /// <summary>The shared, stateless instance.</summary>
    public static RegexTemplateVariableExtractor Instance { get; } = new();

    /// <summary>Words that are Scriban keywords or loop pseudo-objects, never data paths.</summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "for", "in", "end", "endfor", "if", "else", "elsif", "unless", "case", "when", "break",
        "continue", "while", "tablerow", "with", "import", "include", "func", "ret", "and", "or",
        "not", "true", "false", "null", "nil", "empty", "blank", "this", "loop", "do", "as",
        "offset", "limit", "reversed", "params", "echo",
    };

    /// <inheritdoc />
    public IReadOnlyList<string> Extract(string template)
        => ExtractInfos(template).Select(info => info.Path).ToList();

    /// <inheritdoc />
    public IReadOnlyList<TemplateVariableInfo> ExtractInfos(string template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return [];
        }

        var paths = new List<string>();
        var kinds = new Dictionary<string, VariableKind>(StringComparer.Ordinal);
        var locals = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match token in TokenPattern().Matches(template))
        {
            var content = StripLiterals(token.Groups["body"].Value);
            var forMatch = ForPattern().Match(content);
            if (forMatch.Success)
            {
                var iterator = forMatch.Groups["path"].Value;
                Record(iterator, VariableKind.Collection, paths, kinds, locals);

                var local = forMatch.Groups["variable"].Value;
                if (local.Length > 0)
                {
                    locals.Add(local);
                }

                continue;
            }

            foreach (Match identifier in IdentifierPattern().Matches(content))
            {
                // A filter name (`| math.format`) and its dotted path are not data.
                if (IsPrecededByPipe(content, identifier.Index))
                {
                    continue;
                }

                var path = identifier.Value;
                var root = path.Split('.')[0];
                if (Keywords.Contains(root) || locals.Contains(root))
                {
                    continue;
                }

                Record(path, VariableKind.Scalar, paths, kinds, locals);
            }
        }

        return paths.Select(path => new TemplateVariableInfo(path, kinds[path])).ToList();
    }

    private static bool IsPrecededByPipe(string content, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(content[i]))
            {
                continue;
            }

            return content[i] == '|';
        }

        return false;
    }

    private static void Record(
        string path,
        VariableKind kind,
        List<string> paths,
        Dictionary<string, VariableKind> kinds,
        HashSet<string> locals)
    {
        if (path.Length == 0 || locals.Contains(path))
        {
            return;
        }

        var root = path.Split('.')[0];
        if (Keywords.Contains(root))
        {
            return;
        }

        Add(root, kind, paths, kinds);
        Add(path, kind, paths, kinds);
    }

    private static void Add(string path, VariableKind kind, List<string> paths, Dictionary<string, VariableKind> kinds)
    {
        if (!kinds.TryGetValue(path, out var existing))
        {
            paths.Add(path);
            kinds[path] = kind;
            return;
        }

        if (kind == VariableKind.Collection)
        {
            kinds[path] = VariableKind.Collection;
        }
    }

    /// <summary>A Scriban expression (<c>{{ … }}</c>) or block tag (<c>{% … %}</c>).</summary>
    [GeneratedRegex(@"\{\{(?<body>.*?)\}\}|\{%(?<body>.*?)%\}", RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TokenPattern();

    /// <summary>A <c>for &lt;variable&gt; in &lt;path&gt;</c> header, in both the tag and inline form.</summary>
    [GeneratedRegex(@"^\s*for\s+(?<variable>[A-Za-z_][A-Za-z0-9_]*)\s+in\s+(?<path>[A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ForPattern();

    /// <summary>A dotted identifier chain; string literals are removed before matching.</summary>
    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IdentifierPattern();

    /// <summary>A quoted string literal (single or double quotes, backslash escapes).</summary>
    [GeneratedRegex(@"""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StringLiteralPattern();

    private static string StripLiterals(string content)
        => content.IndexOf('"') < 0 && content.IndexOf('\'') < 0
            ? content
            : StringLiteralPattern().Replace(content, " ");
}
