using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// The changelog's contract with the reader is that a hex id in backticks names a commit of THIS
/// repository — that is the whole point of a commit-referenced changelog. An entry that prints
/// <c>`deadbeef`</c> while pointing at nothing is precisely the dishonesty 2.9.0 exists to
/// remove, so the file is not trusted to be accurate by convention: every backticked hex token
/// is extracted and handed to <c>git cat-file -e &lt;id&gt;^{commit}</c>, and a miss fails.
///
/// Parenthesised ids are deliberately NOT commits — the 2.8.26 convention paragraph declares
/// them development-line record ids (D17 register entries and siblings) that only travel in the
/// subject lines of landed commits. The second test therefore enforces the other half of the
/// convention: a hex-looking token that resolves to no commit may appear ONLY inside
/// parentheses, never bare and never in backticks — so a typo'd "landed de4dbeef" cannot pose
/// as a commit claim either.
/// </summary>
public class ChangelogReferencesExistingCommitsTests
{
    private static readonly Regex BacktickedHex = new(@"`([0-9a-fA-F]{7,40})`", RegexOptions.Compiled);
    private static readonly Regex BareHex = new(@"\b[0-9a-fA-F]{7,40}\b", RegexOptions.Compiled);
    private static readonly Regex ContainsDigit = new(@"\d", RegexOptions.Compiled);

    [Fact]
    public void BacktickedCommitIds_AllResolveAsCommits()
    {
        var changelog = ReadChangelog();
        var ids = BacktickedHex.Matches(changelog)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        ids.Should().NotBeEmpty(
            "a commit-referenced changelog that prints no backticked ids has lost the convention "
            + "this test exists to guard");

        var unresolved = ids.Where(id => !ResolvesAsCommit(id)).ToList();
        unresolved.Should().BeEmpty(
            "every hex id printed inside backticks is a commit claim — these resolve to no commit "
            + "of this repository: {0}", string.Join(", ", unresolved));
    }

    [Fact]
    public void NonCommitHexIds_ExistOnlyAsParenthesisedRecordIds()
    {
        var changelog = ReadChangelog();
        var offenders = new List<string>();

        foreach (Match match in BareHex.Matches(changelog))
        {
            var token = match.Value;
            // Pure-alpha words of 7+ letters ("facaded") are not ids; a real short hash carries
            // digits, so require one before treating the token as a hash claim at all.
            if (!ContainsDigit.IsMatch(token))
            {
                continue;
            }

            if (ResolvesAsCommit(token))
            {
                continue; // a commit may stand anywhere
            }

            if (!IsInsideParentheses(changelog, match.Index))
            {
                offenders.Add(token);
            }
        }

        offenders.Distinct().Should().BeEmpty(
            "a hex token that resolves to no commit may appear only inside parentheses — the "
            + "declared record-id convention — never bare in prose: {0}",
            string.Join(", ", offenders.Distinct()));
    }

    private static bool IsInsideParentheses(string text, int index)
    {
        var open = text.LastIndexOf('(', index);
        var closeBefore = text.LastIndexOf(')', index);
        if (open < 0 || closeBefore > open)
        {
            return false;
        }

        var closeAfter = text.IndexOf(')', index);
        return closeAfter > index;
    }

    private static bool ResolvesAsCommit(string id)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = ReleaseScriptInputReadTests.FindRepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("cat-file");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(id + "^{commit}");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("git could not be started");
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    private static string ReadChangelog() =>
        File.ReadAllText(Path.Combine(ReleaseScriptInputReadTests.FindRepoRoot(), "CHANGELOG.md"));
}
