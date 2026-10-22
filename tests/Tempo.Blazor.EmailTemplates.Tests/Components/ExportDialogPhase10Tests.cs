using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.EmailTemplates.Localization;
using Tempo.Blazor.EmailTemplates.Tests.Localization;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 10 (§2.4, B12/EM5): the export dialog is a three-mode code surface (MJML / JSON / HTML) with a
/// footer, a copy affordance and a real file download. The tests pin the contract the host relies on:
/// <list type="bullet">
/// <item>MJML is generated with <see cref="MjmlGeneratorOptions.ForExport"/> (lossless round-trip).</item>
/// <item>HTML goes through <see cref="IEmailPreviewRenderer"/> with <c>SubstituteVariables = false</c>, so
/// the exported file keeps the <c>{{ … }}</c> tokens, and it degrades readably when no renderer exists.</item>
/// <item>Nothing is generated on a parent re-render — only on OPEN and on a TAB CHANGE.</item>
/// <item>The download goes through the existing core helper <c>tmDataTable.downloadFile</c> with the
/// right file name and MIME type, and the copy affordance confirms in a live region.</item>
/// <item>A hostile document cannot get markup, an event handler or a <c>javascript:</c> URL into the
/// exported text.</item>
/// </list>
/// </summary>
public class ExportDialogPhase10Tests : BunitContext, IDisposable
{
    private readonly CultureScope _culture = CultureScope.Use("en");

    public ExportDialogPhase10Tests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ITmEmailLocalizer>(new EchoEmailLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddTempoEmailTemplateEditor();
        Services.AddHttpClient();
    }

    void IDisposable.Dispose()
    {
        _culture.Dispose();
        Dispose();
    }

    private static EmailTemplateDocument Doc(string name = "Welcome email", string content = "Hello")
    {
        var document = new EmailTemplateDocument { Name = name, Subject = "Hi {{ name }}" };
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(new EmailTextBlock { Content = content });
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }

    private static EmailTemplateDocument HostileDoc()
    {
        var document = new EmailTemplateDocument { Name = "Hostile" };
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(new EmailTextBlock
        {
            Content = "<script>alert(1)</script><img src=x onerror=\"alert(2)\">hello",
            CssClass = "\" onmouseover=\"alert(3)",
            ExtraAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["onclick"] = "alert(4)",
                ["href"] = "javascript:alert(5)",
                ["style"] = "background:url(javascript:alert(6))",
            },
        });
        column.Blocks.Add(new EmailButtonBlock { Text = "Go", Href = "javascript:alert(7)" });
        column.Blocks.Add(new EmailImageBlock { Src = "javascript:alert(8)", Alt = "x" });
        section.Columns.Add(column);
        document.Sections.Add(section);
        document.Styles.Styles.Add(new EmailStyle
        {
            Css = "body{color:red}</mj-style><script>alert(9)</script>",
        });
        return document;
    }

    private FakeExportRenderer RegisterRenderer(string html = "<html><body>{{ name }}</body></html>")
    {
        var renderer = new FakeExportRenderer(html);
        Services.AddSingleton<IEmailPreviewRenderer>(renderer);
        return renderer;
    }

    // ── modes ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MjmlTab_ShowsLosslessExportMarkup_InAReadOnlyCodeEditor()
    {
        var document = Doc();
        document.Styles.HtmlAttributes.Add(new MjHtmlSelector
        {
            Path = "body",
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["class"] = "x" },
        });

        var cut = Render<TmEmailExportDialog>(p => p.Add(c => c.Show, true).Add(c => c.Document, document));

        var code = cut.Find("[data-tm-export-mjml] textarea");
        code.HasAttribute("readonly").Should().BeTrue("the export surface is not an editor");
        var value = code.GetAttribute("value") ?? string.Empty;
        value.Should().Contain("<mjml");
        value.Should().Contain("mj-html-attributes", "ForExport keeps the round-trip payload");
    }

    [Fact]
    public void JsonTab_ShowsTheCanonicalJson()
    {
        var cut = Render<TmEmailExportDialog>(p => p.Add(c => c.Show, true).Add(c => c.Document, Doc()));

        cut.Find("#tab-export-json").Click();

        var value = cut.Find("[data-tm-export-json] textarea").GetAttribute("value") ?? string.Empty;
        value.Should().Contain("\"$type\"");
        value.Should().Contain("\"sections\"");
    }

    [Fact]
    public void HtmlTab_RendersThroughThePreviewRenderer_WithoutSubstitutingVariables()
    {
        var renderer = RegisterRenderer("<html><body>Hi {{ name }}</body></html>");
        var cut = Render<TmEmailExportDialog>(p => p.Add(c => c.Show, true).Add(c => c.Document, Doc()));

        cut.Find("#tab-export-html").Click();

        cut.WaitForAssertion(() =>
        {
            var value = cut.Find("[data-tm-export-html] textarea").GetAttribute("value") ?? string.Empty;
            value.Should().Contain("Hi {{ name }}", "the export keeps the placeholders the author wrote");
        });
        renderer.Requests.Should().NotBeEmpty();
        renderer.Requests[^1].SubstituteVariables.Should().BeFalse();
    }

    [Fact]
    public void HtmlTab_WithoutARenderer_ExplainsTheGapInsteadOfShowingAnEmptyBox()
    {
        var cut = Render<TmEmailExportDialog>(p => p.Add(c => c.Show, true).Add(c => c.Document, Doc()));

        cut.Find("#tab-export-html").Click();

        cut.Find("[data-tm-export-unavailable]").TextContent
            .Should().Contain("EmailEditor_Export_HtmlNotConfiguredHelp");
        cut.FindAll("[data-tm-export-retry]").Should().BeEmpty(
            "a missing renderer is a configuration gap, not a transient failure");
        cut.Find("[data-tm-export-download]").HasAttribute("disabled").Should().BeTrue(
            "there is no HTML to download");
    }

    // ── generation happens on open and on tab change, never per parent render ───────────────────

    [Fact]
    public void Mjml_IsGeneratedOnlyWhenTheDialogOpens_AndWhenTheTabChanges()
    {
        var document = Doc();
        var cut = Render<TmEmailExportDialog>(p => p.Add(c => c.Show, false).Add(c => c.Document, document));

        var generations = 0;
        cut.Instance.MjmlFactory = _ =>
        {
            generations++;
            return "<mjml></mjml>";
        };

        cut.Render(p => p.Add(c => c.Show, true).Add(c => c.Document, document));
        generations.Should().Be(1);

        // A parent re-render (Blazor calls OnParametersSet on every render, even with identical values).
        cut.Render(p => p.Add(c => c.Show, true).Add(c => c.Document, document));
        generations.Should().Be(1, "a re-render must not regenerate the export");

        cut.Find("#tab-export-json").Click();
        generations.Should().Be(1, "JSON does not need MJML");

        cut.Find("#tab-export-mjml").Click();
        generations.Should().Be(2, "switching back to MJML regenerates it once");
    }

    [Fact]
    public void Html_IsRenderedOnlyWhenTheTabIsOpened()
    {
        var renderer = RegisterRenderer();
        var document = Doc();
        var cut = Render<TmEmailExportDialog>(p => p.Add(c => c.Show, true).Add(c => c.Document, document));

        renderer.Requests.Should().BeEmpty("opening on MJML must not render HTML");

        cut.Render(p => p.Add(c => c.Show, true).Add(c => c.Document, document));
        renderer.Requests.Should().BeEmpty("a parent re-render must not render HTML");

        cut.Find("#tab-export-html").Click();
        cut.WaitForAssertion(() => renderer.Requests.Should().HaveCount(1));
    }

    // ── footer + download ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Download_UsesTheCoreHelper_WithTheSluggedNameAndTheMjmlMimeType()
    {
        var cut = Render<TmEmailExportDialog>(p => p.Add(c => c.Show, true).Add(c => c.Document, Doc("Welcome email")));

        var download = cut.Find("[data-tm-export-download]");
        download.TextContent.Should().Contain("EmailEditor_Export_Download");
        download.TextContent.Should().Contain("EmailEditor_Format_Mjml");
        download.Click();

        var invocation = JSInterop.Invocations["tmDataTable.downloadFile"].Should().ContainSingle().Subject;
        invocation.Arguments[0].Should().Be("welcome-email.mjml");
        invocation.Arguments[1].Should().Be("text/plain;charset=utf-8");
        invocation.Arguments[2].Should().BeOfType<string>().Which.Should().NotBeEmpty("the payload is base64");
    }

    [Fact]
    public void Download_WithoutAName_FallsBackToTheGenericFileName_PerFormat()
    {
        var cut = Render<TmEmailExportDialog>(p => p.Add(c => c.Show, true).Add(c => c.Document, Doc("!!!")));

        cut.Find("[data-tm-export-download]").Click();
        JSInterop.Invocations["tmDataTable.downloadFile"].Single().Arguments[0].Should().Be("email-template.mjml");

        cut.Find("#tab-export-json").Click();
        cut.Find("[data-tm-export-download]").Click();
        var jsonInvocation = JSInterop.Invocations["tmDataTable.downloadFile"].Last();
        jsonInvocation.Arguments[0].Should().Be("email-template.json");
        jsonInvocation.Arguments[1].Should().Be("application/json;charset=utf-8");
    }

    [Fact]
    public void Footer_OffersClose_AndTheDialogTitleIsExport()
    {
        var closed = false;
        var cut = Render<TmEmailExportDialog>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.Document, Doc())
            .Add(c => c.OnClose, () => closed = true));

        cut.Find(".tm-modal-title").TextContent.Should().Be("EmailEditor_Export_Title");
        cut.Find("[data-tm-export-close]").Click();
        closed.Should().BeTrue();
    }

    // ── copy affordance ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Copy_IsAnIconButtonInTheCodeCorner_AndConfirmsInAPoliteLiveRegion()
    {
        var cut = Render<TmEmailExportDialog>(p => p.Add(c => c.Show, true).Add(c => c.Document, Doc()));

        var copy = cut.Find("[data-tm-export-mjml] .tm-copy-button");
        copy.GetAttribute("aria-label").Should().Be("TmCopyButton_CopyToClipboard");
        cut.Find("[data-tm-export-announcer]").TextContent.Should().BeEmpty();

        copy.Click();

        JSInterop.Invocations["navigator.clipboard.writeText"].Should().ContainSingle();
        // The confirmation lands one render after the reset (two renders, so the region really changes).
        cut.WaitForAssertion(() => cut.Find("[data-tm-export-announcer]").TextContent
            .Should().Be("EmailEditor_Export_Copied"));
        cut.Find("[data-tm-export-announcer]").GetAttribute("aria-live").Should().Be("polite");
    }

    [Fact]
    public void SecondCopy_AnnouncesAgain()
    {
        // A live region whose text does not change is silent: the second copy has to reset the region first,
        // which only the counted seam can prove (the settled DOM looks the same after one and after two copies).
        // A click anywhere but the button is covered in the browser (E2E F10_5): the confirmation hangs off
        // TmCopyButton.OnCopied, not off a handler on the box around the code.
        var cut = Render<TmEmailExportDialog>(p => p.Add(c => c.Show, true).Add(c => c.Document, Doc()));

        cut.Find("[data-tm-export-mjml] .tm-copy-button").Click();
        cut.WaitForAssertion(() => cut.Instance.AnnouncementVersion.Should().Be(1));

        cut.Find("[data-tm-export-mjml] .tm-copy-button").Click();

        cut.WaitForAssertion(() => cut.Instance.AnnouncementVersion.Should().Be(2));
        cut.Find("[data-tm-export-announcer]").TextContent.Should().Be("EmailEditor_Export_Copied");
    }

    [Fact]
    public void UnreachableHtml_OffersARetry_ThatRendersAgain()
    {
        var renderer = new FlakyRenderer();
        Services.AddSingleton<IEmailPreviewRenderer>(renderer);
        var cut = Render<TmEmailExportDialog>(p => p.Add(c => c.Show, true).Add(c => c.Document, Doc()));

        cut.Find("#tab-export-html").Click();

        cut.WaitForAssertion(() => cut.Find("[data-tm-export-unavailable]").TextContent
            .Should().Contain("EmailEditor_Export_HtmlUnreachableHelp"));
        var retry = cut.Find("[data-tm-export-retry]");
        retry.TextContent.Should().Be("EmailEditor_Export_Retry");

        retry.Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-tm-export-html] textarea").GetAttribute("value").Should().Contain("<html>retried</html>");
        });
        renderer.Attempts.Should().Be(2);
    }

    [Fact]
    public void Tabs_HaveAPanelPerFormat_WithTheContentInsideIt()
    {
        // Fáze 10 review (UX 9): the tab strip's aria-controls pointed at a panel that did not exist, so the
        // content was a sibling of the strip instead of the tabpanel it belongs to.
        var cut = Render<TmEmailExportDialog>(p => p.Add(c => c.Show, true).Add(c => c.Document, Doc()));

        cut.Find("#tab-export-mjml").GetAttribute("aria-controls").Should().Be("tabpanel-export-mjml");
        var panel = cut.Find("#tabpanel-export-mjml");
        panel.GetAttribute("role").Should().Be("tabpanel");
        panel.GetAttribute("aria-labelledby").Should().Be("tab-export-mjml");
        panel.QuerySelector("[data-tm-export-mjml] textarea").Should().NotBeNull(
            "the code surface belongs INSIDE the panel it labels");

        cut.Find("#tab-export-json").Click();
        cut.Find("#tab-export-json").GetAttribute("aria-controls").Should().Be("tabpanel-export-json");
        cut.Find("#tabpanel-export-json").QuerySelector("[data-tm-export-json] textarea").Should().NotBeNull();
    }

    [Fact]
    public void LargeExport_IsShownWithoutHighlighting_AndSaysWhy()
    {
        // Prism over a megabyte of markup blocks the dialog that is showing it; the text itself is untouched.
        var document = Doc(content: new string('x', 200_001));
        var cut = Render<TmEmailExportDialog>(p => p.Add(c => c.Show, true).Add(c => c.Document, document));

        cut.Find("[data-tm-export-large]").TextContent.Should().Be("EmailEditor_Export_LargeNoHighlight");
        cut.Find("[data-tm-export-mjml] pre code").ClassList.Should().NotContain(
            c => c.StartsWith("language-", StringComparison.Ordinal),
            "highlighting is off above the limit");
        var value = cut.Find("[data-tm-export-mjml] textarea").GetAttribute("value") ?? string.Empty;
        value.Length.Should().BeGreaterThan(200_000, "the export itself is complete — only the colour is gone");
        value.Should().Contain("<mjml");
    }

    // ── security ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HostileDocument_ExportsNoMarkupEventHandlerOrJavascriptUrl()
    {
        // MJML is the export this dialog PRODUCES (the generator's sanitization, URL allow-list, filtered extra
        // attributes and a style payload that cannot leave its element). The HTML tab is the renderer's output,
        // so its guard is asserted where the renderer lives — RenderBlockContentTests proves the rendered HTML
        // carries no script, no handler and no javascript: URL, including with substitution off (the export
        // request shape), and LocalEmailPreviewRendererTests.HostileDocument_ExportRequest_… asserts the same on
        // the parsed DOM of an export request. The JSON tab is deliberately NOT part of this test: it is the
        // lossless DATA round-trip the import reads back, so a blocked href stays in the model exactly so the
        // author can fix it — every emission path fail-closes it instead. NOTE (Fáze 10 review, U1): a raw block
        // is an opt-in to raw MARKUP, never to markup that RUNS — the renderer's markup guard removes
        // script/iframe/object/… from a raw block too, so the old "the author asked for it" exemption is gone.
        var document = HostileDoc();
        var cut = Render<TmEmailExportDialog>(p => p.Add(c => c.Show, true).Add(c => c.Document, document));

        foreach (var (tab, editor) in new[]
        {
            ("#tab-export-mjml", "[data-tm-export-mjml] textarea"),
            ("#tab-export-json", "[data-tm-export-json] textarea"),
        })
        {
            cut.Find(tab).Click();
            cut.WaitForAssertion(() => cut.FindAll(editor).Count.Should().Be(1));
            var text = cut.Find(editor).GetAttribute("value") ?? string.Empty;
            if (tab.Contains("json")) continue; // the data round-trip, see the comment above

            text.Should().NotContain("<script", "markup in an imported value must never reach the export");
            text.Should().NotContain("</mj-style><", "a style payload may not end its own element");
            // A handler can only act as an ATTRIBUTE of a real tag; the same words inside escaped text content
            // (&lt;img onerror=…&gt;) are inert, so the assertion is on the tag shape, not on the substring.
            EventHandlerOnATag.IsMatch(text).Should().BeFalse("an event-handler attribute name is filtered");
            text.Should().NotContain("style=\"background", "an unknown style attribute is dropped");
            text.Should().NotContain("javascript:", "a blocked URL scheme becomes '#' or is dropped");
        }
    }

    /// <summary>A real tag start followed by an event-handler attribute (<c>&lt;a onclick="…"&gt;</c>).</summary>
    private static readonly System.Text.RegularExpressions.Regex EventHandlerOnATag = new(
        "<[a-zA-Z][^>]*\\son[a-z]+\\s*=",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    /// <summary>A renderer that fails the first call (a dropped connection) and succeeds afterwards.</summary>
    private sealed class FlakyRenderer : IEmailPreviewRenderer
    {
        public int Attempts { get; private set; }

        public Task<EmailPreviewResult> RenderAsync(EmailPreviewRequest request, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Attempts == 1) throw new InvalidOperationException("connection reset");

            return Task.FromResult(new EmailPreviewResult(
                "<html>retried</html>", "text", "subject", null, [], EmailPreviewStatus.Rendered));
        }
    }

    private sealed class FakeExportRenderer : IEmailPreviewRenderer
    {
        private readonly string _html;

        public FakeExportRenderer(string html) => _html = html;

        public List<EmailPreviewRequest> Requests { get; } = [];

        public Task<EmailPreviewResult> RenderAsync(EmailPreviewRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new EmailPreviewResult(
                _html, "text", "subject", null, [], EmailPreviewStatus.Rendered));
        }
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments] => key;
    }

    /// <summary>Echoes the key and appends the format arguments so a formatted key is still assertable.</summary>
    private sealed class EchoEmailLocalizer : Tempo.Blazor.EmailTemplates.Localization.ITmEmailLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments]
            => arguments.Length == 0 ? key : key + "|" + string.Join("|", arguments);
    }
}
