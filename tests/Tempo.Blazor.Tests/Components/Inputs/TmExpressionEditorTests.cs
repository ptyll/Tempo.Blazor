using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>TDD tests for TmExpressionEditor.</summary>
public class TmExpressionEditorTests : LocalizationTestBase
{
    private static readonly IReadOnlyList<ExpressionVariable> TestVars =
    [
        new() { Name = "sender.name", Description = "Name of the sender", Type = "string" },
        new() { Name = "sender.email", Description = "Email of the sender", Type = "string" },
        new() { Name = "message.subject", Description = "Message subject", Type = "string" },
        new() { Name = "message.date", Description = "Date received", Type = "DateTime" },
    ];

    [Fact]
    public void ExpressionEditor_Renders_Textarea()
    {
        var cut = RenderComponent<TmExpressionEditor>(p => p
            .Add(x => x.Variables, TestVars));

        cut.Find(".tm-expression-editor").Should().NotBeNull();
        cut.Find("textarea").Should().NotBeNull();
    }

    [Fact]
    public void ExpressionEditor_Value_BoundToTextarea()
    {
        var cut = RenderComponent<TmExpressionEditor>(p => p
            .Add(x => x.Value, "Hello {{sender.name}}")
            .Add(x => x.Variables, TestVars));

        cut.Find("textarea").GetAttribute("value").Should().Be("Hello {{sender.name}}");
    }

    [Fact]
    public void ExpressionEditor_Placeholder_Applied()
    {
        var cut = RenderComponent<TmExpressionEditor>(p => p
            .Add(x => x.Variables, TestVars)
            .Add(x => x.Placeholder, "Type expression..."));

        cut.Find("textarea").GetAttribute("placeholder").Should().Be("Type expression...");
    }

    [Fact]
    public void ExpressionEditor_Label_Rendered()
    {
        var cut = RenderComponent<TmExpressionEditor>(p => p
            .Add(x => x.Variables, TestVars)
            .Add(x => x.Label, "Subject Template"));

        cut.Markup.Should().Contain("Subject Template");
    }

    [Fact]
    public void ExpressionEditor_Disabled_State()
    {
        var cut = RenderComponent<TmExpressionEditor>(p => p
            .Add(x => x.Variables, TestVars)
            .Add(x => x.Disabled, true));

        cut.Find("textarea").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void ExpressionEditor_Error_ShowsMessage()
    {
        var cut = RenderComponent<TmExpressionEditor>(p => p
            .Add(x => x.Variables, TestVars)
            .Add(x => x.Error, "Invalid expression"));

        cut.Find(".tm-expression-editor__error").TextContent.Should().Contain("Invalid expression");
        cut.Find(".tm-expression-editor").ClassList.Should().Contain("tm-expression-editor--error");
    }

    [Fact]
    public void ExpressionEditor_VariablePanel_ShowsVariables()
    {
        var cut = RenderComponent<TmExpressionEditor>(p => p
            .Add(x => x.Variables, TestVars));

        cut.FindAll(".tm-expression-editor__var").Count.Should().Be(4);
        cut.Markup.Should().Contain("sender.name");
        cut.Markup.Should().Contain("sender.email");
    }

    [Fact]
    public void ExpressionEditor_ClickVariable_InsertsIntoValue()
    {
        string? newValue = null;
        var cut = RenderComponent<TmExpressionEditor>(p => p
            .Add(x => x.Value, "Hello ")
            .Add(x => x.Variables, TestVars)
            .Add(x => x.ValueChanged, v => newValue = v));

        // Click the first variable
        cut.FindAll(".tm-expression-editor__var")[0].Click();

        newValue.Should().NotBeNull();
        newValue.Should().Contain("{{sender.name}}");
    }

    [Fact]
    public void ExpressionEditor_VariableDescription_Shown()
    {
        var cut = RenderComponent<TmExpressionEditor>(p => p
            .Add(x => x.Variables, TestVars));

        // Each variable should show its description somewhere
        cut.Markup.Should().Contain("Name of the sender");
    }

    [Fact]
    public void ExpressionEditor_CustomClass()
    {
        var cut = RenderComponent<TmExpressionEditor>(p => p
            .Add(x => x.Variables, TestVars)
            .Add(x => x.Class, "my-editor"));

        cut.Find(".tm-expression-editor").ClassList.Should().Contain("my-editor");
    }
}
