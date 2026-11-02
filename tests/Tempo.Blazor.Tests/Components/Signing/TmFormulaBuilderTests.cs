using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// Formula builder (Signing redesign phase 7, S1-2): operator/function/token groups carry visible
/// headings with <c>role="group"</c> + <c>aria-labelledby</c>, the operators form a grid that
/// includes parentheses with localized spoken names (Q-S8 — the grammar itself stays), token cards
/// show the field type, insertion happens at the textarea cursor through the neutral
/// <c>signing-editor.js</c> module (with an append fallback when no JS runtime exists), and Save is
/// the primary action.
/// </summary>
public class TmFormulaBuilderTests : LocalizationTestBase
{
    [Fact]
    public void Formula_GroupsHaveRoleGroupAriaLabelledBy()
    {
        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, CreateFields()));

        var groups = cut.FindAll("[role='group'][aria-labelledby]");
        groups.Count.Should().BeGreaterThanOrEqualTo(3, "operators, functions and tokens are labelled groups");

        foreach (var group in groups)
        {
            var headingId = group.GetAttribute("aria-labelledby");
            cut.Find($"#{headingId}").TextContent.Should().NotBeEmpty(
                "every group points at a VISIBLE heading element");
        }
    }

    [Fact]
    public void Formula_Parentheses_Available()
    {
        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, CreateFields()));

        cut.Find("[data-operator='(']").Should().NotBeNull("parentheses are part of the operator grid");
        cut.Find("[data-operator=')']").Should().NotBeNull();
    }

    /// <summary>The operator glyphs are ambiguous to a screen reader ("+", "*"); each button names
    /// itself with the localized spoken name (phase 7 review).</summary>
    [Theory]
    [InlineData("+", "Plus")]
    [InlineData("-", "Minus")]
    [InlineData("*", "Multiply")]
    [InlineData("/", "Divide")]
    [InlineData("(", "Left parenthesis")]
    [InlineData(")", "Right parenthesis")]
    public void OperatorButtons_CarryLocalizedAriaLabels(string op, string expectedLabel)
    {
        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, CreateFields()));

        cut.Find($"[data-operator='{op}']").GetAttribute("aria-label").Should().Be(expectedLabel);
    }

    [Fact]
    public void Formula_TokensShowFieldType()
    {
        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, CreateFields()));

        var token = cut.Find("[data-field-uuid='subtotal']");
        token.QuerySelector(".tm-formula-builder__token-label")!.TextContent.Should().Contain("Subtotal");
        token.QuerySelector(".tm-formula-builder__token-type")!.TextContent.Should().NotBeEmpty(
            "every token card names its field type");
    }

    [Fact]
    public void Formula_InsertsAtCursor()
    {
        // The JS module answers with the formula text reassembled around the simulated cursor.
        JSInterop.SetupModule("./_content/Tempo.Blazor.Signing/js/signing-editor.js")
            .Setup<string>("insertAtCursor", _ => true)
            .SetResult("AB{{Subtotal}}CD");

        string? captured = null;
        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.ValueChanged, EventCallback.Factory.Create<string>(this, value => captured = value)));

        cut.Find("[data-field-uuid='subtotal']").Click();

        captured.Should().Be("AB{{Subtotal}}CD", "the module's cursor-aware result replaces the plain append");
        JSInterop.VerifyInvoke("import");
    }

    [Fact]
    public void Formula_InsertWithoutJs_FallsBackToAppend()
    {
        string? captured = null;
        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.ValueChanged, EventCallback.Factory.Create<string>(this, value => captured = value)));

        cut.Find("[data-field-uuid='subtotal']").Click();

        // No JS runtime (bUnit default): the insertion degrades to appending the token.
        captured.Should().Be("{{Subtotal}}");
    }

    [Fact]
    public void Render_RendersTextarea()
    {
        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, CreateFields()));

        cut.Find(".tm-formula-builder__textarea textarea").Should().NotBeNull(
            "the formula editor is the core TmTextArea");
    }

    [Fact]
    public void Render_RendersTokenButtonsForNumberFields()
    {
        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, CreateFields()));

        cut.FindAll(".tm-formula-builder__token .tm-formula-builder__token-label")
            .Select(label => label.TextContent.Trim())
            .Should()
            .Contain(["Subtotal", "Tax"]);
    }

    [Fact]
    public void Render_RendersNumericSelectAndRadioFieldsWhenOptionsAreNumeric()
    {
        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, CreateFields()));

        cut.FindAll(".tm-formula-builder__token [data-field-uuid], .tm-formula-builder__token[data-field-uuid]")
            .Select(button => button.GetAttribute("data-field-uuid"))
            .Should()
            .Contain(["plan", "multiplier"]);
    }

    [Fact]
    public void Render_DoesNotRenderCurrentFieldToken()
    {
        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, CreateFields()));

        cut.FindAll(".tm-formula-builder__token[data-field-uuid]")
            .Select(button => button.GetAttribute("data-field-uuid"))
            .Should()
            .NotContain("total");
    }

    [Fact]
    public void Render_DoesNotRenderFieldThatWouldCreateCycle()
    {
        var fields = CreateFields();
        fields.First(field => field.Uuid == "dependent").Preferences.Formula = "{{total}}";

        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, fields));

        cut.FindAll(".tm-formula-builder__token[data-field-uuid]")
            .Select(button => button.GetAttribute("data-field-uuid"))
            .Should()
            .NotContain("dependent");
    }

    [Fact]
    public void ClickTokenButton_InsertsToken()
    {
        string? captured = null;
        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.ValueChanged, EventCallback.Factory.Create<string>(this, value => captured = value)));

        cut.Find("[data-field-uuid='subtotal']").Click();

        captured.Should().Contain("{{Subtotal}}");
        cut.Find(".tm-formula-builder__textarea textarea").TextContent.Should().Contain("{{Subtotal}}");
    }

    [Fact]
    public void Render_TokenPickerUsesLocalizedFieldLabel()
    {
        var fields = CreateFields();
        fields.First(field => field.Uuid == "subtotal").Labels = new SigningLocalizedText
        {
            Default = "Mezisoučet",
            Translations = { ["en-US"] = "Subtotal localized" }
        };

        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, fields)
                      .Add(p => p.Culture, "en-US")
                      .Add(p => p.FallbackCulture, "cs-CZ"));

        cut.Find("[data-field-uuid='subtotal'] .tm-formula-builder__token-label").TextContent.Trim()
            .Should().Be("Subtotal localized");
    }

    [Fact]
    public void ClickTokenButton_WithLocalizedLabelStillInsertsHumanizedFieldToken()
    {
        string? captured = null;
        var fields = CreateFields();
        fields.First(field => field.Uuid == "subtotal").Labels = new SigningLocalizedText
        {
            Default = "Mezisoučet",
            Translations = { ["en-US"] = "Subtotal localized" }
        };

        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, fields)
                      .Add(p => p.Culture, "en-US")
                      .Add(p => p.ValueChanged, EventCallback.Factory.Create<string>(this, value => captured = value)));

        cut.Find("[data-field-uuid='subtotal']").Click();

        captured.Should().Be("{{Subtotal}}");
    }

    [Theory]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("*")]
    [InlineData("/")]
    public void OperatorButton_InsertsOperator(string op)
    {
        string? captured = null;
        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.ValueChanged, EventCallback.Factory.Create<string>(this, value => captured = value)));

        cut.Find($"[data-operator='{op}']").Click();

        captured.Should().Contain(op);
    }

    [Theory]
    [InlineData("round(n, d)")]
    [InlineData("abs(n)")]
    public void FunctionButton_InsertsFunction(string function)
    {
        string? captured = null;
        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.ValueChanged, EventCallback.Factory.Create<string>(this, value => captured = value)));

        cut.Find($"[data-function='{function}']").Click();

        captured.Should().Contain(function);
    }

    [Fact]
    public void Save_WithUnknownToken_ShowsValidationAndDoesNotSave()
    {
        var saved = false;
        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.Value, "{{Missing}} + 1")
                      .Add(p => p.Saved, EventCallback.Factory.Create<string>(this, _ => saved = true)));

        cut.Find(".tm-formula-builder__save").Click();

        saved.Should().BeFalse();
        cut.Find(".tm-formula-builder__textarea .tm-input-error-message").TextContent.Should().Contain("Missing");
    }

    [Fact]
    public void Save_WithValidFormula_SetsFieldReadonlyAndSavesNormalizedFormula()
    {
        string? saved = null;
        var field = CreateCurrentField();
        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, field)
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.Value, "{{Subtotal}} + {{Tax}}")
                      .Add(p => p.Saved, EventCallback.Factory.Create<string>(this, value => saved = value)));

        cut.Find(".tm-formula-builder__save").Click();

        saved.Should().Be("{{subtotal}} + {{tax}}");
        field.ReadOnly.Should().BeTrue();
        field.Preferences.Formula.Should().Be("{{subtotal}} + {{tax}}");
    }

    [Fact]
    public void CultureChange_DoesNotChangeExistingFormulaToken()
    {
        var fields = CreateFields();
        fields.First(field => field.Uuid == "subtotal").Labels.Translations["cs"] = "Mezisoučet";

        var cut = Render<TmFormulaBuilder>(parameters =>
            parameters.Add(p => p.Field, CreateCurrentField())
                      .Add(p => p.Fields, fields)
                      .Add(p => p.Value, "{{subtotal}}")
                      .Add(p => p.Culture, "en-US"));

        cut.Render(parameters => parameters.Add(p => p.Culture, "cs-CZ"));

        cut.Find(".tm-formula-builder__textarea textarea").TextContent.Should().Be("{{Subtotal}}");
        cut.Find("[data-field-uuid='subtotal'] .tm-formula-builder__token-label").TextContent
            .Should().Contain("Mezisoučet");
    }

    private static SigningField CreateCurrentField()
    {
        return CreateField("total", "Total", SigningFieldType.Number);
    }

    private static List<SigningField> CreateFields()
    {
        return
        [
            CreateField("total", "Total", SigningFieldType.Number),
            CreateField("subtotal", "Subtotal", SigningFieldType.Number),
            CreateField("tax", "Tax", SigningFieldType.Number),
            CreateField("dependent", "Dependent", SigningFieldType.Number),
            CreateChoiceField("plan", "Plan price", SigningFieldType.Select, "10", "20"),
            CreateChoiceField("multiplier", "Multiplier", SigningFieldType.Radio, "1", "2"),
            CreateChoiceField("region", "Region", SigningFieldType.Select, "EU", "US"),
            CreateField("notes", "Notes", SigningFieldType.Text)
        ];
    }

    private static SigningField CreateField(string uuid, string name, SigningFieldType type)
    {
        return new SigningField
        {
            Uuid = uuid,
            Name = name,
            Type = type
        };
    }

    private static SigningField CreateChoiceField(string uuid, string name, SigningFieldType type, params string[] values)
    {
        var field = CreateField(uuid, name, type);
        field.Options = values
            .Select(value => new SigningFieldOption { Uuid = value, Value = value })
            .ToList();

        return field;
    }
}
