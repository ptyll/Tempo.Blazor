using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// Guard for the ambient-culture hygiene rule: a test that assigns
/// <c>CultureInfo.CurrentCulture</c> / <c>CultureInfo.CurrentUICulture</c> pins a shared thread
/// state and leaks the language into every later test on that thread (the JsonStringLocalizer
/// suite did exactly that and intermittently localized unrelated suites). The only sanctioned
/// shapes are <see cref="CultureScope"/> (restores on Dispose) or an explicit try/finally that
/// restores inside the finally. The helper itself is allowlisted.
/// </summary>
public sealed class CultureAssignmentGuardTests
{
    private static readonly Regex Assignment =
        new(@"(?<![=!<>])CultureInfo\.(CurrentUICulture|CurrentCulture)\s*=(?!=)",
            RegexOptions.Compiled);

    /// <summary>The sanctioned restore-on-dispose helpers, one per test assembly.</summary>
    private static readonly string[] HelperAllowlist =
    [
        "CultureScope.cs",
        // Tempo.ReportServer.Web.Tests' CultureScope equivalent (private UiCultureScope, restores
        // both cultures on Dispose) — lives in that assembly's shared fixture base.
        "ReportServerWebTestBase.cs",
    ];

    [Fact]
    public void NoTestAssignsAmbientCultureOutsideCultureScopeOrTryFinally()
    {
        var root = FindRepoRoot();
        var testsDir = Path.Combine(root.FullName, "tests");

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(testsDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || HelperAllowlist.Any(helper => file.EndsWith(helper, StringComparison.Ordinal)))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Assignment.IsMatch(lines[i]))
                {
                    continue;
                }

                if (!IsInsideTryFinally(lines, i))
                {
                    offenders.Add($"{Path.GetRelativePath(root.FullName, file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "assign the ambient culture through CultureScope.Use(...) (restores on Dispose) or " +
            "restore it in a finally. Bare assignments leak the language into later tests on the thread.");
    }

    /// <summary>
    /// Heuristic: the assignment is allowed when it sits inside a <c>try</c> block that has a
    /// matching <c>finally</c> (the finally is where the restore belongs — sibling guards cover
    /// restore-specifics). Found by walking the brace stack upwards from the assignment line and
    /// checking whether any enclosing block opens directly after the <c>try</c> keyword.
    /// </summary>
    private static bool IsInsideTryFinally(string[] lines, int index)
    {
        // Brace balance from the top of the file to the assignment line; then walk up the stack.
        var stack = new List<int>();
        for (var i = 0; i <= index; i++)
        {
            var line = lines[i];
            for (var c = 0; c < line.Length; c++)
            {
                if (line[c] == '{') stack.Add(i);
                else if (line[c] == '}' && stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            }
        }

        var lineText = lines[index];
        var braceOnAssignmentLine = lineText.IndexOf('{') >= 0 && lineText.IndexOf('}') > lineText.IndexOf('{');
        foreach (var openLine in stack)
        {
            if (openLine == index && braceOnAssignmentLine)
            {
                continue; // block that OPENS on the assignment line cannot be its try
            }

            var header = HeaderBefore(lines, openLine);
            // Inside the try with a matching finally, or inside the finally itself (the restore
            // half) — both halves of the sanctioned shape.
            if (header == "try" && HasMatchingFinally(lines, openLine))
            {
                return true;
            }

            if (header == "finally")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Token that directly precedes the <c>{</c> opening on <paramref name="openLine"/>
    /// (or the closing <c>)</c> of a try-header on an earlier line), lower-cased.</summary>
    private static string HeaderBefore(string[] lines, int openLine)
    {
        var text = lines[openLine];
        var brace = text.IndexOf('{');
        if (brace < 0)
        {
            return string.Empty;
        }

        var before = text[..brace].TrimEnd();
        if (before.Length > 0)
        {
            var token = before.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
            return token == ")" ? TryKeywordOnEarlierLine(lines, openLine) : token;
        }

        return TryKeywordOnEarlierLine(lines, openLine);
    }

    private static string TryKeywordOnEarlierLine(string[] lines, int openLine)
    {
        for (var i = openLine - 1; i >= 0 && i >= openLine - 3; i--)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var token = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
            return token;
        }

        return string.Empty;
    }

    private static bool HasMatchingFinally(string[] lines, int tryOpenLine)
    {
        // Find the closing brace of the try block, then require a `finally` block right after.
        var tryLine = lines[tryOpenLine];
        var firstBrace = tryLine.IndexOf('{');
        if (firstBrace < 0)
        {
            return false;
        }

        var depth = 1;
        // Count the remainder of the opening line AFTER the first brace (it is already depth 1).
        for (var c = firstBrace + 1; c < tryLine.Length; c++)
        {
            if (tryLine[c] == '{') depth++;
            else if (tryLine[c] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return FollowedByFinally(lines, tryOpenLine);
                }
            }
        }

        for (var i = tryOpenLine + 1; i < lines.Length; i++)
        {
            foreach (var ch in lines[i])
            {
                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return FollowedByFinally(lines, i);
                    }
                }
            }
        }

        return false;
    }

    /// <summary>After the block closes at <paramref name="closeLine"/>, the next meaningful token
    /// must be <c>finally</c> (possibly after <c>catch</c> blocks, which are walked over).</summary>
    private static bool FollowedByFinally(string[] lines, int closeLine)
    {
        for (var j = closeLine + 1; j < Math.Min(closeLine + 4, lines.Length); j++)
        {
            var trimmed = lines[j].Trim();
            if (trimmed.Length == 0 || trimmed == "}")
            {
                continue;
            }

            if (trimmed.StartsWith("finally", StringComparison.Ordinal))
            {
                return true;
            }

            if (trimmed.StartsWith("catch", StringComparison.Ordinal))
            {
                // Skip the whole catch block, then continue scanning after it.
                var catchLine = lines[j];
                var catchBrace = catchLine.IndexOf('{');
                if (catchBrace < 0)
                {
                    return false; // filter-style catch without a block — not the guarded shape
                }

                var catchDepth = 1;
                for (var c = catchBrace + 1; c < catchLine.Length; c++)
                {
                    if (catchLine[c] == '{') catchDepth++;
                    else if (catchLine[c] == '}')
                    {
                        catchDepth--;
                        if (catchDepth == 0)
                        {
                            return FollowedByFinally(lines, j);
                        }
                    }
                }

                for (var k = j + 1; k < lines.Length; k++)
                {
                    foreach (var ch in lines[k])
                    {
                        if (ch == '{') catchDepth++;
                        else if (ch == '}')
                        {
                            catchDepth--;
                            if (catchDepth == 0)
                            {
                                return FollowedByFinally(lines, k);
                            }
                        }
                    }
                }

                return false;
            }

            return false;
        }

        return false;
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
