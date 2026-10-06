using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// CI gate for the strict token audit (00-cross-cutting §7.3). The workflow only runs
/// <c>dotnet test</c>, so a Node script on its own would never fail a build — this test shells
/// out to <c>scripts/audit-css-strict.mjs</c> and fails when the violation count grows past the
/// committed baseline. The baseline may only shrink.
/// </summary>
public sealed class CssTokenAuditTests
{
    private static readonly TimeSpan AuditTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Hard ceiling. Raising it is a visible code change, which is the point: the baseline file can
    /// be regenerated, but growing past this number has to be argued for in review.
    /// </summary>
    private const int StrictAuditCeiling = 5858;

    [Fact]
    public void StrictAudit_DoesNotExceedTheBaseline()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var baselinePath = Path.Combine(root, "scripts", "css-token-baseline.json");
        File.Exists(baselinePath).Should().BeTrue($"{baselinePath} freezes the current violation count");

        using var baseline = JsonDocument.Parse(File.ReadAllText(baselinePath));
        var allowed = baseline.RootElement.GetProperty("count").GetInt32();

        var (exitCode, output) = Run(root, "scripts/audit-css-strict.mjs");

        var reported = ReportedCount(output);
        reported.Should().BeLessThanOrEqualTo(StrictAuditCeiling,
            "growing past the hard ceiling is a code change, not a baseline edit");
        reported.Should().BeLessThanOrEqualTo(allowed,
            "the strict audit baseline may only shrink — a new hex literal, an undefined --tm-* alias "
            + "(even with a fallback) or a [data-theme=dark] block in a component stylesheet fails here. "
            + output);
        exitCode.Should().Be(0, output);
    }

    [Fact]
    public void StrictAudit_StillSeesTheExistingDebt()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var (exitCode, output) = Run(root, "scripts/audit-css-strict.mjs");

        exitCode.Should().Be(0, output);
        ReportedCount(output).Should().BeGreaterThan(0,
            "the baseline exists because core stylesheets still contain colour literals; "
            + "a zero means the audit stopped seeing them");
    }

    [Fact]
    public void StrictAudit_FixtureSuitePasses()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var (exitCode, output) = Run(root, "--test scripts/audit-css-strict.test.mjs");

        exitCode.Should().Be(0, output);
    }

    private static int ReportedCount(string output)
    {
        var line = output.Split('\n').FirstOrDefault(row => row.Contains("violation(s)", StringComparison.Ordinal));
        line.Should().NotBeNull($"the audit must report its count. Output:\n{output}");
        return int.Parse(line!.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1],
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static (int ExitCode, string Output) Run(string root, string script)
    {
        var start = new ProcessStartInfo("node", script)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(AuditTimeout).Should().BeTrue("the audit must finish");
        return (process.ExitCode, stdout + stderr);
    }
}
