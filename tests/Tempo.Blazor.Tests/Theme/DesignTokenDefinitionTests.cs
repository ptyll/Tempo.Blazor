using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Guards the design-token contract: every <c>var(--tm-*)</c> a component reaches for must actually
/// be declared in the token files. A phantom token is invisible in code review and in bUnit tests —
/// CSS silently drops the declaration, so a border falls back to <c>currentColor</c> and a background
/// to <c>transparent</c>. That is how <c>--tm-color-error-500</c> and the whole
/// <c>--tm-color-neutral-*</c> family shipped broken.
/// </summary>
public class DesignTokenDefinitionTests
{
    private static readonly Regex TokenUsage = new(@"var\(\s*(--tm-[\w-]+)\s*(?<fallback>,)?", RegexOptions.Compiled);
    private static readonly Regex TokenDeclaration = new(@"^\s*(--tm-[\w-]+)\s*:", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Host knobs — component-local custom properties the library DELIBERATELY never declares
    /// (not in the token files, not in the component stylesheets, not on the component root): a
    /// host sets them on a wrapper or <c>:root</c> and every <c>var()</c> that reads one falls
    /// back to the default baked into the stylesheet. They are therefore NOT a phantom-token
    /// offence — but only while every usage carries its fallback: an allowlisted knob read
    /// without one resolves to the guaranteed-invalid initial value the moment a host does not
    /// set it. The value is the one-line reason the knob is host-owned.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> HostKnobs = new Dictionary<string, string>
    {
        // TmChat (wwwroot/css/components/_chat.css): the chat's layout knobs, incl. the two
        // runtime knobs chat.js writes into the root's inline style.
        ["--tm-chat-min-height"] = "conversation floor a host pins to its own layout (fallback 300px, 0 in fill mode)",
        ["--tm-chat-composer-offset"] = "height of the host's own docked composer the thread panel must clear (fallback 0px)",
        ["--tm-chat-safe-area-bottom"] = "host opt-out when its chrome already pads the safe area (fallback env(safe-area-inset-bottom, 0px))",
        ["--tm-chat-keyboard-inset"] = "runtime keyboard gap written by chat.js (fallback 0px)",
        ["--tm-chat-composer-clearance"] = "gap chat.js measures under a floating composer (fallback 0px)",

        // TmChatBubble + the built-in .tm-chat__bubble (wwwroot/css/components/_chat.css): the
        // per-variant bubble chrome a host re-skins without touching the component.
        ["--tm-chat-bubble-in-bg"] = "incoming bubble background (fallback --tm-bg-muted)",
        ["--tm-chat-bubble-in-fg"] = "incoming bubble text colour (fallback --tm-color-text)",
        ["--tm-chat-bubble-in-border"] = "incoming bubble border colour (fallback transparent)",
        ["--tm-chat-bubble-out-bg"] = "outgoing bubble background (fallback --tm-color-primary)",
        ["--tm-chat-bubble-out-fg"] = "outgoing bubble text colour (fallback --tm-color-primary-contrast)",
        ["--tm-chat-bubble-out-border"] = "outgoing bubble border colour (fallback transparent)",
        ["--tm-chat-bubble-system-bg"] = "system message background (fallback --tm-bg-muted)",
        ["--tm-chat-bubble-system-fg"] = "system message text colour (fallback --tm-color-text-secondary)",
        ["--tm-chat-bubble-system-border"] = "system message border colour (fallback transparent)",
        ["--tm-chat-bubble-pending-bg"] = "pending-variant bubble background (fallback --tm-bg-surface-secondary)",
        ["--tm-chat-bubble-pending-fg"] = "pending-variant bubble text colour (fallback --tm-color-text-secondary)",
        ["--tm-chat-bubble-pending-border"] = "pending-variant bubble border colour (fallback --tm-border-color-strong)",
        ["--tm-chat-bubble-queued-bg"] = "queued-variant bubble background (fallback --tm-color-info-subtle)",
        ["--tm-chat-bubble-queued-fg"] = "queued-variant bubble text colour (fallback --tm-color-info-text)",
        ["--tm-chat-bubble-queued-border"] = "queued-variant bubble border colour (fallback --tm-color-info-soft-border)",
        ["--tm-chat-bubble-discarded-bg"] = "discarded-variant bubble background (fallback --tm-bg-muted)",
        ["--tm-chat-bubble-discarded-fg"] = "discarded-variant bubble text colour (fallback --tm-color-text-secondary)",
        ["--tm-chat-bubble-discarded-border"] = "discarded-variant bubble border colour (fallback --tm-border-color)",
        ["--tm-chat-bubble-locked-bg"] = "locked-variant bubble background (fallback --tm-color-primary-subtle)",
        ["--tm-chat-bubble-locked-fg"] = "locked-variant bubble text colour (fallback --tm-color-primary-text)",
        ["--tm-chat-bubble-locked-border"] = "locked-variant bubble border colour (fallback --tm-color-primary-soft-border)",
        ["--tm-chat-bubble-radius"] = "bubble corner radius (fallback --tm-radius-xl)",
        ["--tm-chat-bubble-padding"] = "bubble padding (fallback the space-2-5/3-5 pair minus the border width)",
        ["--tm-chat-bubble-max-width"] = "bubble maximum width (fallback 80%)",
    };

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

    /// <summary>Global design tokens — the themeable contract declared in the token files.</summary>
    private static HashSet<string> DeclaredTokens()
    {
        var root = RepositoryRoot().FullName;
        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in new[] { "tokens.css", "tokens-dark.css" })
        {
            var path = Path.Combine(root, "src", "Tempo.Blazor", "wwwroot", "css", file);
            foreach (Match match in TokenDeclaration.Matches(File.ReadAllText(path)))
            {
                declared.Add(match.Groups[1].Value);
            }
        }

        return declared;
    }

    /// <summary>
    /// Component-local custom properties (e.g. <c>--tm-timeline-dot-color</c>): a component declares
    /// them itself, or a caller sets them through an inline style. They are a per-component knob, not
    /// a global design token, so they are legitimately absent from the token files.
    /// </summary>
    private static HashSet<string> LocallyDeclaredProperties()
    {
        var root = RepositoryRoot().FullName;
        var local = new HashSet<string>(StringComparer.Ordinal);

        foreach (var stylesheet in ComponentStylesheets())
        {
            foreach (Match match in TokenDeclaration.Matches(File.ReadAllText(stylesheet.FullName)))
            {
                local.Add(match.Groups[1].Value);
            }
        }

        var componentsDir = new DirectoryInfo(Path.Combine(root, "src", "Tempo.Blazor", "Components"));
        var inlineProperty = new Regex(@"(--tm-[\w-]+)\s*:", RegexOptions.Compiled);
        // Code-behind partials count too: TmDashboard's grid variables (--tm-w-*) are emitted
        // from the .razor.cs half of the component (F2 moved the @code there for the 1200-line
        // ceiling), and the declaration is just as real.
        foreach (var razor in componentsDir.EnumerateFiles("*.razor", SearchOption.AllDirectories))
        {
            foreach (Match match in inlineProperty.Matches(File.ReadAllText(razor.FullName)))
            {
                local.Add(match.Groups[1].Value);
            }
        }

        foreach (var codeBehind in componentsDir.EnumerateFiles("*.razor.cs", SearchOption.AllDirectories))
        {
            foreach (Match match in inlineProperty.Matches(File.ReadAllText(codeBehind.FullName)))
            {
                local.Add(match.Groups[1].Value);
            }
        }

        return local;
    }

    private static IEnumerable<FileInfo> ComponentStylesheets() =>
        new DirectoryInfo(Path.Combine(
                RepositoryRoot().FullName, "src", "Tempo.Blazor", "wwwroot", "css", "components"))
            .EnumerateFiles("*.css");

    [Fact]
    public void TokenFiles_DeclareTheDocumentedFamilies()
    {
        var declared = DeclaredTokens();

        declared.Should().Contain("--tm-color-gray-500", "the neutral scale is the gray-* family");
        declared.Should().Contain("--tm-color-danger", "the error colour is danger, not error-500");
        declared.Should().Contain("--tm-border-color");
        declared.Should().Contain("--tm-text-primary");
        declared.Should().NotContain("--tm-color-neutral-500", "there is no neutral-* family — it is gray-*");
    }

    /// <summary>
    /// Every <c>var(--tm-*)</c> in a component stylesheet that resolves to nothing. A host knob
    /// (the <see cref="HostKnobs"/> allowlist) is deliberately undeclared — its usage is clean
    /// only when the <c>var()</c> carries a fallback.
    /// </summary>
    private static List<string> PhantomTokenUsages()
    {
        var declared = DeclaredTokens();
        var local = LocallyDeclaredProperties();
        var offenders = new List<string>();

        foreach (var stylesheet in ComponentStylesheets())
        {
            var text = File.ReadAllText(stylesheet.FullName);
            foreach (Match match in TokenUsage.Matches(text))
            {
                var token = match.Groups[1].Value;
                if (declared.Contains(token) || local.Contains(token))
                {
                    continue;
                }

                if (HostKnobs.ContainsKey(token))
                {
                    if (match.Groups["fallback"].Success)
                    {
                        continue;
                    }

                    offenders.Add($"{stylesheet.Name}: {token} (host knob without a fallback)");
                    continue;
                }

                offenders.Add($"{stylesheet.Name}: {token}");
            }
        }

        return offenders.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Every <c>var()</c> of an allowlisted host knob used WITHOUT a fallback, across the
    /// component stylesheets and the components' markup. A knob the library never declares must
    /// carry its default in every read — a fallback-less read renders the guaranteed-invalid
    /// initial value (no background, no radius) for every host that does not set the knob.
    /// </summary>
    private static List<string> HostKnobUsagesWithoutFallback()
    {
        var offenders = new List<string>();

        void Scan(string fileLabel, string text)
        {
            foreach (Match match in TokenUsage.Matches(text))
            {
                var token = match.Groups[1].Value;
                if (HostKnobs.ContainsKey(token) && !match.Groups["fallback"].Success)
                {
                    offenders.Add($"{fileLabel}: {token}");
                }
            }
        }

        foreach (var stylesheet in ComponentStylesheets())
        {
            Scan(stylesheet.Name, File.ReadAllText(stylesheet.FullName));
        }

        var componentsDir = new DirectoryInfo(Path.Combine(
            RepositoryRoot().FullName, "src", "Tempo.Blazor", "Components"));
        foreach (var razor in componentsDir.EnumerateFiles("*.razor", SearchOption.AllDirectories))
        {
            Scan(razor.Name, File.ReadAllText(razor.FullName));
        }

        return offenders.Distinct(StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void ComponentStylesheets_DoNotUseThePhantomColourScales()
    {
        // --tm-color-neutral-* and --tm-color-error-* were never declared anywhere. Components that
        // reached for them got currentColor / transparent instead, and no theme could reach them.
        var offenders = PhantomTokenUsages()
            .Where(o => o.Contains("--tm-color-neutral-", StringComparison.Ordinal)
                     || o.Contains("--tm-color-error-", StringComparison.Ordinal))
            .ToList();

        offenders.Should().BeEmpty(
            "the neutral/error scales do not exist — use the gray scale and the semantic tokens (--tm-border-color, "
            + "--tm-text-*, --tm-bg-*, --tm-color-danger)");
    }

    [Fact]
    public void ComponentStylesheets_HaveNoUndeclaredTokenDebt()
    {
        // The alias debt (--tm-spacing-* vs --tm-space-*, --tm-bg-hover, --tm-color-danger-600,
        // the rich-editor colour ramps, …) has been retired: every component token now resolves.
        var offenders = PhantomTokenUsages();

        offenders.Should().BeEmpty(
            "every component must style itself with tokens declared in tokens.css/tokens-dark.css "
            + "(or a component-local custom property, or an allowlisted host knob read with its fallback)");
    }

    [Fact]
    public void HostKnobs_AreNeverUsedWithoutAFallback()
    {
        // A host knob is never declared by the library — the fallback IS the default. Every
        // var() read must therefore carry it, or the knob is unusable for any host that does not
        // set it (and the knob-contract comments in the .razor files must stay free of trailing
        // colons that would fake a declaration).
        var offenders = HostKnobUsagesWithoutFallback();

        offenders.Should().BeEmpty(
            "every host knob in the allowlist must be read as var(--tm-…, <default>) — a knob "
            + "the library never declares resolves to nothing without its fallback");
    }
}
