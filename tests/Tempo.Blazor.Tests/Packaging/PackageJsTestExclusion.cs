using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// Repo-wide check that every packable RCL which keeps node tests under <c>wwwroot</c> also
/// declares the paired <c>Content Remove</c> + <c>None Pack="false"</c> globs that keep those
/// files out of static web assets. Matching is by file against compiled globs (the same
/// MSBuild glob language <see cref="ReleaseContractTests.PackageProvenance"/> translates), not
/// by requiring a particular glob string — Signing and EmailTemplates use a wider
/// <c>wwwroot\**\__tests__\**</c> pair and that pair is valid.
/// </summary>
internal static class PackageJsTestExclusion
{
    /// <summary>
    /// One node-test file under a project's <c>wwwroot</c> and whether each half of the
    /// exclusion pair covers it.
    /// </summary>
    /// <param name="ProjectName">The project directory name under <c>src/</c>.</param>
    /// <param name="WwwrootRelativePath">Path relative to <c>wwwroot</c>, forward slashes.</param>
    /// <param name="CoveredByContentRemove">A <c>Content Remove</c> glob compiled and matched.</param>
    /// <param name="CoveredByNonePackFalse">A <c>None Pack="false"</c> glob compiled and matched.</param>
    internal readonly record struct Coverage(
        string ProjectName,
        string WwwrootRelativePath,
        bool CoveredByContentRemove,
        bool CoveredByNonePackFalse)
    {
        /// <summary>Either half of the pair is missing.</summary>
        internal bool IsViolation => !CoveredByContentRemove || !CoveredByNonePackFalse;

        /// <summary>
        /// <c>Tempo.Blazor.Modeling: js/__tests__/modeling-viewer-tokens.test.mjs (missing Content Remove, missing None Pack=false)</c>.
        /// </summary>
        internal string Format()
        {
            var missing = new List<string>(2);
            if (!CoveredByContentRemove)
            {
                missing.Add("missing Content Remove");
            }

            if (!CoveredByNonePackFalse)
            {
                missing.Add("missing None Pack=false");
            }

            return $"{ProjectName}: {WwwrootRelativePath} ({string.Join(", ", missing)})";
        }
    }

    /// <summary>
    /// Every packable <c>src/*/*.csproj</c> whose <c>wwwroot</c> holds a <c>__tests__</c> segment
    /// or a <c>*.test.mjs</c> file, with per-file coverage of the exclusion pair.
    /// </summary>
    internal static IReadOnlyList<Coverage> InspectRepository(string repositoryRoot)
    {
        var result = new List<Coverage>();
        foreach (var projectRoot in EnumeratePackableProjectRoots(repositoryRoot))
        {
            result.AddRange(InspectProject(projectRoot));
        }

        return result;
    }

    /// <summary>
    /// Coverage of every node-test file under one project's <c>wwwroot</c>. A project with no
    /// such files yields an empty list — the caller asserts the repository-wide set is not empty.
    /// </summary>
    internal static IReadOnlyList<Coverage> InspectProject(string projectRoot)
    {
        var projectName = ProjectDirectoryName(projectRoot);
        var wwwroot = Path.Combine(projectRoot, "wwwroot");
        var testFiles = EnumerateJsTestFiles(wwwroot);
        if (testFiles.Count == 0)
        {
            return [];
        }

        var csproj = FindSingleCsproj(projectRoot);
        var document = XDocument.Load(csproj);
        var contentRemove = CompileGlobs(ReadContentRemoveGlobs(document));
        var nonePackFalse = CompileGlobs(ReadNonePackFalseGlobs(document));

        return testFiles.Select(relative => new Coverage(
            projectName,
            relative,
            contentRemove.Any(matcher => matcher.IsMatch(relative)),
            nonePackFalse.Any(matcher => matcher.IsMatch(relative)))).ToList();
    }

    /// <summary>
    /// Keys of <see cref="ReleaseContractTests.PackageProvenance.TreeContent"/> that would land
    /// in <c>staticwebassets/</c> and look like a node test. The denominator only subtracts
    /// <c>Pack="false"</c> — a project that has <c>Content Remove</c> but no <c>None Pack="false"</c>
    /// still reports the file here.
    /// </summary>
    internal static IReadOnlyList<string> CollectShippedJsTests(string repositoryRoot)
    {
        var shipped = new List<string>();
        foreach (var projectRoot in EnumeratePackableProjectRoots(repositoryRoot))
        {
            if (!Directory.Exists(Path.Combine(projectRoot, "wwwroot")))
            {
                continue;
            }

            var projectName = ProjectDirectoryName(projectRoot);
            var denominator = ReleaseContractTests.PackageProvenance.TreeContent(projectName);
            foreach (var key in denominator.Files.Keys)
            {
                if (IsJsTestPath(key))
                {
                    shipped.Add($"{projectName}: {key}");
                }
            }
        }

        return shipped;
    }

    /// <summary>Packable projects one level under <c>src/</c> (the RCL layout this repo uses).</summary>
    internal static IReadOnlyList<string> EnumeratePackableProjectRoots(string repositoryRoot)
    {
        var src = Path.Combine(repositoryRoot, "src");
        var roots = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(src))
        {
            foreach (var csproj in Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly))
            {
                if (IsPackable(XDocument.Load(csproj)))
                {
                    roots.Add(directory);
                }
            }
        }

        return roots;
    }

    /// <summary>
    /// Files under <c>wwwroot</c> whose path contains a <c>__tests__</c> segment or ends with
    /// <c>.test.mjs</c>. Paths are relative to <c>wwwroot</c> with forward slashes.
    /// </summary>
    internal static IReadOnlyList<string> EnumerateJsTestFiles(string wwwroot)
    {
        if (!Directory.Exists(wwwroot))
        {
            return [];
        }

        return Directory.EnumerateFiles(wwwroot, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(wwwroot, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(IsJsTestPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A <c>__tests__</c> path segment or a <c>*.test.mjs</c> file name.</summary>
    internal static bool IsJsTestPath(string wwwrootRelativePath)
    {
        var normalized = wwwrootRelativePath.Replace('\\', '/');
        var segments = normalized.Split('/');
        return segments.Any(segment => string.Equals(segment, "__tests__", StringComparison.OrdinalIgnoreCase))
            || normalized.EndsWith(".test.mjs", StringComparison.OrdinalIgnoreCase);
    }

    /// <remarks>
    /// Reads only the project file; IsPackable inherited from Directory.Build.props is not
    /// evaluated (all src RCLs declare it locally or default to packable).
    /// </remarks>
    private static bool IsPackable(XDocument document)
    {
        var values = document.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "IsPackable", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Value.Trim())
            .ToList();
        if (values.Count == 0)
        {
            return true;
        }

        return values.Any(value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            && values.All(value => !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindSingleCsproj(string projectRoot)
    {
        var files = Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly).ToList();
        return files.Count == 1
            ? files[0]
            : throw new InvalidOperationException(
                $"{projectRoot} holds {files.Count} csproj files — which one packs is ambiguous.");
    }

    private static IEnumerable<string> ReadContentRemoveGlobs(XDocument document) =>
        document.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Content", StringComparison.OrdinalIgnoreCase))
            .Select(element => AttributeNamed(element, "Remove")?.Value)
            .OfType<string>()
            .SelectMany(SplitGlobs);

    private static IEnumerable<string> ReadNonePackFalseGlobs(XDocument document) =>
        document.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "None", StringComparison.OrdinalIgnoreCase))
            .Where(element => string.Equals(
                AttributeNamed(element, "Pack")?.Value, "false", StringComparison.OrdinalIgnoreCase))
            .Select(element => AttributeNamed(element, "Include")?.Value)
            .OfType<string>()
            .SelectMany(SplitGlobs);

    private static IEnumerable<string> SplitGlobs(string spec) =>
        spec.Split(';').Select(piece => piece.Trim()).Where(piece => piece.Length > 0);

    /// <summary>
    /// Compiles project-relative globs into matchers over <c>wwwroot</c>-relative paths. Unreadable
    /// shapes are skipped (they cover nothing) so a substitution that was meant to exclude a test
    /// file still surfaces as a violation.
    /// </summary>
    private static List<Regex> CompileGlobs(IEnumerable<string> globs)
    {
        var matchers = new List<Regex>();
        foreach (var glob in globs)
        {
            var matcher = TryCompileWwwrootGlob(glob);
            if (matcher is not null)
            {
                matchers.Add(matcher);
            }
        }

        return matchers;
    }

    /// <summary>
    /// The same glob language <c>PackageProvenance.CompilePackExclusion</c> translates: separators
    /// normalize to <c>/</c>, <c>**</c> as a whole segment matches zero or more directories,
    /// <c>*</c>/<c>?</c> never cross a separator, matching is case-insensitive. A glob whose first
    /// segment is not <c>wwwroot</c> cannot reach a swept file and is ignored.
    /// </summary>
    internal static Regex? TryCompileWwwrootGlob(string rawPattern)
    {
        var pattern = rawPattern.Replace('\\', '/');
        if (pattern.Contains("$(") || pattern.Contains("${")
            || pattern.Contains("@(") || pattern.Contains("%(")
            || pattern.Contains(':'))
        {
            return null;
        }

        var segments = pattern.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or "..")
            || segments.Any(segment => segment != "**" && segment.Contains("**")))
        {
            return null;
        }

        if (segments.Length == 1
            || !string.Equals(segments[0], "wwwroot", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new Regex(
            "^" + GlobToRegex(segments[1..]) + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Copied from <c>PackageProvenance.GlobToRegex</c> so Content Remove can reuse the same
    /// translation the pack-exclusion denominator already trusts. A mid-pattern <c>**</c> owns the
    /// separator that follows it; a trailing <c>**</c> is "anything below".
    /// </summary>
    private static string GlobToRegex(IReadOnlyList<string> segments)
    {
        var builder = new StringBuilder();
        var needsSeparator = false;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (segment == "**")
            {
                if (needsSeparator)
                {
                    builder.Append('/');
                }

                builder.Append(index == segments.Count - 1 ? ".*" : "(?:[^/]+/)*");
                needsSeparator = false;
                continue;
            }

            if (needsSeparator)
            {
                builder.Append('/');
            }

            foreach (var c in segment)
            {
                builder.Append(c switch
                {
                    '*' => "[^/]*",
                    '?' => "[^/]",
                    _ when ".\\+()[]{}^$|".IndexOf(c) >= 0 => "\\" + c,
                    _ => c.ToString(),
                });
            }

            needsSeparator = true;
        }

        return builder.ToString();
    }

    private static XAttribute? AttributeNamed(XElement element, string name) =>
        element.Attributes().FirstOrDefault(
            attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Directory name of a project root, trimmed of trailing separators so
    /// <see cref="InspectProject"/> and <see cref="CollectShippedJsTests"/> agree.
    /// </summary>
    private static string ProjectDirectoryName(string projectRoot) =>
        Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
}
