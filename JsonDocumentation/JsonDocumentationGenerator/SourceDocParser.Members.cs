using System.Text;
using System.Text.RegularExpressions;

namespace JsonDocumentationGenerator;

/// <summary>
/// Member extraction the <see cref="SourceDocParser.ExtractMembers"/> line loops cannot express:
/// expression-bodied properties (<c>public bool IsMobile => …;</c> — no <c>{</c> and no
/// <c>;</c>-terminated field shape) and positional record parameters (generated public
/// properties with no declaration of their own).
/// <para>
/// Kept in a partial file because <c>PackageDocumentationGenerator.cs</c> sits in the frozen
/// over-1200-line set (SourceFileLengthTests) and must not grow new bulk. The field loop's
/// <see cref="IsExpressionBody"/> guard lives here too: it is the one-line fix that stops
/// <c>public bool IsMobile => X == 1;</c> from being read as a field initializer (the old loop
/// split on the first <c>=</c>, which is the arrow's <c>=</c>).
/// </para>
/// </summary>
internal static partial class SourceDocParser
{
    // `public [modifiers] Type Name =>` — no '(' before the arrow (that is a method or an
    // operator), no indexers (their name is `this`, which cannot precede the arrow).
    // Known limitations: tuple-typed expression-bodied properties and declarations wrapped across lines are not extracted.
    private static readonly Regex ExpressionBodiedPropertyRegex = new(
        @"^public\s+(?:(?:static|virtual|override|sealed|abstract|new|required|readonly)\s+)*(?<type>[^=(){};]+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=>",
        RegexOptions.Compiled,
        GeneratorRegex.MatchTimeout);

    // `record [struct|class] Name[<T>] (` — positional records only. A class primary constructor
    // creates NO properties, so plain `class Name(` must never match; the `record` keyword is the
    // discriminator.
    private static readonly Regex PositionalRecordRegex = new(
        @"\brecord\s+(?:struct\s+|class\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>{};()]+>)?\s*\(",
        RegexOptions.Compiled,
        GeneratorRegex.MatchTimeout);

    /// <summary>
    /// True when the first <c>=</c> of a line is the <c>=&gt;</c> of an expression body — the
    /// signal the field loop uses to leave the line to <see cref="ExtractExpressionBodiedProperties"/>.
    /// A genuine initializer (<c>public static readonly Func&lt;int,int&gt; Twice = x =&gt; x * 2;</c>)
    /// keeps its first <c>=</c> followed by a space, so it stays a Field.
    /// </summary>
    internal static bool IsExpressionBody(string line)
    {
        var eq = line.IndexOf('=');
        return eq >= 0 && eq + 1 < line.Length && line[eq + 1] == '>';
    }

    /// <summary>
    /// Every expression-bodied public property of the file: single-line arrows
    /// (<c>public bool IsMobile => Resolved == TmLayoutMode.Mobile;</c>) and declarations whose
    /// body wraps (the arrow itself ends the declaration line, so <c>public string CssModifier => Resolved switch</c>
    /// is captured without reading the body). Methods and operators are excluded — their name is
    /// followed by <c>(</c>, which the type group forbids.
    /// </summary>
    internal static IEnumerable<MemberDocumentation> ExtractExpressionBodiedProperties(string[] lines, string typeName)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("public ", StringComparison.Ordinal)
                || line.Contains(" operator ", StringComparison.Ordinal))
            {
                continue;
            }

            var match = ExpressionBodiedPropertyRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value.Trim();
            if (name == typeName)
            {
                continue;
            }

            yield return new MemberDocumentation(
                name,
                "Property",
                NormalizeType(match.Groups["type"].Value),
                CleanXmlDoc(ReadXmlDocBlockBefore(lines, i)),
                null);
        }
    }

    /// <summary>
    /// The positional parameters of every matching <c>record</c> declaration, emitted as the
    /// public properties the compiler generates for them. Parameter descriptions come from the
    /// record's type-level <c>&lt;param name="…"&gt;</c> doc tags — the only documentation a
    /// generated property ever has.
    /// <para>
    /// <c>typeName</c> filters to one record; the empty string means every record in the file
    /// (the <see cref="PackageDocumentationGenerator.UnambiguousMemberDescription"/> repair path).
    /// </para>
    /// </summary>
    internal static IEnumerable<MemberDocumentation> ExtractPositionalRecordParameters(string source, string[] lines, string typeName)
    {
        // Indices must line up with `lines`, which SplitLines produced from the same source.
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (Match match in PositionalRecordRegex.Matches(normalized))
        {
            var recordName = match.Groups["name"].Value;
            if (typeName.Length > 0 && !string.Equals(recordName, typeName, StringComparison.Ordinal))
            {
                continue;
            }

            var openParen = match.Index + match.Length - 1;
            var parameterList = ReadBalancedParameterList(normalized, openParen);
            if (string.IsNullOrWhiteSpace(parameterList))
            {
                continue;
            }

            var recordLineIndex = CountNewlines(normalized, match.Index);
            foreach (var piece in SplitTopLevelParameters(parameterList))
            {
                var member = ParsePositionalParameter(piece, lines, recordLineIndex);
                if (member is not null)
                {
                    yield return member;
                }
            }
        }
    }

    /// <summary>The text between a parameter list's parens, honouring nested <c>()</c>, <c>&lt;&gt;</c> and <c>[]</c>.</summary>
    private static string ReadBalancedParameterList(string source, int openParenIndex)
    {
        var paren = 0;
        var angle = 0;
        var bracket = 0;
        for (var i = openParenIndex; i < source.Length; i++)
        {
            switch (source[i])
            {
                case '(':
                    paren++;
                    break;
                case ')':
                    paren--;
                    if (paren == 0)
                    {
                        return source[(openParenIndex + 1)..i];
                    }

                    break;
                case '<':
                    angle++;
                    break;
                case '>' when angle > 0:
                    angle--;
                    break;
                case '[':
                    bracket++;
                    break;
                case ']' when bracket > 0:
                    bracket--;
                    break;
            }
        }

        return source[(openParenIndex + 1)..];
    }

    /// <summary>Splits a parameter list on top-level commas only — <c>Dictionary&lt;string, int&gt;</c> survives.</summary>
    private static IEnumerable<string> SplitTopLevelParameters(string parameterList)
    {
        var pieces = new List<string>();
        var current = new StringBuilder();
        var paren = 0;
        var angle = 0;
        var bracket = 0;
        foreach (var c in parameterList)
        {
            if (c is ',' && paren == 0 && angle == 0 && bracket == 0)
            {
                pieces.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
            switch (c)
            {
                case '(':
                    paren++;
                    break;
                case ')' when paren > 0:
                    paren--;
                    break;
                case '<':
                    angle++;
                    break;
                case '>' when angle > 0:
                    angle--;
                    break;
                case '[':
                    bracket++;
                    break;
                case ']' when bracket > 0:
                    bracket--;
                    break;
            }
        }

        if (current.Length > 0)
        {
            pieces.Add(current.ToString());
        }

        return pieces;
    }

    /// <summary>Ref-kind and parameter-passing modifiers that never belong to a parameter's documented type.</summary>
    private static readonly string[] ParameterModifiers = ["in", "ref", "out", "params", "scoped"];

    /// <summary>
    /// One positional parameter → one <c>Property</c> member: leading attribute lists are stripped
    /// (<c>[property: JsonPropertyName("c")] string? Cursor = null</c> → <c>string? Cursor</c>),
    /// leading <c>in</c>/<c>ref</c>/<c>out</c>/<c>params</c>/<c>scoped</c> modifiers are stripped
    /// from the type, the <c>= default</c> tail is cut, and the last token is the name with the
    /// rest as its type.
    /// </summary>
    private static MemberDocumentation? ParsePositionalParameter(string piece, string[] lines, int recordLineIndex)
    {
        var text = piece.Trim();
        while (text.StartsWith('['))
        {
            var close = FindMatchingBracket(text);
            if (close < 1)
            {
                return null;
            }

            text = text[(close + 1)..].Trim();
        }

        var equals = text.IndexOf('=');
        if (equals >= 0)
        {
            text = text[..equals].Trim();
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            return null;
        }

        var name = tokens[^1];
        if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.None, GeneratorRegex.MatchTimeout))
        {
            return null;
        }

        // `record P(in int X, scoped Span<byte> B)` documents the properties the compiler
        // generates — those carry none of the parameter-passing modifiers.
        var typeTokens = tokens[..^1].SkipWhile(t => ParameterModifiers.Contains(t, StringComparer.Ordinal)).ToArray();

        return new MemberDocumentation(
            name,
            "Property",
            NormalizeType(string.Join(' ', typeTokens)),
            ReadPositionalParamDoc(lines, recordLineIndex, name),
            null);
    }

    /// <summary>Index of the <c>]</c> closing a leading attribute list, or -1 when unbalanced.</summary>
    private static int FindMatchingBracket(string text)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '[')
            {
                depth++;
            }
            else if (text[i] == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// The record's <c>&lt;param name="{name}"&gt;</c> doc text, run through the same cleanup as a
    /// summary (<c>&lt;see cref="…"/&gt;</c> becomes the bare type name, tags stripped).
    /// </summary>
    private static string? ReadPositionalParamDoc(string[] lines, int recordLineIndex, string parameterName)
    {
        var block = ReadXmlDocBlockBefore(lines, recordLineIndex);
        if (string.IsNullOrWhiteSpace(block))
        {
            return null;
        }

        var param = Regex.Match(
            block,
            @"(?s)<param\s+name=""" + Regex.Escape(parameterName) + @""">(.*?)</param>",
            RegexOptions.None,
            GeneratorRegex.MatchTimeout);
        return param.Success ? CleanXmlDoc("<summary>" + param.Groups[1].Value + "</summary>") : null;
    }

    private static int CountNewlines(string text, int endIndex)
    {
        var count = 0;
        var limit = Math.Min(endIndex, text.Length);
        for (var i = 0; i < limit; i++)
        {
            if (text[i] == '\n')
            {
                count++;
            }
        }

        return count;
    }
}
