using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Testing;

/// <summary>
/// The ONE XSS / mutation-XSS payload set an untrusted-markup pipeline must survive.
/// </summary>
/// <remarks>
/// <para>
/// This file deliberately has NO dependency on a sanitizer: no <c>Tempo.Blazor.EmailTemplates.Abstractions</c>
/// internals, no production tokenizer, no policy type. That is what lets the email test projects (through
/// <c>SanitizerXssVectors</c>, which adds the production-parser assertions) AND the general
/// <c>Tempo.Blazor.Tests</c> project (the AI markdown pipeline) link the very same bytes. The alternative —
/// copying the array into each project — is a list that drifts, and a drifted XSS vector set silently stops
/// covering the payload someone removed from the other copy.
/// </para>
/// <para>
/// <see cref="AssertNoExecutableMarkup"/> here is the dependency-free contract used by a renderer that
/// emits its own markup vocabulary (the AI markdown renderer escapes all author HTML, so its output is
/// parser-simple). A sanitizer that rewrites hostile HTML keeps its own stricter check on the production
/// tokenizer — <c>SanitizerXssVectors.AssertNoExecutableMarkup</c> — because there the output must be
/// judged by the same parser that will read it in production.
/// </para>
/// </remarks>
internal static class XssPayloads
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A start tag in the rendered output. Attributes are read only inside one of these, so encoded TEXT
    /// that happens to spell <c>href='javascript:…'</c> — the whole payload after escaping — cannot trip
    /// the attribute checks. Markdig escapes <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c> and <c>"</c> in text,
    /// so an escaped payload never looks like a tag.
    /// </summary>
    private static readonly Regex StartTag = new(
        @"<(?<name>[a-zA-Z][a-zA-Z0-9-]*)(?<attributes>[^<>]*)>",
        RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>One quoted attribute inside a start tag.</summary>
    private static readonly Regex Attribute = new(
        @"(?<name>[a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*(?<quote>[""'])(?<value>.*?)(?:\k<quote>)",
        RegexOptions.Compiled | RegexOptions.Singleline,
        RegexTimeout);

    /// <summary>Classic and mutation-XSS payloads: script/event/URL smuggling plus parser-confusion.</summary>
    internal static readonly string[] Payloads =
    [
        "<script>alert(1)</script>",
        "<SCRIPT>alert(1)</SCRIPT>",
        "<scr<script>ipt>alert(1)</scr</script>ipt>",
        "<script src=//evil/x.js></script>",
        "<script>alert(1)",
        "<img src=x onerror=alert(1)>",
        "<img src=x onerror=alert(1) />",
        "<svg onload=alert(1)>",
        "<svg><style><img src=x onerror=alert(1)></style></svg>",
        "<svg><script>alert(1)</script></svg>",
        "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>",
        "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">",
        "<a href=\"javascript:alert(1)\">x</a>",
        "<a href='javascript:alert(1)'>x</a>",
        "<a href=javascript:alert(1)>x</a>",
        "<a href=\"java\tscript:alert(1)\">x</a>",
        "<a href=\"java\nscript:alert(1)\">x</a>",
        "<a href=\"&#106;avascript:alert(1)\">x</a>",
        "<a href=\"javascript&#58;alert(1)\">x</a>",
        "<a href=\"vbscript:msgbox(1)\">x</a>",
        "<a href=\"data:text/html;base64,PHNjcmlwdD4=\">x</a>",
        "<a href=\" javascript:alert(1)\">x</a>",
        "<a href=\"\u0001javascript:alert(1)\">x</a>",
        "<a href=\"//evil.example/x\">x</a>",
        "<a href=\"https://ok.example/x\" onclick=\"alert(1)\">x</a>",
        "<b style=\"background:url(javascript:alert(1))\">x</b>",
        "<b style=\"width:expression(alert(1))\">x</b>",
        "<div onmouseover=alert(1)>x</div>",
        "<!--<script>alert(1)</script>-->",
        "<!--><script>alert(1)</script>-->",
        "<!---><script>alert(1)</script>-->",
        "<!--><img src=x onerror=alert(1)>-->",
        "<![CDATA[<script>alert(1)</script>]]>",
        "<title><script>alert(1)</script></title>",
        "<textarea><script>alert(1)</script></textarea>",
        "<iframe src=\"javascript:alert(1)\"></iframe>",
        "<object data=\"javascript:alert(1)\"></object>",
        "<embed src=\"javascript:alert(1)\">",
        "<base href=\"javascript:alert(1)//\">",
        "<meta http-equiv=\"refresh\" content=\"0;url=javascript:alert(1)\">",
        "<link rel=stylesheet href=\"javascript:alert(1)\">",
        "<form action=\"javascript:alert(1)\"><input name=x></form>",
        "<b><i></b></i>",
        "<b>x<i>y</b>z</i>",
        "</b>",
        "</mj-text></mj-raw>",
        "<mj-text onload=alert(1)>x</mj-text>",
        "<b>x</b></mj-text><script>alert(1)</script>",
        "<b",
        "<b>",
        "<a href=\"https://ok.example/x",
        "<!-",
        "<svg/onload=alert(1)>",
        "<a href=\"https://ok.example/x\" target=\"_blank\" rel=\"noopener\">x</a>",
        // Headings are allow-listed (D28) and carry NO attribute: a heading is a formatting element, not a
        // container for an event handler or a style that could paint over the editor.
        "<h1 onclick=alert(1) style=\"background:url(javascript:alert(1))\">Ahoj</h1>",
        "<h2><img src=x onerror=alert(1)></h2>",
        "<h3 style=\"position:fixed;inset:0\">x</h3>",
        "<h4 onmouseover=alert(1)>x</h4>",
        "<h5>x</h5><h6>y</h6>",
    ];

    /// <summary>A deterministic adjective/noun soup fuzzer: adversarial but reproducible.</summary>
    internal static IEnumerable<string> Fuzz(int count, int seed = 20261020)
    {
        string[] parts =
        [
            "<", ">", "&", "\"", "'", "/", "!", "-", "=", " ", "a", "b", "i", "br", "span", "p", "ul",
            "li", "strong", "em", "mj-text", "mj-raw", "script", "svg", "style", "onload", "href",
            "javascript:", "http://x.y/z", "#", "{{ a }}", "{% if a %}", "\u00a0", "\uE000", "\\",
            ";", ":", "&#106;", "&amp;", "&lt;", "-->", "<!--", "&#xE000;", "&#xE001;", "_0", "{{ x }}",
        ];

        var random = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            var builder = new StringBuilder();
            var length = random.Next(1, 14);
            for (var part = 0; part < length; part++)
            {
                builder.Append(parts[random.Next(parts.Length)]);
            }

            yield return builder.ToString();
        }
    }

    /// <summary>
    /// Dependency-free proof that rendered output carries no active content: no element that can execute
    /// or load a document, no comment/CDATA sequence, no <c>on*</c> attribute, no <c>style</c> attribute and
    /// no attribute whose value starts with an executable URL scheme.
    /// </summary>
    /// <remarks>
    /// The check is attribute-aware rather than substring-based on purpose: encoded TEXT saying
    /// <c>onerror</c> or <c>javascript:</c> is inert, an attribute is not. It reads only quoted values, so a
    /// renderer that emits an unquoted attribute fails loudly instead of slipping through.
    /// </remarks>
    internal static void AssertNoExecutableMarkup(string markup, string because)
    {
        foreach (var forbidden in ForbiddenElements)
        {
            markup.Should().NotContainEquivalentOf($"<{forbidden}", because);
        }

        markup.Should().NotContainEquivalentOf("<!--", because);
        markup.Should().NotContainEquivalentOf("<![CDATA[", because);

        foreach (Match tag in StartTag.Matches(markup))
        {
            foreach (Match attribute in Attribute.Matches(tag.Groups["attributes"].Value))
            {
                var name = attribute.Groups["name"].Value;
                name.Should().NotStartWithEquivalentOf("on", because);
                name.Should().NotBeEquivalentTo("style", because);

                var value = attribute.Groups["value"].Value;
                if (!name.Equals("href", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("src", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var lowered = value
                    .Replace("\t", string.Empty, StringComparison.Ordinal)
                    .Replace("\n", string.Empty, StringComparison.Ordinal)
                    .Replace("\r", string.Empty, StringComparison.Ordinal)
                    .TrimStart();
                lowered.Should().NotStartWithEquivalentOf("javascript:", because);
                lowered.Should().NotStartWithEquivalentOf("vbscript:", because);
                lowered.Should().NotStartWithEquivalentOf("data:", because);
                lowered.Should().NotStartWithEquivalentOf("//", because);
            }
        }
    }

    private static readonly string[] ForbiddenElements =
    [
        "script", "iframe", "object", "embed", "link", "meta", "base", "title", "style", "form",
        "textarea", "noscript", "template",
    ];
}
