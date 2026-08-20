using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Locks down WHERE the CSS bundler takes the content of <c>tempo-blazor.bundled.css</c> from.
/// <para>
/// The bundled stylesheet is a TRACKED file that the <c>BundleCssFiles</c> target of
/// <c>Tempo.Blazor.csproj</c> rewrites on every build and every pack. A pre-pack "is the working
/// tree clean?" check runs BEFORE that rewrite, so it cannot see dirt the pack itself produces.
/// Today that costs nothing, because the bundler is deterministic and reproduces the committed
/// bytes exactly — measured 2026-08-20 on <c>096849fc</c>: three forced runs of
/// <c>dotnet build src/Tempo.Blazor/Tempo.Blazor.csproj -f net10.0 -t:BundleCssFiles</c> (output
/// deleted before each) all produced sha256 <c>ffeb95210725ba594d93eee54897736bc5825a31c90c1763d60bea53b5687603</c>,
/// which is the committed file's hash, and <c>git status --porcelain</c> stayed empty.
/// </para>
/// <para>
/// That harmlessness is a CONDITION, not a property: it holds only while the bundler composes the
/// output from the <c>@import</c> graph of a single entry stylesheet. A bundler that instead
/// ENUMERATED the css directory would order its inputs by whatever the file system and the current
/// locale hand back, and the committed bundle would start drifting on machines that disagree — dirt
/// produced after the check that licensed the pack. Nobody was watching that condition, so these
/// tests are what watches it.
/// </para>
/// <para>
/// The project does contain a directory glob over the css folder
/// (<c>CssBundleInputs</c>, <c>wwwroot\css\**\*.css</c>), and that is deliberate: it feeds the
/// target's <c>Inputs</c> attribute, i.e. MSBuild's up-to-date check, so that editing any partial
/// re-runs the bundler. It does NOT feed the task. Measured 2026-08-20 on <c>096849fc</c>, the two
/// populations happen to coincide as SETS (144 <c>@import</c>s; 145 globbed <c>.css</c> files minus
/// the bundle itself is the same 144 plus the entry stylesheet), so membership would not tell the
/// two designs apart — ORDER would. That is why
/// <see cref="ImportOrderIsNotDirectoryOrder"/> asserts on the order and not on the set.
/// </para>
/// <para>
/// Construction, because the failure mode that matters here is a false GREEN: the body scanner
/// OVER-APPROXIMATES. Every identifier in the task body is a candidate; the ways out are
/// (a) C# keywords, (b) names DERIVED from the body itself — locals and the task's own declared
/// parameters — and (c) a named allow-list that carries a reason per entry.
/// </para>
/// <para>
/// Exit (b) needed a bound, and finding out why cost a measured FALSE GREEN. It is not a fixed set:
/// it is "whatever the body writes about itself", so a body that declares <c>var GetFiles = 0</c>,
/// <c>catch (Exception GetFiles)</c>, a lambda parameter <c>GetFiles =></c>, or a
/// <c>ParameterGroup</c> entry named <c>GetFiles</c> hands itself the permission, and a real
/// <c>Directory.GetFiles(...)</c> call in the same body then passed. The bound is a ROLE, not a
/// deny-list: a derived name excuses an occurrence only where the name is the RECEIVER of a
/// declaration or a value, never where it stands in MEMBER position after a dot. Member positions
/// have exactly one exit, the reasoned allow-list, so no wording the body chooses for its own locals
/// can open one.
/// </para>
/// <para>
/// What this does NOT claim is exhaustion over every possible spelling; the honest statement is the
/// construction above plus its measured direction of error. Shapes the scanner mis-parses cost a
/// FALSE RED — a local declared with an explicit type instead of <c>var</c>, a raw or interpolated
/// string literal — which is the direction this guard is allowed to be wrong in.
/// </para>
/// <para>
/// That disclaimer covers the PRECISION of the needle, and it must not be stretched to cover its
/// REACH — the two fail differently and only one of them is survivable. A spelling the scanner
/// mis-reads is still a spelling inside the text being scanned: the needle arrives and may misjudge,
/// which is why the error lands on the red side. Moving the body OUT of the scanned text is not a
/// misjudgement, it is the needle never arriving, and then silence means nothing at all. MSBuild
/// offers exactly that move: <c>&lt;Code Source="…"/&gt;</c> compiles the named file and IGNORES the
/// inline CDATA, so an untouched, innocent fragment could sit in this project while different code
/// builds the bundle. It is the same class as a second writer of the bundle, and it is closed the
/// same way — by refusing the redirection, not by describing it.
/// </para>
/// </summary>
public class CssBundlerInputSourceTests
{
    /// <summary>
    /// Every name the <c>BundleCss</c> task body is allowed to mention that is neither a C# keyword,
    /// nor one of its own locals, nor one of its declared parameters. Adding an entry here is how a
    /// change to the task is approved — the reason column is the point of the list, not decoration.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedTaskBodyNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["File"] = "reads the entry stylesheet and each @import target; writes the bundle",
            ["ReadAllText"] = "the ONLY way source text enters the bundle: the entry file, then one call per @import match",
            ["WriteAllText"] = "writes the finished bundle",
            ["Exists"] = "an @import that resolves to no file is WARNED about; nothing falls back to scanning the folder",
            ["Path"] = "resolves an @import target against the entry file's own directory",
            ["GetDirectoryName"] = "the import base directory (from the entry file) and the output directory",
            ["GetFullPath"] = "normalises the resolved @import target",
            ["Combine"] = "joins the @import target onto the import base directory",
            ["Directory"] = "reaches the file system only through CreateDirectory - see that entry",
            ["CreateDirectory"] = "creates the OUTPUT folder; it does not read one. Directory.GetFiles or EnumerateFiles would be a name nobody listed here and would fail this test",
            ["Regex"] = "the @import matcher plus the three minifier passes",
            ["Replace"] = "substitutes each @import with the imported text; also the minifier passes and the newline restore after '}'",
            ["Trim"] = "trims the minified result",
            ["Groups"] = "the captured @import target",
            ["Value"] = "the captured @import target, or the untouched match when the target is missing",
            ["Length"] = "the size reported in the informational log line",
            ["Log"] = "MSBuild task logging",
            ["LogWarning"] = "reports an @import that resolved to no file",
            ["LogMessage"] = "reports the finished bundle",
            ["LogError"] = "reports a failure",
            ["Exception"] = "the catch-all wrapped around the whole body",
            ["Message"] = "the exception text in the error log line",
            ["System"] = "namespace qualifier of System.Text.Encoding.UTF8",
            ["Text"] = "namespace qualifier of System.Text.Encoding.UTF8",
            ["Encoding"] = "UTF-8 in, UTF-8 out",
            ["UTF8"] = "UTF-8 in, UTF-8 out",
            ["Microsoft"] = "namespace qualifier of Microsoft.Build.Framework.MessageImportance",
            ["Build"] = "namespace qualifier of Microsoft.Build.Framework.MessageImportance",
            ["Framework"] = "namespace qualifier of Microsoft.Build.Framework.MessageImportance",
            ["MessageImportance"] = "importance of the informational log line",
            ["High"] = "importance of the informational log line",
        };

    /// <summary>
    /// Every task the project is allowed to run inside a target, keyed <c>TargetName/TaskName</c>.
    /// <para>
    /// The three assertions above have a denominator of ONE inline task, <c>BundleCss</c> — but the
    /// thing being protected is a FILE. A second inline task, a <c>WriteLinesToFile</c>, a
    /// <c>Copy</c> or an <c>Exec</c> that enumerated the css folder and rewrote
    /// <c>tempo-blazor.bundled.css</c> would leave <c>BundleCss</c> untouched and every one of those
    /// assertions green. Keying on the pair rather than on the task name keeps a known-harmless task
    /// from being harmless in a target nobody reasoned about.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedTargetTasks =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BundleCssFiles/BundleCss"] = "the single writer of the bundle; it resolves the @import graph of one entry stylesheet",
            ["CleanBundledCss/Delete"] = "removes the bundle on Clean. A delete cannot put content in, so it cannot make the bundle depend on directory order",
        };

    /// <summary>
    /// Every place the bundle's own path may be named, keyed <c>Element/@Attribute</c>. A mention is
    /// not automatically a write, but a write is always a mention, so this is the cheap
    /// over-approximation of "who touches this file".
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedBundleMentions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CssBundleInputs/@Exclude"] = "keeps the bundle out of the glob that drives the up-to-date check, so the bundler is not listed as its own input",
            ["Target/@Outputs"] = "declares the bundle as the target's output for MSBuild's timestamp comparison; a declaration, not a write",
            ["BundleCss/@OutputFile"] = "the write itself - reasoned about in AllowedTargetTasks",
            ["Delete/@Files"] = "the Clean-time delete",
        };

    /// <summary>
    /// C# keywords and contextual keywords. They are language constructs, not names of anything the
    /// task could read a directory with, so they are subtracted from the candidate set.
    /// </summary>
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "and", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "dynamic",
        "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "init", "int", "interface", "internal", "is",
        "lock", "long", "namespace", "nameof", "new", "not", "null", "object", "operator", "or",
        "out", "override", "params", "private", "protected", "public", "readonly", "record", "ref",
        "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct",
        "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "value", "var", "virtual", "void", "volatile", "when", "where", "while",
        "with", "yield",
    };

    private const string EntryStylesheetName = "tempo-blazor.css";
    private const string BundleName = "tempo-blazor.bundled.css";

    /// <summary>Below the number of imports the entry stylesheet has today, so the order assertion
    /// cannot pass by measuring an empty or near-empty import list.</summary>
    private const int MinimumImports = 100;

    private static readonly Regex ImportRule =
        new("@import\\s+[\"']([^\"']+)[\"']\\s*;", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private static readonly Regex ItemReference =
        new(@"@\(\s*([A-Za-z_][A-Za-z0-9_-]*)", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private static readonly Regex Identifier =
        new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private static DirectoryInfo RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }

            current = current.Parent!;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }

    private static string ProjectPath() =>
        Path.Combine(RepositoryRoot().FullName, "src", "Tempo.Blazor", "Tempo.Blazor.csproj");

    private static string CssDirectory() =>
        Path.Combine(RepositoryRoot().FullName, "src", "Tempo.Blazor", "wwwroot", "css");

    private static XElement ProjectXml()
    {
        var path = ProjectPath();
        File.Exists(path).Should().BeTrue($"{path} owns the CSS bundler");
        return XElement.Load(path);
    }

    private static XElement BundleCssUsingTask()
    {
        var usingTasks = ProjectXml()
            .Descendants("UsingTask")
            .Where(element => (string?)element.Attribute("TaskName") == "BundleCss")
            .ToList();

        usingTasks.Should().ContainSingle(
            "the CSS bundler is one inline task; a second definition would mean this test is reading "
            + "only half of what runs");
        return usingTasks[0];
    }

    /// <summary>
    /// Comments and string literals, blanked in place so every remaining offset still points at real
    /// code. A word inside a comment (<c>calc(100%+8px)</c> appears in one) or inside a regex literal
    /// is not a name the task uses, and counting it would produce a false red on the very first run.
    /// </summary>
    private static string BlankCommentsAndStrings(string code)
    {
        var scrubbed = new StringBuilder(code);
        var i = 0;

        void Blank(int index)
        {
            if (code[index] != '\n')
            {
                scrubbed[index] = ' ';
            }
        }

        while (i < code.Length)
        {
            var c = code[i];

            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                while (i < code.Length && code[i] != '\n')
                {
                    Blank(i);
                    i++;
                }
            }
            else if (c == '/' && i + 1 < code.Length && code[i + 1] == '*')
            {
                Blank(i);
                Blank(i + 1);
                i += 2;
                while (i + 1 < code.Length && !(code[i] == '*' && code[i + 1] == '/'))
                {
                    Blank(i);
                    i++;
                }

                if (i + 1 < code.Length)
                {
                    Blank(i);
                    Blank(i + 1);
                    i += 2;
                }
            }
            else if (c == '@' && i + 1 < code.Length && code[i + 1] == '"')
            {
                Blank(i);
                Blank(i + 1);
                i += 2;
                while (i < code.Length)
                {
                    if (code[i] == '"')
                    {
                        // "" is an escaped quote inside a verbatim string, not its end.
                        if (i + 1 < code.Length && code[i + 1] == '"')
                        {
                            Blank(i);
                            Blank(i + 1);
                            i += 2;
                            continue;
                        }

                        Blank(i);
                        i++;
                        break;
                    }

                    Blank(i);
                    i++;
                }
            }
            else if (c is '"' or '\'')
            {
                var quote = c;
                Blank(i);
                i++;
                while (i < code.Length && code[i] != quote)
                {
                    if (code[i] == '\\' && i + 1 < code.Length)
                    {
                        Blank(i);
                        Blank(i + 1);
                        i += 2;
                        continue;
                    }

                    Blank(i);
                    i++;
                }

                if (i < code.Length)
                {
                    Blank(i);
                    i++;
                }
            }
            else
            {
                i++;
            }
        }

        return scrubbed.ToString();
    }

    /// <summary>
    /// Names the body introduces itself: <c>var</c> declarations, lambda parameters and the
    /// <c>catch</c> variable. Derived from the text rather than listed, so renaming a local is not a
    /// change anybody has to approve. A local declared with an explicit type instead of <c>var</c>
    /// would not be derived and would show up as an unapproved name — a false red, which is the side
    /// this guard is allowed to fail on.
    /// <para>
    /// What comes back from here excuses a name only in RECEIVER position. It is deliberately NOT
    /// consulted for a name in member position, because the body chooses these words itself and would
    /// otherwise be able to license <c>Directory.GetFiles</c> by declaring a local called
    /// <c>GetFiles</c>.
    /// </para>
    /// </summary>
    private static HashSet<string> DeclaredLocals(string scrubbedCode)
    {
        var locals = new HashSet<string>(StringComparer.Ordinal);
        var timeout = TimeSpan.FromSeconds(5);

        foreach (Match match in Regex.Matches(scrubbedCode, @"\bvar\s+([A-Za-z_][A-Za-z0-9_]*)",
                     RegexOptions.None, timeout))
        {
            locals.Add(match.Groups[1].Value);
        }

        foreach (Match match in Regex.Matches(scrubbedCode, @"([A-Za-z_][A-Za-z0-9_]*)\s*=>",
                     RegexOptions.None, timeout))
        {
            locals.Add(match.Groups[1].Value);
        }

        foreach (Match match in Regex.Matches(scrubbedCode,
                     @"catch\s*\(\s*[A-Za-z_][A-Za-z0-9_.]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\)",
                     RegexOptions.None, timeout))
        {
            locals.Add(match.Groups[1].Value);
        }

        return locals;
    }

    /// <summary>
    /// True when the identifier starting at <paramref name="index"/> stands after a dot, i.e. it is a
    /// MEMBER of whatever precedes it (<c>Directory.GetFiles</c>, <c>content.Trim</c>) rather than a
    /// name in its own right. Whitespace between the dot and the name is skipped, because
    /// <c>Directory .GetFiles</c> is the same call.
    /// </summary>
    private static bool IsMemberPosition(string code, int index)
    {
        var i = index - 1;
        while (i >= 0 && char.IsWhiteSpace(code[i]))
        {
            i--;
        }

        return i >= 0 && code[i] == '.';
    }

    [Fact]
    public void BundleCssTaskBodyNamesNothingOutsideTheAllowList()
    {
        var usingTask = BundleCssUsingTask();

        var parameters = usingTask.Element("ParameterGroup")?.Elements().Select(e => e.Name.LocalName)
            .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        parameters.Should().NotBeEmpty("the inline task declares the parameters it is fed");

        var codeElement = usingTask.Element("Task")?.Element("Code");
        codeElement.Should().NotBeNull("the inline task carries its body as a Code element");

        // Everything below reads the INLINE body. A Source attribute makes MSBuild compile that file
        // and ignore the CDATA entirely, so the body scanned here would not be the body that runs -
        // see the class remarks for why no disclaimer can cover this.
        codeElement!.Attribute("Source").Should().BeNull(
            "the bundler's body must stay inline in the project file. With Source set, MSBuild "
            + "compiles the named file and the CDATA below it never runs, so a harmless-looking "
            + "fragment could sit here while the code that actually builds the bundle enumerates the "
            + "css directory somewhere else. That is the same channel this file already closes for "
            + "TARGETS by refusing an Import.");

        var declaredCode = codeElement.Value;
        declaredCode.Should().NotBeNull("the inline task carries its body as a Code fragment");
        var code = declaredCode!;

        // Non-vacuity: an emptied or renamed body would otherwise leave "no unapproved names" true
        // while measuring nothing at all.
        code.Length.Should().BeGreaterThan(500, "the bundler body is not a stub");
        code.Should().Contain("@import",
            "the bundle is composed by resolving @import rules; if this word is gone, the input "
            + "source has changed and everything else this file asserts is about a different task");

        var scrubbed = BlankCommentsAndStrings(code);
        var locals = DeclaredLocals(scrubbed);

        var unapproved = new List<string>();

        foreach (Match match in Identifier.Matches(scrubbed))
        {
            var name = match.Value;

            // The reasoned allow-list excuses a name in either role - that is what being reasoned
            // about buys it.
            if (AllowedTaskBodyNames.ContainsKey(name))
            {
                continue;
            }

            // MEMBER position: the name stands after a dot, so it is an API this body reaches for.
            // Locals and parameters are NOT consulted here - see DeclaredLocals for the false green
            // that rule was written against.
            if (IsMemberPosition(scrubbed, match.Index))
            {
                unapproved.Add(name + " (member of something - only the allow-list can excuse this)");
                continue;
            }

            if (CSharpKeywords.Contains(name) || locals.Contains(name) || parameters.Contains(name))
            {
                continue;
            }

            unapproved.Add(name + " (name the body neither declares nor was given)");
        }

        unapproved = unapproved.Distinct(StringComparer.Ordinal)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        unapproved.Should().BeEmpty(
            "the CSS bundler must keep taking its content from the @import graph of one entry "
            + "stylesheet. Every name in its body is a candidate; a name after a dot can only be "
            + "excused by the reasoned allow-list in this test, and everywhere else a C# keyword or "
            + "a local or parameter the body declares itself will do. Unapproved: "
            + string.Join(", ", unapproved));

        // The allow-list must not rot either: an entry for a name the body no longer uses is a
        // permission nobody re-examined.
        var used = Identifier.Matches(scrubbed).Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);
        AllowedTaskBodyNames.Keys.Where(name => !used.Contains(name)).Should().BeEmpty(
            "an allow-list entry that matches nothing in the task body is stale permission");
    }

    [Fact]
    public void BundleCssTaskInvocationIsFedByOneLiteralStylesheet()
    {
        var invocations = ProjectXml()
            .Descendants("Target")
            .SelectMany(target => target.Elements("BundleCss"))
            .ToList();

        invocations.Should().ContainSingle(
            "the bundler runs from exactly one place; a second call site could be fed differently "
            + "and this test would only be reading the first");

        var invocation = invocations[0];
        invocation.Attributes().Should().HaveCountGreaterThanOrEqualTo(2,
            "the call site passes at least an input and an output");

        foreach (var attribute in invocation.Attributes())
        {
            attribute.Value.Should().NotContain("@(",
                $"the '{attribute.Name.LocalName}' parameter of the bundler must not be fed an "
                + "MSBuild item. An item is a directory enumeration result, and routing one into the "
                + "task is exactly the change that turns the bundle's content into whatever the file "
                + "system enumerated. Items may reach the Inputs/Outputs of the TARGET, which is the "
                + "up-to-date check, and nothing else.");

            attribute.Value.Should().NotContain("*",
                $"the '{attribute.Name.LocalName}' parameter must be a literal path, not a wildcard");
        }

        var inputFile = (string?)invocation.Attribute("InputFile");
        inputFile.Should().NotBeNull("the bundler is fed one entry stylesheet");
        inputFile!.Replace('\\', '/').Should().EndWith("/" + EntryStylesheetName,
            "the entry stylesheet is the single root of the @import graph");
    }

    [Fact]
    public void CssItemGlobsAreReferencedOnlyByTargetUpToDateChecks()
    {
        var project = ProjectXml();

        var globbedItems = project.Descendants("ItemGroup")
            .Elements()
            .Where(item => ((string?)item.Attribute("Include"))?.Contains('*') == true)
            .ToList();

        globbedItems.Should().NotBeEmpty(
            "this test is about how directory globs are consumed; with no glob in the project it "
            + "would be measuring nothing");

        globbedItems.Select(item => item.Name.LocalName)
            .Should().Contain(
                name => name == "CssBundleInputs",
                "the css glob feeding the bundler target's up-to-date check is the item this test "
                + "exists for; if it was renamed, re-read where the new one is consumed");

        var misplaced = new List<string>();
        var references = 0;

        foreach (var element in project.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                foreach (Match match in ItemReference.Matches(attribute.Value))
                {
                    references++;
                    var isUpToDateCheck =
                        element.Name.LocalName == "Target"
                        && attribute.Name.LocalName is "Inputs" or "Outputs";

                    if (!isUpToDateCheck)
                    {
                        misplaced.Add(
                            $"@({match.Groups[1].Value}) in {element.Name.LocalName}/@{attribute.Name.LocalName}");
                    }
                }
            }

            foreach (var text in element.Nodes().OfType<XText>())
            {
                foreach (Match match in ItemReference.Matches(text.Value))
                {
                    references++;
                    misplaced.Add($"@({match.Groups[1].Value}) in the body of {element.Name.LocalName}");
                }
            }
        }

        references.Should().BeGreaterThan(0,
            "the project does reference items; zero would mean the scan found nothing to judge");

        misplaced.Should().BeEmpty(
            "an MSBuild item in this project is a directory enumeration. It may drive a target's "
            + "Inputs/Outputs - that is only MSBuild deciding whether to re-run - but the moment one "
            + "reaches a task parameter or a property, the enumeration starts deciding the bundle's "
            + "CONTENT and its ORDER. Misplaced: " + string.Join(", ", misplaced));
    }

    [Fact]
    public void ImportOrderIsNotDirectoryOrder()
    {
        var cssDirectory = CssDirectory();
        var entry = Path.Combine(cssDirectory, EntryStylesheetName);
        File.Exists(entry).Should().BeTrue($"{entry} is the root of the @import graph");

        var imports = ImportRule.Matches(File.ReadAllText(entry))
            .Select(match => match.Groups[1].Value)
            .ToList();

        imports.Should().HaveCountGreaterThanOrEqualTo(MinimumImports,
            "an entry stylesheet with no imports would make the order comparison below vacuous");

        var missing = imports
            .Where(import => !File.Exists(Path.GetFullPath(Path.Combine(cssDirectory, import))))
            .ToList();
        missing.Should().BeEmpty(
            "an @import that resolves to nothing is only a build WARNING, so its styles would go "
            + "missing from the bundle in silence. Missing: " + string.Join(", ", missing));

        var enumerationOrder = imports.OrderBy(import => import, StringComparer.Ordinal).ToList();
        imports.Should().NotEqual(enumerationOrder,
            "this is what makes the difference between the two designs observable at all. The css "
            + "folder holds the same files the entry stylesheet imports, so a bundler that "
            + "enumerated the directory would emit the same SET - only in a different ORDER, and CSS "
            + "is order-dependent. If the imports were ever sorted into directory order, that "
            + "distinction would vanish and this file's other assertions would be the only thing "
            + "left holding the design in place.");

        var bundle = Path.Combine(cssDirectory, BundleName);
        File.Exists(bundle).Should().BeTrue(
            $"{bundle} is tracked and rewritten by the pack; it is the artefact this whole file is about");
    }

    /// <summary>
    /// Widens the denominator from ONE task to the whole project file: whatever writes
    /// <c>tempo-blazor.bundled.css</c> has to be a thing somebody reasoned about, not merely a thing
    /// that is not called <c>BundleCss</c>.
    /// <para>
    /// MEASURED LIMIT, so nobody reads more coverage out of this than it has: the denominator is
    /// <c>Tempo.Blazor.csproj</c>. Targets injected from elsewhere — a <c>Directory.Build.targets</c>,
    /// a <c>.targets</c> file shipped inside a PackageReference — are outside it and this test says
    /// nothing about them. It does close the one channel that would hide such a file from a reader of
    /// the csproj, by refusing an explicit <c>Import</c>. Checked 2026-08-20 on <c>096849fc</c>:
    /// the repository has a <c>Directory.Build.props</c> and no <c>Directory.Build.targets</c>, and
    /// the props file declares no <c>Target</c> and no <c>Import</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void NothingElseInTheProjectCanWriteTheBundle()
    {
        var project = ProjectXml();

        project.Descendants("Import").Should().BeEmpty(
            "an Import can carry targets that write the bundle without appearing in this file, which "
            + "would put them outside everything asserted here");

        var targets = project.Descendants("Target").ToList();
        targets.Should().NotBeEmpty("with no target in the project this test would be measuring nothing");

        var unapprovedTasks = targets
            .SelectMany(target => target.Elements()
                .Select(task => $"{(string?)target.Attribute("Name")}/{task.Name.LocalName}"))
            .Where(pair => !AllowedTargetTasks.ContainsKey(pair))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(pair => pair, StringComparer.Ordinal)
            .ToList();

        unapprovedTasks.Should().BeEmpty(
            "the bundle is a TRACKED file that the pack rewrites, and the reason that is harmless is "
            + "that one reasoned task produces it from an @import graph. Any other task running in "
            + "this project is a second candidate writer, and 'it is not called BundleCss' is not a "
            + "reason. Unapproved: " + string.Join(", ", unapprovedTasks));

        var mentions = new List<string>();
        var unapprovedMentions = new List<string>();

        foreach (var element in project.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                if (!attribute.Value.Contains(BundleName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var role = $"{element.Name.LocalName}/@{attribute.Name.LocalName}";
                mentions.Add(role);
                if (!AllowedBundleMentions.ContainsKey(role))
                {
                    unapprovedMentions.Add(role);
                }
            }

            foreach (var text in element.Nodes().OfType<XText>())
            {
                if (text.Value.Contains(BundleName, StringComparison.OrdinalIgnoreCase))
                {
                    unapprovedMentions.Add($"{element.Name.LocalName}/text()");
                }
            }
        }

        mentions.Should().HaveCountGreaterThanOrEqualTo(3,
            "the bundle is named by the glob's Exclude, by the target's Outputs and by the task's "
            + "OutputFile; finding fewer than that means this scan stopped matching the file it is "
            + "about and its silence would mean nothing");

        unapprovedMentions.Distinct(StringComparer.Ordinal)
            .OrderBy(role => role, StringComparer.Ordinal)
            .Should().BeEmpty(
                "every place that names the bundle is a place that might write it. A mention nobody "
                + "reasoned about is exactly how a second writer arrives. Unapproved: "
                + string.Join(", ", unapprovedMentions.Distinct(StringComparer.Ordinal)));
    }
}
