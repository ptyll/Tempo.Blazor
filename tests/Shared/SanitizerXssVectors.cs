using FluentAssertions;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.Testing;

/// <summary>
/// The ONE XSS / mutation-XSS vector set every <see cref="IHtmlContentSanitizer"/> must survive
/// (D12/D22), linked into <c>tests/Tempo.Blazor.EmailTemplates.Abstractions.Tests</c> and
/// <c>tests/Tempo.Blazor.EmailTemplates.Engine.Tests</c> so the conservative BCL sanitizer and the
/// AngleSharp one are held to byte-for-byte the same contract instead of two hand-copied lists that
/// can drift.
/// </summary>
internal static class SanitizerXssVectors
{
    /// <summary>
    /// The shared payload set, linked from <c>tests/Shared/XssPayloads.cs</c> so the sanitizer contract and
    /// the AI markdown contract are held to the very same bytes. That file carries no sanitizer dependency,
    /// which is why the general test project can link it without an <c>InternalsVisibleTo</c> across the
    /// email assemblies.
    /// </summary>
    internal static string[] Payloads => XssPayloads.Payloads;

    /// <summary>
    /// The nonce an attacker forges when they try to make the masking/restore round-trip delete their own
    /// placeholder and thereby splice two harmless-looking pieces into executable markup.
    /// </summary>
    internal const string ForgedNonce = "0123456789abcdef0123456789abcdef";

    /// <summary>
    /// <c>SanitizeTemplateContent</c> vectors: a placeholder forged through a character reference (hex,
    /// decimal and without the terminating <c>;</c>) so the pre-check cannot see a private-use character,
    /// while the sanitizer decodes one, and the restore step then removes it — leaving
    /// <c>javascript:</c>, <c>//evil</c> or a <c>url(javascript:…)</c> style splice behind. Every payload
    /// carries a real <c>{{ x }}</c> span so the restore step actually runs (it is skipped when no span was
    /// masked).
    /// </summary>
    internal static readonly string[] TemplatePayloads =
    [
        // href: entity-encoded placeholder in the middle of the scheme.
        $"<a href=\"java&#xE000;{ForgedNonce}_0&#xE001;script:alert(1)\">{{{{ x }}}}</a>",
        // href: decimal form.
        $"<a href=\"java&#57344;{ForgedNonce}_0&#57345;script:alert(1)\">{{{{ x }}}}</a>",
        // href: closing reference without the terminating semicolon ("s" is not a hex digit).
        $"<a href=\"java&#xE000;{ForgedNonce}_0&#xE001script:alert(1)\">{{{{ x }}}}</a>",
        // href: protocol-relative URL assembled by removing the placeholder.
        $"<a href=\"/&#xE000;{ForgedNonce}_0&#xE001;/evil\">{{{{ x }}}}</a>",
        // href: an entity-encoded colon, judged before the placeholder collapses.
        $"<a href=\"&#xE000;{ForgedNonce}_0&#xE001;javascript&#58;alert(1)\">{{{{ x }}}}</a>",
        // style: the placeholder removal assembles both "url(" and "javascript:" after the policy check.
        $"<b style=\"background:u&#xE000;{ForgedNonce}_2&#xE001l(java&#xE000;{ForgedNonce}_3&#xE001script:alert(1))\">{{{{ x }}}}</b>",
        // text: the placeholder removal assembles the scheme as visible text.
        $"<b>java&#xE000;{ForgedNonce}_0&#xE001;script:alert(1)</b>{{{{ x }}}}",
        // index that a real masking never produces (out of range).
        $"<a href=\"java&#xE000;{ForgedNonce}_9&#xE001;script:alert(1)\">{{{{ x }}}}</a>",
    ];

    /// <summary>
    /// Proves the sanitizer output is markup-safe: no script/iframe/object/embed/link/meta/base/title
    /// element, no event-handler attribute, no <c>style</c> attribute, no executable URL scheme and no
    /// raw <c>&lt;!--</c> or <c>&lt;![CDATA[</c> sequence survives.
    /// </summary>
    internal static void AssertNoExecutableMarkup(string output, string because)
    {
        output.Should().NotContainEquivalentOf("<script", because);
        output.Should().NotContainEquivalentOf("<iframe", because);
        output.Should().NotContainEquivalentOf("<object", because);
        output.Should().NotContainEquivalentOf("<embed", because);
        output.Should().NotContainEquivalentOf("<link", because);
        output.Should().NotContainEquivalentOf("<meta", because);
        output.Should().NotContainEquivalentOf("<base", because);
        output.Should().NotContainEquivalentOf("<title", because);
        output.Should().NotContainEquivalentOf("<!--", because);
        output.Should().NotContainEquivalentOf("<![CDATA[", because);

        // Attribute-level checks, not naive substrings: encoded *text* saying "onerror" or
        // "javascript:" is inert, an attribute is not. MarkupGuard's scanner is the arbiter.
        foreach (var tag in HtmlTagTokenizer.Scan(output).Tokens)
        {
            foreach (var attribute in tag.Attrs)
            {
                var name = output.Substring(attribute.NameStart, attribute.NameEnd - attribute.NameStart);
                name.Should().NotStartWithEquivalentOf("on", because);
                if (attribute.HasValue)
                {
                    var value = output.Substring(attribute.ValueStart, attribute.ValueEnd - attribute.ValueStart);
                    if (name.Equals("style", StringComparison.OrdinalIgnoreCase))
                    {
                        // A style attribute is legal only while the CSS policy allows it — the arbiter is
                        // HtmlSanitizerPolicy.IsSafeStyle, so a spliced url(...)/expression() still fails.
                        HtmlSanitizerPolicy.IsSafeStyle(value).Should().BeTrue(
                            "{0}: style '{1}' is not safe", because, value);
                        continue;
                    }

                    var lowered = value.ToLowerInvariant()
                        .Replace("\t", string.Empty, StringComparison.Ordinal)
                        .Replace("\n", string.Empty, StringComparison.Ordinal)
                        .Replace("\r", string.Empty, StringComparison.Ordinal)
                        .TrimStart();
                    lowered.Should().NotStartWith("javascript:", because);
                    lowered.Should().NotStartWith("vbscript:", because);
                    lowered.Should().NotStartWith("data:", because);
                    lowered.Should().NotStartWith("//", because);
                    value.Should().NotContainEquivalentOf("expression(", because);
                }
            }
        }
    }

    /// <summary>
    /// Proves "output ⊆ policy": every start tag in the output is an allow-listed tag, carries only
    /// attributes the policy permits for it, and opens exactly the tags the policy allows. The scan
    /// reuses the production tokenizer, so the check cannot be fooled by a different parser.
    /// </summary>
    internal static void AssertOutputWithinPolicy(string output, bool conservative)
    {
        var scan = HtmlTagTokenizer.Scan(output);
        scan.TruncatedAt.Should().Be(-1, "sanitizer output must be well-formed enough to scan");
        foreach (var tag in scan.Tokens)
        {
            HtmlSanitizerPolicy.AllowedTags.Should().Contain(tag.Name,
                "the sanitizer may only emit allow-listed tags, found '{0}' in '{1}'", tag.Name, output);
            var allowedForTag = conservative && !tag.Name.Equals("a", StringComparison.OrdinalIgnoreCase)
                ? Array.Empty<string>()
                : (conservative ? ["href"] : (IEnumerable<string>)HtmlSanitizerPolicy.AllowedAttributes);
            foreach (var attribute in tag.Attrs)
            {
                var name = output.Substring(attribute.NameStart, attribute.NameEnd - attribute.NameStart);
                allowedForTag.Should().ContainEquivalentOf(name,
                    "attribute '{0}' is not allowed here ('{1}')", name, output);
            }
        }
    }

    /// <summary>
    /// Proves the masking round-trip left nothing of its own behind: a private-use placeholder character
    /// (U+E000/U+E001) in the output means a forged placeholder survived instead of being neutralised.
    /// </summary>
    internal static void AssertNoMaskArtifacts(string output, string because)
    {
        output.Should().NotContain("\uE000", because);
        output.Should().NotContain("\uE001", because);
    }

    /// <summary>A deterministic adjective/noun soup fuzzer, shared with the AI markdown suite.</summary>
    internal static IEnumerable<string> Fuzz(int count, int seed = 20261020) => XssPayloads.Fuzz(count, seed);
}
