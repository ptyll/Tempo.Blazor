using Bunit;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.EmailTemplates.Localization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 6 (contract §3.4/D17): the preview talks to <see cref="IEmailPreviewRenderer"/> only, so the render
/// may run on the server while the editor runs in Blazor WebAssembly. This suite pins the states
/// (loading / rendered / render errors / warnings / unavailable + retry), the cancellation contract, the
/// device and substitution switches, the HTML/Text toggle, the sample-data editor, and the fact that HTML
/// from a server renderer is still untrusted — it only ever lands in a <c>sandbox=""</c> iframe's srcdoc.
/// </summary>
public class PreviewServerRendererTests : BunitContext
{
    public PreviewServerRendererTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        // Echo the editor's keys instead of the shipped JSON strings, so the assertions can name keys.
        Services.AddSingleton<ITmEmailLocalizer>(new EchoEmailLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddTempoEmailTemplateEditor();
        Services.AddHttpClient();
    }

    private static EmailTemplateDocument Doc(string content = "Hello", string subject = "Subj")
    {
        var doc = new EmailTemplateDocument { Subject = subject };
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(new EmailTextBlock { Content = content });
        section.Columns.Add(column);
        doc.Sections.Add(section);
        return doc;
    }

    private FakePreviewRenderer RegisterRenderer()
    {
        var renderer = new FakePreviewRenderer();
        Services.AddSingleton<IEmailPreviewRenderer>(renderer);
        Services.AddSingleton(TimeProvider.System);
        return renderer;
    }

    // ── injection seam ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Preview_InjectsTheRenderer_AndPutsServerHtmlIntoASandboxedIframe()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html><body>from the server</body></html>"));

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));
        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-frame]").GetAttribute("srcdoc")
            .Should().Contain("from the server"));

        var frame = cut.Find("[data-tm-preview-frame]");
        frame.GetAttribute("sandbox").Should().BeEmpty(
            "server HTML is untrusted too: sandbox=\"\" (no allow-scripts, no allow-same-origin)");
        frame.HasAttribute("allow").Should().BeFalse();
    }

    [Fact]
    public void Preview_SendsTheDocumentVariablesAndCulture()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html>x</html>"));

        var cut = Render<TmEmailTemplatePreview>(p => p
            .Add(c => c.Document, Doc("Hi {{ name }}", "Hi {{ name }}"))
            .Add(c => c.VariablesJson, "{\"name\":\"Ada\"}"));

        cut.WaitForAssertion(() => renderer.Requests.Should().NotBeEmpty());
        var request = renderer.Requests[0];
        request.Document.Subject.Should().Be("Hi {{ name }}");
        request.VariablesJson.Should().Be("{\"name\":\"Ada\"}");
        request.SubstituteVariables.Should().BeTrue();
    }

    // ── states ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Preview_Unavailable_ShowsLocalizedStateAndRetries()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(EmailPreviewResultUnavailable());
        renderer.Enqueue(Rendered("<html><body>second try</body></html>"));

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-unavailable]"));
        cut.Find("[data-tm-preview-unavailable]").TextContent.Should().Contain("EmailEditor_Preview_Unavailable");
        cut.Find("[data-tm-preview-unavailable]").TextContent
            .Should().Contain("EmailEditor_Preview_UnreachableHelp", "a failed render is retryable");

        cut.Find("[data-tm-preview-retry]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-frame]").GetAttribute("srcdoc")
            .Should().Contain("second try"));
        cut.FindAll("[data-tm-preview-unavailable]").Should().BeEmpty();
        renderer.Requests.Should().HaveCount(2);
    }

    [Fact]
    public void Preview_WithoutAnyRendererRegistration_DegradesToUnavailable_NotToAnEmptyIframe()
    {
        Services.AddSingleton(TimeProvider.System);

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-unavailable]"));
        cut.Find("[data-tm-preview-unavailable]").TextContent
            .Should().Contain("EmailEditor_Preview_NotConfiguredHelp");
        cut.FindAll("[data-tm-preview-frame]").Should().BeEmpty("no silent empty iframe");
    }

    [Fact]
    public void Preview_WithoutAnyRendererRegistration_ShowsTheNotConfiguredHelp_WithoutNamingTheInterface()
    {
        // §3.4 UX: an end user can act on "the preview is not set up in this application; contact your
        // administrator", not on a service name. The type/package names belong in the LOG entry (covered
        // below), never in the text the user reads. Uses the real DefaultTmEmailLocalizer.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        context.Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        context.Services.AddTempoEmailTemplateEditor();

        var cut = context.Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-unavailable]"));
        var text = cut.Find("[data-tm-preview-unavailable]").TextContent;
        text.Should().Contain("not set up in this application", "the shipped NotConfigured help");
        text.Should().NotContain("IEmailPreviewRenderer");
        text.Should().NotContain("Tempo.Blazor.EmailTemplates.Engine");
        cut.FindAll("[data-tm-preview-retry]").Should().BeEmpty(
            "a missing registration cannot be fixed by retrying");
    }

    [Fact]
    public void Preview_RendererFailure_ShowsTheUnreachableHelp_AndTheInterfaceNameOnlyInTheLog()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        context.Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        context.Services.AddTempoEmailTemplateEditor();
        context.Services.AddSingleton<IEmailPreviewRenderer>(new ThrowingRenderer(new HttpRequestException("socket closed")));

        var cut = context.Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-unavailable]"));
        var text = cut.Find("[data-tm-preview-unavailable]").TextContent;
        text.Should().Contain("could not be loaded from the server");
        text.Should().NotContain("IEmailPreviewRenderer");
        cut.Find("[data-tm-preview-retry]").Should().NotBeNull("a network failure is retryable");
    }

    [Fact]
    public void Preview_WithoutAnyRendererRegistration_LogsTheMissingRegistrationOnce()
    {
        // §3.0/§3.5: "chybějící registrace = jasná chyba při prvním použití s lokalizovanou zprávou
        // v náhledu + log". The localized message is covered above; this pins the LOG half, and that it is
        // not repeated on every re-render (the preview re-renders on each parameter set, so a log per
        // render would flood a WASM host's console).
        var logger = Substitute.For<ILogger<TmEmailTemplatePreview>>();
        Services.AddSingleton(logger);

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));
        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-unavailable]"));

        cut.Render(p => p.Add(c => c.Document, Doc()));
        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-unavailable]"));

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
        var logged = logger.ReceivedCalls()
            .Select(call => call.GetArguments()[2]?.ToString() ?? string.Empty)
            .ToList();
        logged.Should().Contain(message => message.Contains("IEmailPreviewRenderer"),
            "the log entry has to name the service the host forgot to register");
    }

    [Fact]
    public void Preview_RenderFailed_ShowsErrorsAndNoHtml()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(new EmailPreviewResult(
            string.Empty, string.Empty, string.Empty, null,
            [new RenderError("boom", 1, 2)], EmailPreviewStatus.RenderFailed));

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-errors]").TextContent.Should().Contain("boom"));
        cut.Find("[data-tm-preview-frame]").GetAttribute("srcdoc").Should().BeEmpty();
    }

    [Fact]
    public void Preview_Warnings_KeepTheRenderedHtml()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(new EmailPreviewResult(
            "<html>body</html>", "text", "subject", null,
            [new RenderError("blocked url", null, null, RenderKeys.BlockedUrlScheme, RenderErrorSeverity.Warning)],
            EmailPreviewStatus.Rendered));

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-warnings]"));
        cut.Find("[data-tm-preview-frame]").GetAttribute("srcdoc").Should().Contain("body");
        cut.FindAll("[data-tm-preview-errors]").Should().BeEmpty();
    }

    [Fact]
    public void Preview_StatusRegions_ArePoliteLiveRegions()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html>x</html>"));

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-status]"));
        cut.Find("[data-tm-preview-status]").GetAttribute("aria-live").Should().Be("polite");
    }

    // ── cancellation / staleness ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_NewerRender_CancelsThePreviousOne()
    {
        var renderer = RegisterRenderer();
        var first = renderer.EnqueuePending();
        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));
        cut.WaitForAssertion(() => renderer.Requests.Should().HaveCount(1));

        var second = renderer.EnqueuePending();
        // Fire-and-forget on the dispatcher: the render is still pending, so awaiting RefreshAsync here
        // would deadlock the test (the answer is handed out below).
        _ = cut.InvokeAsync(() => { _ = cut.Instance.RefreshAsync(); });
        cut.WaitForAssertion(() => renderer.Requests.Should().HaveCount(2));

        renderer.Tokens[0].IsCancellationRequested.Should().BeTrue(
            "a newer render cancels the previous CancellationToken");

        second.SetResult(Rendered("<html>new</html>"));
        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-frame]").GetAttribute("srcdoc").Should().Contain("new"));

        // The stale answer arrives last and must never overwrite the newer one.
        first.SetResult(Rendered("<html>stale</html>"));
        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-frame]").GetAttribute("srcdoc").Should().Contain("new"));
        cut.Find("[data-tm-preview-frame]").GetAttribute("srcdoc").Should().NotContain("stale");
    }

    // ── device switch ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Preview_DeviceSwitch_IsAPillTabStripWithIconsAndLocalizedLabels()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html>x</html>"));

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));

        cut.WaitForAssertion(() => cut.FindAll("[role='tablist']").Count.Should().BeGreaterThan(0));
        cut.Find("#tab-preview-device-desktop").GetAttribute("aria-selected").Should().Be("true");
        cut.Find("#tab-preview-device-tablet").GetAttribute("aria-selected").Should().Be("false");
        cut.Find("#tab-preview-device-mobile").GetAttribute("aria-selected").Should().Be("false");
        cut.Find("#tab-preview-device-desktop").TextContent.Should().Contain("EmailEditor_Preview_Desktop");
        cut.Find("#tab-preview-device-tablet").QuerySelector("svg").Should().NotBeNull("the device tabs carry icons");
    }

    [Theory]
    [InlineData(EmailPreviewDevice.Desktop, 680)]
    [InlineData(EmailPreviewDevice.Tablet, 768)]
    [InlineData(EmailPreviewDevice.Mobile, 390)]
    public void Preview_SceneWidth_ComesFromTheDevice(EmailPreviewDevice device, int expectedWidth)
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html>x</html>"));

        // Document.Styles.ContentWidth defaults to 600px; Desktop adds the stage margin (80px).
        var cut = Render<TmEmailTemplatePreview>(p => p
            .Add(c => c.Document, Doc())
            .Add(c => c.InitialDevice, device));

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-stage]")
            .GetAttribute("data-tm-preview-stage-width").Should().Be(expectedWidth.ToString()));
    }

    [Fact]
    public void Preview_SceneWidth_FollowsTheDocumentContentWidth()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html>x</html>"));
        var doc = Doc();
        doc.Styles.ContentWidth = "720px";

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, doc));

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-stage]")
            .GetAttribute("data-tm-preview-stage-width").Should().Be("800"));
    }

    [Fact]
    public void Preview_DeviceSwitch_ChangesTheScene()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html>x</html>"));

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));
        cut.Find("#tab-preview-device-mobile").Click();

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-stage]")
            .GetAttribute("data-tm-preview-stage-width").Should().Be("390"));
        cut.Find("#tab-preview-device-mobile").GetAttribute("aria-selected").Should().Be("true");
    }

    // ── substitution switch ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Preview_SubstitutionSwitch_MapsToSubstituteVariables()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html>x</html>"));

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc("Hi {{ name }}")));
        cut.WaitForAssertion(() => renderer.Requests.Should().HaveCount(1));

        cut.Find("#tab-preview-substitute-off").Click();

        cut.WaitForAssertion(() => renderer.Requests.Count.Should().BeGreaterThan(1));
        renderer.Requests[^1].SubstituteVariables.Should().BeFalse();
        cut.Find("#tab-preview-substitute-off").GetAttribute("aria-selected").Should().Be("true");
    }

    // ── sample data editor ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Preview_SampleDataEditor_IsBehindAnEditDataButton_AndUsesACodeEditor()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html>x</html>"));

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc("Hi {{ name }}")));

        cut.FindAll("[data-tm-preview-data-editor]").Should().BeEmpty("the editor is collapsed by default");
        cut.FindAll("details").Should().BeEmpty("the sample data editor is a button, not a <details>");

        cut.Find("[data-tm-preview-edit-data]").Click();

        var editor = cut.Find("[data-tm-preview-data-editor]");
        editor.QuerySelector("textarea").Should().NotBeNull("the sample data uses TmCodeEditor");
        editor.QuerySelector("textarea")!.GetAttribute("rows").Should().NotBeNull();
    }

    [Fact]
    public void Preview_SampleDataEdits_AreDebouncedAndReportedThroughVariablesJsonChanged()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html>x</html>"));
        var time = new FakeTimeProvider();
        var reported = new List<string>();

        var cut = Render<TmEmailTemplatePreview>(p => p
            .Add(c => c.Document, Doc("Hi {{ name }}"))
            .Add(c => c.TimeProvider, time)
            .Add(c => c.VariablesJsonChanged, v => reported.Add(v)));

        cut.Find("[data-tm-preview-edit-data]").Click();
        cut.Find("[data-tm-preview-data-editor] textarea").Input("{\"name\":\"Bob\"}");

        reported.Should().Contain("{\"name\":\"Bob\"}");
        var before = renderer.Requests.Count;
        time.Advance(TimeSpan.FromMilliseconds(750));

        cut.WaitForAssertion(
            () => renderer.Requests.Count.Should().BeGreaterThan(before,
                "the debounce fires after 750 ms"));
        renderer.Requests[^1].VariablesJson.Should().Contain("Bob");
    }

    [Fact]
    public void Preview_ShowSampleDataEditorFalse_HidesTheEditButton()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html>x</html>"));

        var cut = Render<TmEmailTemplatePreview>(p => p
            .Add(c => c.Document, Doc())
            .Add(c => c.ShowSampleDataEditor, false));

        cut.FindAll("[data-tm-preview-edit-data]").Should().BeEmpty();
    }

    // ── view toggle + footer ────────────────────────────────────────────────────────────────

    [Fact]
    public void Preview_ShowViewToggleFalse_HidesTheHtmlTextSwitch()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html>x</html>"));

        var cut = Render<TmEmailTemplatePreview>(p => p
            .Add(c => c.Document, Doc())
            .Add(c => c.ShowViewToggle, false));

        cut.FindAll("#tab-preview-view-html").Should().BeEmpty();
        cut.FindAll("#tab-preview-view-text").Should().BeEmpty();
    }

    [Fact]
    public void Preview_TextToggle_ShowsThePlainText()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(new EmailPreviewResult("<html>x</html>", "plain text version", "subject", null, [], EmailPreviewStatus.Rendered));

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));
        cut.WaitForAssertion(() => cut.Find("#tab-preview-view-text"));
        cut.Find("#tab-preview-view-text").Click();

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-text]").TextContent.Should().Contain("plain text version"));
    }

    [Fact]
    public void Preview_FooterActions_AreRenderedInTheToolbar()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html>x</html>"));

        var cut = Render<TmEmailTemplatePreview>(p => p
            .Add(c => c.Document, Doc())
            .Add(c => c.FooterActions, builder => builder.AddMarkupContent(0,
                "<button type=\"button\" data-test-host-action>Send test</button>")));

        cut.Find("[data-tm-preview-footer] [data-test-host-action]").Should().NotBeNull();
    }

    // ── F6 review fixes: cancellation, re-render key, focus, aria-busy, frame title, paper, empty ──

    [Fact]
    public void Preview_NonCancellationTaskCanceled_IsUnavailableNotStuckLoading()
    {
        // A TaskCanceledException that did NOT come from our own token is a real failure (an HttpClient
        // timeout is exactly this shape). Swallowing every OperationCanceledException left the component in
        // Loading forever with no way out.
        var renderer = RegisterRenderer();
        renderer.EnqueueException(new TaskCanceledException("The request timed out."));

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-unavailable]"));
        cut.Find("[data-tm-preview-unavailable]").TextContent
            .Should().Contain("EmailEditor_Preview_UnreachableHelp");
        cut.FindAll("[data-tm-preview-loading]").Should().BeEmpty("the loading state must end");
        cut.Find("[data-tm-preview-root]").GetAttribute("aria-busy").Should().Be("false");
        cut.Find("[data-tm-preview-retry]").Should().NotBeNull();
    }

    [Fact]
    public void Preview_ThousandsOfIdenticalParameterSets_RenderOnce()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html>one</html>"));
        var doc = Doc();

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, doc));
        cut.WaitForAssertion(() => renderer.Requests.Should().HaveCount(1));

        // The editor re-renders its child on every keystroke: the preview must not re-render the same
        // document through the server for an identical request.
        cut.Render(p => p.Add(c => c.Document, doc));
        cut.Render(p => p.Add(c => c.Document, doc));

        renderer.Requests.Should().HaveCount(1, "the request key of the render is unchanged");
    }

    [Fact]
    public void Preview_RapidExternalVariableChanges_AreDebouncedIntoOneRender()
    {
        var renderer = RegisterRenderer();
        var time = new FakeTimeProvider();

        var cut = Render<TmEmailTemplatePreview>(p => p
            .Add(c => c.Document, Doc("Hi {{ name }}"))
            .Add(c => c.TimeProvider, time)
            .Add(c => c.VariablesJson, "{\"name\":\"A\"}"));
        cut.WaitForAssertion(() => renderer.Requests.Should().HaveCount(1));

        cut.Render(p => p.Add(c => c.VariablesJson, "{\"name\":\"B\"}"));
        cut.Render(p => p.Add(c => c.VariablesJson, "{\"name\":\"C\"}"));
        cut.Render(p => p.Add(c => c.VariablesJson, "{\"name\":\"D\"}"));

        renderer.Requests.Should().HaveCount(1, "an external data change is debounced, not rendered immediately");

        time.Advance(TimeSpan.FromMilliseconds(750));

        cut.WaitForAssertion(() => renderer.Requests.Should().HaveCount(2, "the debounce coalesces the three changes"));
        renderer.Requests[^1].VariablesJson.Should().Contain("D", "the newest value wins");
    }

    [Fact]
    public async Task Preview_RefreshAsync_ForcesARenderWithoutAChange()
    {
        var renderer = RegisterRenderer();

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));
        cut.WaitForAssertion(() => renderer.Requests.Should().HaveCount(1));

        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        renderer.Requests.Should().HaveCount(2, "the refresh button forces a new render");
    }

    [Fact]
    public void Preview_Stage_IsProgrammaticFocusTarget_AndASuccessfulRetryMovesFocusIntoIt()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(EmailPreviewResultUnavailable());
        renderer.Enqueue(Rendered("<html>second try</html>"));

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));
        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-unavailable]"));

        cut.Find("[data-tm-preview-stage]").GetAttribute("tabindex").Should().Be("-1",
            "the stage is a programmatic focus target, never a tab stop");

        cut.Find("[data-tm-preview-retry]").Click();
        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-frame]"));

        cut.WaitForAssertion(() => JSInterop.Invocations
            .Should().Contain(invocation => invocation.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase),
                "the focus moves into the freshly rendered preview"));
    }

    [Fact]
    public void Preview_RootIsAriaBusy_AndTheStageDimsTheStaleHtml()
    {
        var renderer = RegisterRenderer();
        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));
        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-frame]"));

        cut.Find("[data-tm-preview-root]").GetAttribute("aria-busy").Should().Be("false");

        var pending = renderer.EnqueuePending();
        _ = cut.InvokeAsync(() => { _ = cut.Instance.RefreshAsync(); });
        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-root]").GetAttribute("aria-busy").Should().Be("true"));
        cut.Find("[data-tm-preview-stage]").ClassList.Should()
            .Contain("tm-email-preview__stage--busy", "the stale HTML is dimmed while the new render runs");

        pending.SetResult(Rendered("<html>second</html>"));
        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-root]").GetAttribute("aria-busy").Should().Be("false"));
        cut.Find("[data-tm-preview-stage]").ClassList.Should().NotContain("tm-email-preview__stage--busy");
    }

    [Fact]
    public void Preview_Unavailable_KeepsTheStageWithTheAlertInsideIt()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(EmailPreviewResultUnavailable());

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));
        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-unavailable]"));

        cut.Find("[data-tm-preview-stage] [data-tm-preview-unavailable]").Should().NotBeNull(
            "the alert lives inside the stage so the dialog does not jump");
    }

    [Fact]
    public void Preview_FrameTitle_NamesTheLocalizedDevice()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html>x</html>"));

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));
        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-frame]"));
        cut.Find("[data-tm-preview-frame]").GetAttribute("title")
            .Should().Be("EmailEditor_Preview_FrameTitle|EmailEditor_Preview_Desktop");

        cut.Find("#tab-preview-device-mobile").Click();

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-frame]").GetAttribute("title")
            .Should().Be("EmailEditor_Preview_FrameTitle|EmailEditor_Preview_Mobile"));
    }

    [Fact]
    public void Preview_PaperBackground_ComesFromTheDocumentButOnlyWhenItIsAHexOrRgbColor()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html>x</html>"));
        var doc = Doc();
        doc.Styles.BackgroundColor = "#102030";

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, doc));

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-stage]").GetAttribute("style")
            .Should().Contain("--tm-email-preview-paper-bg: #102030"));

        var rgb = Doc();
        rgb.Styles.BackgroundColor = "rgb(16, 32, 48)";
        cut.Render(p => p.Add(c => c.Document, rgb));
        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-stage]").GetAttribute("style")
            .Should().Contain("--tm-email-preview-paper-bg: rgb(16, 32, 48)"));

        var hostile = Doc();
        hostile.Styles.BackgroundColor = "url(javascript:alert(1))";
        cut.Render(p => p.Add(c => c.Document, hostile));
        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-stage]").GetAttribute("style")
            .Should().NotContain("--tm-email-preview-paper-bg"));
    }

    [Fact]
    public void Preview_EmptyDocument_ShowsAnEmptyStateInsteadOfAnEmptyPaper()
    {
        var renderer = RegisterRenderer();
        renderer.Enqueue(Rendered("<html></html>"));

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, new EmailTemplateDocument { Subject = "S" }));

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-empty]"));
        cut.Find("[data-tm-preview-empty]").TextContent.Should().Contain("EmailEditor_Preview_Empty");
        cut.FindAll("[data-tm-preview-frame]").Should().BeEmpty("an empty template has nothing to frame");
    }

    [Fact]
    public void Preview_RenderedStatus_IsAnnouncedOnlyInTheRenderedState()
    {
        var renderer = RegisterRenderer();
        var pending = renderer.EnqueuePending();

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()));
        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-status]"));
        cut.Find("[data-tm-preview-status]").TextContent.Should().Contain("EmailEditor_Preview_Loading");
        cut.Find("[data-tm-preview-status]").TextContent.Should().NotContain("EmailEditor_Preview_Rendered");

        pending.SetResult(Rendered("<html>x</html>"));
        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-status]").TextContent
            .Should().Contain("EmailEditor_Preview_Rendered"));
    }

    /// <summary>A renderer that always fails with the given exception.</summary>
    private sealed class ThrowingRenderer(Exception exception) : IEmailPreviewRenderer
    {
        public Task<EmailPreviewResult> RenderAsync(EmailPreviewRequest request, CancellationToken cancellationToken = default)
            => throw exception;
    }

    private static EmailPreviewResult Rendered(string html)
        => new(html, "text", "subject", null, [], EmailPreviewStatus.Rendered);

    private static EmailPreviewResult EmailPreviewResultUnavailable()
        => Abstractions.Rendering.EmailPreviewResult.Unavailable([new RenderError("offline", Key: RenderKeys.PreviewUnavailable)]);

    /// <summary>A preview renderer whose answers the test hands out one by one.</summary>
    private sealed class FakePreviewRenderer : IEmailPreviewRenderer
    {
        private readonly Queue<object> _answers = new();

        public List<EmailPreviewRequest> Requests { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public void Enqueue(EmailPreviewResult result) => _answers.Enqueue(result);

        public TaskCompletionSource<EmailPreviewResult> EnqueuePending()
        {
            var source = new TaskCompletionSource<EmailPreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _answers.Enqueue(source);
            return source;
        }

        public void EnqueueException(Exception exception) => _answers.Enqueue(exception);

        public Task<EmailPreviewResult> RenderAsync(EmailPreviewRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Tokens.Add(cancellationToken);
            if (_answers.Count == 0)
            {
                return Task.FromResult(new EmailPreviewResult(
                    "<html>default</html>", "text", "subject", null, [], EmailPreviewStatus.Rendered));
            }

            var answer = _answers.Dequeue();
            return answer switch
            {
                EmailPreviewResult result => Task.FromResult(result),
                Exception exception => Task.FromException<EmailPreviewResult>(exception),
                _ => ((TaskCompletionSource<EmailPreviewResult>)answer).Task,
            };
        }
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments] => key;
    }

    /// <summary>Echoes the key, and appends the format arguments so a formatted key is still assertable.</summary>
    private sealed class EchoEmailLocalizer : Tempo.Blazor.EmailTemplates.Localization.ITmEmailLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments]
            => arguments.Length == 0 ? key : key + "|" + string.Join("|", arguments);
    }
}
