using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Forms;

/// <summary>TDD tests for TmDynamicFormRenderer.</summary>
public class TmDynamicFormRendererTests : LocalizationTestBase
{
    [Fact]
    public void DynamicForm_RendersTextField()
    {
        var fields = new List<DynamicFieldDefinition>
        {
            new() { Name = "name", FieldType = DynamicFieldType.Text, Label = "Full Name" }
        };

        var cut = RenderComponent<TmDynamicFormRenderer>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Values, new Dictionary<string, object?>()));

        cut.Find("input[type='text']").Should().NotBeNull();
        cut.Markup.Should().Contain("Full Name");
    }

    [Fact]
    public void DynamicForm_RendersTextArea()
    {
        var fields = new List<DynamicFieldDefinition>
        {
            new() { Name = "notes", FieldType = DynamicFieldType.TextArea, Label = "Notes" }
        };

        var cut = RenderComponent<TmDynamicFormRenderer>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Values, new Dictionary<string, object?>()));

        cut.Find("textarea").Should().NotBeNull();
    }

    [Fact]
    public void DynamicForm_RendersNumberField()
    {
        var fields = new List<DynamicFieldDefinition>
        {
            new() { Name = "age", FieldType = DynamicFieldType.Number, Label = "Age" }
        };

        var cut = RenderComponent<TmDynamicFormRenderer>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Values, new Dictionary<string, object?>()));

        cut.Find("input[type='number']").Should().NotBeNull();
    }

    [Fact]
    public void DynamicForm_RendersCheckbox()
    {
        var fields = new List<DynamicFieldDefinition>
        {
            new() { Name = "agree", FieldType = DynamicFieldType.Checkbox, Label = "I agree" }
        };

        var cut = RenderComponent<TmDynamicFormRenderer>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Values, new Dictionary<string, object?>()));

        cut.Find("input[type='checkbox']").Should().NotBeNull();
    }

    [Fact]
    public void DynamicForm_RendersSelect()
    {
        var fields = new List<DynamicFieldDefinition>
        {
            new()
            {
                Name = "status", FieldType = DynamicFieldType.Select, Label = "Status",
                Options = new List<SelectOption<string>>
                {
                    new("active", "Active"),
                    new("inactive", "Inactive"),
                }
            }
        };

        var cut = RenderComponent<TmDynamicFormRenderer>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Values, new Dictionary<string, object?>()));

        cut.Find("select").Should().NotBeNull();
    }

    [Fact]
    public void DynamicForm_RequiredField_ShowsStar()
    {
        var fields = new List<DynamicFieldDefinition>
        {
            new() { Name = "name", FieldType = DynamicFieldType.Text, Label = "Name", Required = true }
        };

        var cut = RenderComponent<TmDynamicFormRenderer>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Values, new Dictionary<string, object?>()));

        cut.Markup.Should().Contain("*");
    }

    [Fact]
    public void DynamicForm_DisabledField_IsDisabled()
    {
        var fields = new List<DynamicFieldDefinition>
        {
            new() { Name = "name", FieldType = DynamicFieldType.Text, Label = "Name", Disabled = true }
        };

        var cut = RenderComponent<TmDynamicFormRenderer>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Values, new Dictionary<string, object?>()));

        cut.Find("input").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void DynamicForm_DefaultValue_IsDisplayed()
    {
        var fields = new List<DynamicFieldDefinition>
        {
            new() { Name = "name", FieldType = DynamicFieldType.Text, Label = "Name", DefaultValue = "John" }
        };

        var cut = RenderComponent<TmDynamicFormRenderer>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Values, new Dictionary<string, object?>()));

        cut.Find("input").GetAttribute("value").Should().Be("John");
    }

    [Fact]
    public void DynamicForm_MultiColumn_HasGridClass()
    {
        var fields = new List<DynamicFieldDefinition>
        {
            new() { Name = "first", FieldType = DynamicFieldType.Text, Label = "First" },
            new() { Name = "last", FieldType = DynamicFieldType.Text, Label = "Last" }
        };

        var cut = RenderComponent<TmDynamicFormRenderer>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Columns, 2)
            .Add(x => x.Values, new Dictionary<string, object?>()));

        cut.Find(".tm-dynamic-form").ClassList.Should().Contain("tm-dynamic-form--2col");
    }

    [Fact]
    public void DynamicForm_ReadOnly_DisablesAllFields()
    {
        var fields = new List<DynamicFieldDefinition>
        {
            new() { Name = "name", FieldType = DynamicFieldType.Text, Label = "Name" },
            new() { Name = "notes", FieldType = DynamicFieldType.TextArea, Label = "Notes" }
        };

        var cut = RenderComponent<TmDynamicFormRenderer>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.ReadOnly, true)
            .Add(x => x.Values, new Dictionary<string, object?>()));

        var inputs = cut.FindAll("input, textarea");
        foreach (var input in inputs)
        {
            input.HasAttribute("disabled").Should().BeTrue();
        }
    }

    [Fact]
    public void DynamicForm_HelpText_Renders()
    {
        var fields = new List<DynamicFieldDefinition>
        {
            new() { Name = "email", FieldType = DynamicFieldType.Text, Label = "Email", HelpText = "Enter valid email" }
        };

        var cut = RenderComponent<TmDynamicFormRenderer>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Values, new Dictionary<string, object?>()));

        cut.Markup.Should().Contain("Enter valid email");
    }

    [Fact]
    public void DynamicForm_ValueChange_FiresEvent()
    {
        (string FieldName, object? Value)? changed = null;
        var fields = new List<DynamicFieldDefinition>
        {
            new() { Name = "name", FieldType = DynamicFieldType.Text, Label = "Name" }
        };

        var cut = RenderComponent<TmDynamicFormRenderer>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Values, new Dictionary<string, object?>())
            .Add(x => x.OnFieldChanged, EventCallback.Factory.Create<(string, object?)>(this, v => changed = v)));

        cut.Find("input").Change("Alice");

        changed.Should().NotBeNull();
        changed!.Value.FieldName.Should().Be("name");
        changed!.Value.Value.Should().Be("Alice");
    }
}
