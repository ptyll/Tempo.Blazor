using System.Text.RegularExpressions;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// Pre-render scan of the code spans of a template (<c>{{ … }}</c> and <c>{% … %}</c>). Literal text
/// is never scanned, so an e-mail body that merely mentions "Assembly" keeps rendering, while a
/// template that references reflection, an environment/file root, an evaluating builtin or the
/// <c>include</c> statement is refused before anything is evaluated. The scan is the loud half of the
/// sandbox: <see cref="SandboxMemberPolicy"/> already makes the same accesses impossible, the scan
/// turns them into a render error instead of an empty string.
/// </summary>
internal static partial class SandboxTemplateScanner
{
    /// <summary>One refused construct found in the template source.</summary>
    /// <param name="Kind">How the construct was classified.</param>
    /// <param name="Detail">The refused member, namespace or function name.</param>
    /// <param name="Index">Zero-based character index of the construct in the template.</param>
    internal readonly record struct Violation(TemplateSecurityEventKind Kind, string Detail, int Index);

    /// <summary>Scans the code spans of a template and returns every distinct refused construct.</summary>
    public static IReadOnlyList<Violation> Scan(string template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return [];
        }

        List<Violation>? violations = null;
        foreach (var (code, start) in ExtractCodeSpans(template))
        {
            Collect(code, start, ref violations);
        }

        return violations ?? [];
    }

    private static void Collect(string code, int start, ref List<Violation>? violations)
    {
        // Data is not code: blank out the contents of every string literal (keeping the length, so
        // every match index still points into the original template) before scanning. Otherwise an
        // e-mail body that says "Prices include VAT" or "see GetType" would be refused.
        var source = BlankStringLiterals(code);

        foreach (Match match in NamespacePattern().Matches(source))
        {
            Add(ref violations, TemplateSecurityEventKind.BlockedNamespace, Compact(match.Value), start + match.Index);
        }

        foreach (Match match in EnvironmentRootPattern().Matches(source))
        {
            Add(ref violations, TemplateSecurityEventKind.BlockedNamespace, match.Groups["root"].Value, start + match.Index);
        }

        foreach (Match match in FileFunctionPattern().Matches(source))
        {
            Add(ref violations, TemplateSecurityEventKind.BlockedFunction, Compact(match.Value), start + match.Index);
        }

        foreach (Match match in EvaluatingFunctionPattern().Matches(source))
        {
            Add(ref violations, TemplateSecurityEventKind.BlockedFunction, Compact(match.Value), start + match.Index);
        }

        foreach (Match match in IncludeStatementPattern().Matches(source))
        {
            Add(ref violations, TemplateSecurityEventKind.BlockedFunction, match.Value, start + match.Index);
        }

        foreach (Match match in IdentifierPattern().Matches(source))
        {
            if (SandboxMemberPolicy.IsScannedName(match.Value))
            {
                Add(ref violations, TemplateSecurityEventKind.BlockedMember, match.Value, start + match.Index);
            }
        }
    }

    private static string BlankStringLiterals(string code)
        => StringLiteralPattern().Replace(code, static match => new string(' ', match.Length));

    private static void Add(
        ref List<Violation>? violations,
        TemplateSecurityEventKind kind,
        string detail,
        int index)
    {
        violations ??= [];
        foreach (var existing in violations)
        {
            if (existing.Kind == kind && string.Equals(existing.Detail, detail, StringComparison.Ordinal))
            {
                return;
            }
        }

        violations.Add(new Violation(kind, detail, index));
    }

    private static string Compact(string value) => WhitespacePattern().Replace(value, string.Empty).TrimStart('@');

    private static IEnumerable<(string Code, int Start)> ExtractCodeSpans(string template)
    {
        foreach (Match match in ExpressionSpanPattern().Matches(template))
        {
            yield return (match.Groups["code"].Value, match.Groups["code"].Index);
        }

        foreach (Match match in BlockSpanPattern().Matches(template))
        {
            yield return (match.Groups["code"].Value, match.Groups["code"].Index);
        }
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"\{\{(?<code>.*?)\}\}", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ExpressionSpanPattern();

    [GeneratedRegex(@"\{%(?<code>.*?)%\}", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex BlockSpanPattern();

    [GeneratedRegex(
        @"@?System\s*\.\s*(?:Reflection|IO|Diagnostics|Net|Runtime|Threading)(?:\s*\.\s*[A-Za-z_]\w*)*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamespacePattern();

    /// <summary>
    /// The environment root (<c>env.</c>) only. A bare <c>environment</c> is a plausible model key
    /// (for example <c>{{ environment.region }}</c> in a multi-tenant e-mail) and is data, not code.
    /// </summary>
    [GeneratedRegex(
        @"(?<![\w.])(?<root>env)\s*\.",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentRootPattern();

    [GeneratedRegex(
        @"(?<![\w.])file\s*\.\s*(?:read|write|exists|append|delete|copy|move|open|info)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileFunctionPattern();

    [GeneratedRegex(
        @"(?<![\w.])object\s*\.\s*(?:eval_template|eval)\b|(?<![\w.])eval_template\b|(?<![\w.])eval\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex EvaluatingFunctionPattern();

    [GeneratedRegex(@"(?<![\w.])include\b", RegexOptions.CultureInvariant)]
    private static partial Regex IncludeStatementPattern();

    [GeneratedRegex(@"[A-Za-z_]\w*", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    /// <summary>
    /// A double-quoted, single-quoted or backtick string literal, with backslash escapes. Used only to
    /// mask literal contents before scanning; it is deliberately simple (the scan is not a parser).
    /// </summary>
    [GeneratedRegex(@"""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|`[^`]*`", RegexOptions.CultureInvariant)]
    private static partial Regex StringLiteralPattern();
}
