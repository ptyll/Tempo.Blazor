using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Unit-level coverage of the evaluate-retry policy (N211) — deliberately NOT a Playwright test:
/// the pure decision functions are exercised directly so this class needs no browser and no host.
/// The scenario the spec calls out — the SECOND "Execution context was destroyed" in a call
/// sequence — used to be retried silently (<c>attempt &lt; 4</c>); it must now refuse to retry.
/// </summary>
[TestClass]
public class ContextLossRetryPolicyTests
{
    [TestMethod]
    public void FirstContextLoss_IsTheOnlyRetryableOne()
    {
        var destroyed = new PlaywrightException("Execution context was destroyed, most likely because of a navigation");

        Assert.IsTrue(DocumentEditorCanvasUxFixE2ETests.ShouldRetryContextLoss(destroyed, attempt: 0),
            "the first post-ready transient may retry once");

        // The Red scenario from N211: the SECOND loss mid-sequence used to retry silently.
        Assert.IsFalse(DocumentEditorCanvasUxFixE2ETests.ShouldRetryContextLoss(destroyed, attempt: 1),
            "a repeated context loss is a navigation/reload — old code retried it away (attempt<4)");
        Assert.IsFalse(DocumentEditorCanvasUxFixE2ETests.ShouldRetryContextLoss(destroyed, attempt: 3),
            "no later attempt may retry either");
    }

    [TestMethod]
    public void NavigationMessage_CountsAsContextLoss()
    {
        var nav = new PlaywrightException("Protocol error: because of a navigation");
        Assert.IsTrue(DocumentEditorCanvasUxFixE2ETests.IsTransientContextLoss(nav));
        Assert.IsTrue(DocumentEditorCanvasUxFixE2ETests.ShouldRetryContextLoss(nav, 0));
        Assert.IsFalse(DocumentEditorCanvasUxFixE2ETests.ShouldRetryContextLoss(nav, 1));
    }

    [TestMethod]
    public void UnrelatedErrors_AreNeverRetried()
    {
        var other = new PlaywrightException("Node is not visible");
        Assert.IsFalse(DocumentEditorCanvasUxFixE2ETests.IsTransientContextLoss(other));
        Assert.IsFalse(DocumentEditorCanvasUxFixE2ETests.ShouldRetryContextLoss(other, 0));
    }

    [TestMethod]
    public void FailureMessage_CarriesAttemptAndNavigationCount()
    {
        string message = DocumentEditorCanvasUxFixE2ETests.ContextLossFailureMessage(attempt: 1, frameNavigationsSinceReady: 2);
        StringAssert.Contains(message, "2 frame navigation(s)",
            "the failure must say how many navigations happened since ready — the diagnostic that makes a masked reload visible");
        StringAssert.Contains(message, "retry #1");
    }
}
