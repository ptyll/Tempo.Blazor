using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public class TmDocumentJsonDebugModalTests : LocalizationTestBase
{
    // ── 15.1 – Modal rendering ────────────────────────────────────────────

    [Fact]
    public void Modal_WhenClosed_RendersNothing()
    {
        var cut = Render<TmDocumentJsonDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, false)
            .Add(p => p.Json, "{}"));

        cut.FindAll("[data-testid='document-json-debug-modal']").Should().BeEmpty();
    }

    [Fact]
    public void Modal_WhenOpen_RendersWithTestId()
    {
        var cut = Render<TmDocumentJsonDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Json, "{}"));

        cut.Find("[data-testid='document-json-debug-modal']").Should().NotBeNull();
    }

    [Fact]
    public void Modal_ShowsJsonContent()
    {
        var json = """{"blocks":[]}""";
        var cut = Render<TmDocumentJsonDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Json, json));

        // The block is shown indented (readable), the content is the same JSON.
        System.Text.RegularExpressions.Regex.Replace(cut.Find("[data-testid='document-json-debug-content']").TextContent, @"\s+", string.Empty)
            .Should().Contain(json);
    }

    [Fact]
    public void Modal_CloseButton_InvokesOnClose()
    {
        var closed = false;
        var cut = Render<TmDocumentJsonDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Json, "{}")
            .Add(p => p.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        cut.Find("[data-testid='document-json-debug-close']").Click();

        closed.Should().BeTrue();
    }

    [Fact]
    public void Modal_NullJson_RendersEmpty()
    {
        var cut = Render<TmDocumentJsonDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Json, null));

        cut.Find("[data-testid='document-json-debug-content']").TextContent.Trim().Should().BeEmpty();
    }

    [Fact]
    public void Modal_RecoveryDetail_ShowsRuntimeRecoveryDebugSection()
    {
        var cut = Render<TmDocumentJsonDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Json, "{}")
            .Add(p => p.RecoveryDetailJson, """{"source":"command","attempt":1}"""));

        cut.Find("[data-testid='document-runtime-recovery-debug']").Should().NotBeNull();
        cut.Find("[data-testid='document-runtime-recovery-debug-content']").TextContent
            .Should().Contain("command");
    }

    [Fact]
    public void Modal_RuntimeDebug_ShowsRuntimeDebugSection()
    {
        var cut = Render<TmDocumentJsonDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Json, "{}")
            .Add(p => p.RuntimeDebugJson, """{"HasRuntimeDocument":true}"""));

        cut.Find("[data-testid='document-runtime-debug']").Should().NotBeNull();
        cut.Find("[data-testid='document-runtime-debug-content']").TextContent
            .Should().Contain("HasRuntimeDocument");
    }

    [Fact]
    public void Modal_CopyButton_InvokesOnCopyJson()
    {
        var copied = false;
        var cut = Render<TmDocumentJsonDebugModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Json, "{}")
            .Add(p => p.OnCopyJson, EventCallback.Factory.Create(this, () => copied = true)));

        cut.Find("[data-testid='document-json-debug-copy']").Click();

        copied.Should().BeTrue();
    }
}
