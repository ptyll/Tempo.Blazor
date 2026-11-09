using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 5 review finding 1: the <c>?bpmn=</c> deep link must auto-import a corpus sample on the
/// SERVER host too. The Server/InteractiveAuto demos point their default <c>HttpClient</c> at the
/// Demo API, so the sample fetch needs an absolute URL (NavigationManager.ToAbsoluteUri) that
/// reaches the serving host's static web assets — this test proves the wiring end to end.
/// </summary>
[TestClass]
[TestCategory("Server")]
public sealed class ModelingBpmnServerE2ETests : ServerTestBase
{
    [TestMethod]
    [Description("The ?bpmn= deep link auto-imports a corpus sample on the Server host")]
    public async Task BpmnDeepLink_ServerHost_ImportsAutomatically()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/modeling-editor?bpmn=simple-process",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var status = page.Locator("[data-testid='modeling-demo-bpmn-status']");
        await Assertions.Expect(status).ToContainTextAsync("simple-process.bpmn",
            new LocatorAssertionsToContainTextOptions { Timeout = 20000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll(\"[data-testid='modeling-diagram-preview'] g.tm-diagram-node\").length > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 20000 });
        await Assertions.Expect(page.Locator("#blazor-error-ui"))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
    }
}
