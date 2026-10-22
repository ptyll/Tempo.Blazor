using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>Tests for TmCodeEditor.</summary>
public class TmCodeEditorTests : LocalizationTestBase
{
    [Fact]
    public void TmCodeEditor_Renders_Textarea_And_Overlay()
    {
        var cut = Render<TmCodeEditor>();
        cut.Find("textarea").ClassList.Should().Contain("tm-code-editor__textarea");
        cut.Find("pre.tm-code-editor__highlight").Should().NotBeNull();
    }

    [Fact]
    public void TmCodeEditor_Root_Has_Default_TestId()
    {
        var cut = Render<TmCodeEditor>();
        cut.Find("[data-testid='code-editor']").Should().NotBeNull();
    }

    [Fact]
    public void TmCodeEditor_DataTestId_Overrides_Root_TestId()
    {
        var cut = Render<TmCodeEditor>(p => p.Add(c => c.DataTestId, "job-attributes"));
        cut.Find("[data-testid='job-attributes']").Should().NotBeNull();
    }

    [Fact]
    public void TmCodeEditor_Default_Rows_Is_8()
    {
        var cut = Render<TmCodeEditor>();
        cut.Find("textarea").GetAttribute("rows").Should().Be("8");
    }

    [Fact]
    public void TmCodeEditor_Label_Renders_Label_Element()
    {
        var cut = Render<TmCodeEditor>(p => p.Add(c => c.Label, "Attributes"));
        cut.Find("label").TextContent.Trim().Should().Be("Attributes");
    }

    [Fact]
    public void TmCodeEditor_Placeholder_Applied()
    {
        var cut = Render<TmCodeEditor>(p => p.Add(c => c.Placeholder, "{ }"));
        cut.Find("textarea").GetAttribute("placeholder").Should().Be("{ }");
    }

    [Fact]
    public void TmCodeEditor_Disabled_Sets_Disabled_Attribute()
    {
        var cut = Render<TmCodeEditor>(p => p.Add(c => c.Disabled, true));
        cut.Find("textarea").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmCodeEditor_ReadOnly_Sets_Readonly_Attribute()
    {
        var cut = Render<TmCodeEditor>(p => p.Add(c => c.ReadOnly, true));
        cut.Find("textarea").HasAttribute("readonly").Should().BeTrue();
    }

    [Fact]
    public void TmCodeEditor_Language_Sets_Highlight_Language_Class()
    {
        var cut = Render<TmCodeEditor>(p => p.Add(c => c.Language, "json"));
        cut.Find("pre.tm-code-editor__highlight code").ClassList.Should().Contain("language-json");
    }

    [Fact]
    public void TmCodeEditor_ValueChanged_Fires_On_Input()
    {
        string? captured = null;
        var cut = Render<TmCodeEditor>(p => p
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => captured = v)));

        cut.Find("textarea").Input("{\"a\":1}");

        captured.Should().Be("{\"a\":1}");
    }

    [Fact]
    public void TmCodeEditor_CopyButton_Rendered_By_Default()
    {
        var cut = Render<TmCodeEditor>();
        cut.FindAll(".tm-code-editor__copy").Should().NotBeEmpty();
    }

    [Fact]
    public void TmCodeEditor_CopyButton_Hidden_When_Disabled_By_Parameter()
    {
        var cut = Render<TmCodeEditor>(p => p.Add(c => c.ShowCopyButton, false));
        cut.FindAll(".tm-code-editor__copy").Should().BeEmpty();
    }

    // ── JSON validation ──────────────────────────────────────────

    [Fact]
    public void TmCodeEditor_ValidateJson_Shows_Error_For_Invalid_Json()
    {
        var cut = Render<TmCodeEditor>(p => p
            .Add(c => c.Language, "json")
            .Add(c => c.ValidateJson, true));

        cut.Find("textarea").Input("{not json");

        cut.Find("[data-testid='error-message']").TextContent.Should().Contain("Invalid JSON");
        cut.Instance.IsValid.Should().BeFalse();
        cut.Find("textarea").GetAttribute("aria-invalid").Should().Be("true");
    }

    [Fact]
    public void TmCodeEditor_ValidateJson_No_Error_For_Valid_Json()
    {
        var cut = Render<TmCodeEditor>(p => p
            .Add(c => c.Language, "json")
            .Add(c => c.ValidateJson, true));

        cut.Find("textarea").Input("{\"a\": [1, 2, 3]}");

        cut.FindAll("[data-testid='error-message']").Should().BeEmpty();
        cut.Instance.IsValid.Should().BeTrue();
    }

    [Fact]
    public void TmCodeEditor_ValidateJson_Ignores_Empty_Value()
    {
        var cut = Render<TmCodeEditor>(p => p
            .Add(c => c.Language, "json")
            .Add(c => c.ValidateJson, true));

        cut.FindAll("[data-testid='error-message']").Should().BeEmpty();
        cut.Instance.IsValid.Should().BeTrue();
    }

    [Fact]
    public void TmCodeEditor_ValidateJson_Inactive_For_Other_Languages()
    {
        var cut = Render<TmCodeEditor>(p => p
            .Add(c => c.Language, "csharp")
            .Add(c => c.ValidateJson, true));

        cut.Find("textarea").Input("{not json");

        cut.FindAll("[data-testid='error-message']").Should().BeEmpty();
    }

    [Fact]
    public void TmCodeEditor_External_Error_Takes_Precedence_Over_Json_Error()
    {
        var cut = Render<TmCodeEditor>(p => p
            .Add(c => c.Language, "json")
            .Add(c => c.ValidateJson, true)
            .Add(c => c.Error, "Server rejected the value"));

        cut.Find("textarea").Input("{not json");

        cut.Find("[data-testid='error-message']").TextContent.Should().Be("Server rejected the value");
    }

    // ── Format button ────────────────────────────────────────────

    [Fact]
    public void TmCodeEditor_FormatButton_Rendered_Only_For_Json()
    {
        var json = Render<TmCodeEditor>(p => p
            .Add(c => c.Language, "json")
            .Add(c => c.ShowFormatButton, true));
        json.FindAll("[data-testid='format-button']").Should().NotBeEmpty();

        var csharp = Render<TmCodeEditor>(p => p
            .Add(c => c.Language, "csharp")
            .Add(c => c.ShowFormatButton, true));
        csharp.FindAll("[data-testid='format-button']").Should().BeEmpty();
    }

    [Fact]
    public async Task TmCodeEditor_FormatJsonAsync_PrettyPrints_Value()
    {
        string? captured = null;
        var cut = Render<TmCodeEditor>(p => p
            .Add(c => c.Value, "{\"a\":1,\"b\":[true,null]}")
            .Add(c => c.Language, "json")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => captured = v)));

        await cut.InvokeAsync(() => cut.Instance.FormatJsonAsync());

        captured.Should().Contain("\n");
        captured.Should().Contain("\"a\": 1");
    }

    [Fact]
    public async Task TmCodeEditor_FormatJsonAsync_NoOp_For_Invalid_Json()
    {
        string? captured = null;
        var cut = Render<TmCodeEditor>(p => p
            .Add(c => c.Value, "{broken")
            .Add(c => c.Language, "json")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => captured = v)));

        await cut.InvokeAsync(() => cut.Instance.FormatJsonAsync());

        captured.Should().BeNull();
    }

    // ── Tab trapping and wrapping ────────────────────────────────

    [Fact]
    public void TmCodeEditor_TrapTab_Defaults_To_True()
    {
        // Existing hosts rely on Tab indenting the code — the upgrade must not change that.
        var cut = Render<TmCodeEditor>();
        cut.Instance.TrapTab.Should().BeTrue();
    }

    [Fact]
    public void TmCodeEditor_Wrap_Defaults_To_False()
    {
        var cut = Render<TmCodeEditor>();
        cut.Instance.Wrap.Should().BeFalse();
        cut.Find("[data-testid='code-editor']").ClassList.Should().NotContain("tm-code-editor--wrap");
    }

    [Fact]
    public void TmCodeEditor_Wrap_Adds_Modifier_Class()
    {
        // The modifier flips white-space on the textarea AND the overlay at once — they must keep
        // identical metrics, otherwise the highlight drifts against the caret.
        var cut = Render<TmCodeEditor>(p => p.Add(c => c.Wrap, true));
        cut.Find("[data-testid='code-editor']").ClassList.Should().Contain("tm-code-editor--wrap");
    }

    [Fact]
    public void TmCodeEditor_Wrap_KeepsCustomClass()
    {
        var cut = Render<TmCodeEditor>(p => p
            .Add(c => c.Wrap, true)
            .Add(c => c.Class, "host-editor"));

        var classes = cut.Find("[data-testid='code-editor']").ClassList;
        classes.Should().Contain("tm-code-editor--wrap");
        classes.Should().Contain("host-editor");
    }

    // ── TestIdPrefix ─────────────────────────────────────────────

    [Fact]
    public void TmCodeEditor_TestIdPrefix_Namespaces_Internal_TestIds()
    {
        var cut = Render<TmCodeEditor>(p => p
            .Add(c => c.TestIdPrefix, "job-attrs")
            .Add(c => c.Language, "json")
            .Add(c => c.ValidateJson, true));

        cut.Find("[data-testid='job-attrs-code-editor']").Should().NotBeNull();

        cut.Find("textarea").Input("{invalid");
        cut.Find("[data-testid='job-attrs-error-message']").Should().NotBeNull();
    }

    [Fact]
    public void TmCodeEditor_ForwardsTheCopyConfirmation_ToTheHost()
    {
        // Fáze 10 review (UX 8): the code editor owns the copy button, so the dialog cannot hang its own
        // handler on the surrounding box — the confirmation has to travel through the component.
        JSInterop.SetupVoid("navigator.clipboard.writeText", "abc").SetVoidResult();
        var invocations = 0;

        var cut = Render<TmCodeEditor>(p => p
            .Add(c => c.Value, "abc")
            .Add(c => c.CopyButtonCopied, () => invocations++));

        cut.Find(".tm-copy-button").Click();

        invocations.Should().Be(1);
    }
}
