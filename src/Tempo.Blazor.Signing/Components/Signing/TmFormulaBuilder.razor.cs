using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Builds computed signing field formulas from numeric field tokens and operators.</summary>
public partial class TmFormulaBuilder : IAsyncDisposable
{
    private static readonly string[] Operators = ["+", "-", "*", "/", "(", ")"];
    private static readonly string[] Functions = ["round(n, d)", "abs(n)"];

    private readonly string _textareaId = $"tm-formula-{Guid.NewGuid():N}";
    private readonly string _operatorsHeadingId = $"tm-formula-operators-{Guid.NewGuid():N}";
    private readonly string _functionsHeadingId = $"tm-formula-functions-{Guid.NewGuid():N}";
    private readonly string _tokensHeadingId = $"tm-formula-tokens-{Guid.NewGuid():N}";
    private ElementReference _textareaHost;
    private SigningEditorInterop? _interop;
    private string _formulaText = string.Empty;
    private string? _lastSourceFormula;
    private string? _error;

    /// <summary>All fields in the current signing template.</summary>
    [Parameter] public IReadOnlyList<SigningField> Fields { get; set; } = [];

    /// <summary>Computed field edited by the formula builder.</summary>
    [Parameter] public SigningField? Field { get; set; }

    /// <summary>Field identifier edited by the formula builder when <see cref="Field"/> is not supplied.</summary>
    [Parameter] public string? CurrentFieldUuid { get; set; }

    /// <summary>Formula text displayed by the builder. Human-readable field tokens are accepted.</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>Callback invoked when displayed formula text changes.</summary>
    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    /// <summary>Callback invoked after a valid formula is saved. The supplied value is normalized to UUID tokens.</summary>
    [Parameter] public EventCallback<string> Saved { get; set; }

    /// <summary>Callback invoked after the edited field is updated on save.</summary>
    [Parameter] public EventCallback<SigningField> FieldChanged { get; set; }

    /// <summary>Whether the builder controls are disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Culture used to resolve field labels in the token picker.</summary>
    [Parameter] public string? Culture { get; set; }

    /// <summary>Fallback culture used when a field label is missing for <see cref="Culture"/>.</summary>
    [Parameter] public string? FallbackCulture { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    private string? EffectiveCurrentFieldUuid => Field?.Uuid ?? CurrentFieldUuid;

    private IEnumerable<SigningField> FormulaSourceFields => Fields
        .Where(IsFormulaSourceField)
        .Where(sourceField => !WouldCreateCycle(sourceField));

    private string RootClass
    {
        get
        {
            var classes = new List<string> { "tm-formula-builder" };
            AddClass(classes, Disabled, "tm-formula-builder--disabled");
            AddClass(classes, _error is not null, "tm-formula-builder--invalid");

            if (!string.IsNullOrWhiteSpace(Class))
            {
                classes.Add(Class);
            }

            return string.Join(" ", classes);
        }
    }

    protected override void OnParametersSet()
    {
        var sourceFormula = Value ?? Field?.Preferences.Formula ?? string.Empty;
        if (!string.Equals(sourceFormula, _lastSourceFormula, StringComparison.Ordinal))
        {
            _formulaText = SigningFormulaHelper.Humanize(sourceFormula, Fields);
            _lastSourceFormula = sourceFormula;
            _error = null;
        }
    }

    private static void AddClass(List<string> classes, bool condition, string cssClass)
    {
        if (condition)
        {
            classes.Add(cssClass);
        }
    }

    private static string BoolText(bool value) => value ? "true" : "false";

    private async Task HandleInputAsync(string value)
    {
        _formulaText = value ?? string.Empty;
        _lastSourceFormula = _formulaText;
        _error = null;
        await ValueChanged.InvokeAsync(_formulaText);
    }

    private Task InsertFieldTokenAsync(SigningField field)
    {
        return InsertTextAsync("{{" + SigningFormulaHelper.GetFieldLabel(field) + "}}");
    }

    private string GetLocalizedFieldLabel(SigningField field)
    {
        return SigningTextResolver.FieldLabel(field, Culture, FallbackCulture, Loc);
    }

    private string GetFieldTypeLabel(SigningFieldType type)
    {
        return SigningTextResolver.FieldTypeLabel(type, Loc);
    }

    private static string GetOperatorText(string op)
    {
        return " " + op + " ";
    }

    /// <summary>The spoken name of an operator glyph (the "+" / "*" keys are ambiguous to a
    /// screen reader; phase 7 review).</summary>
    private string GetOperatorAriaLabel(string op)
    {
        return op switch
        {
            "+" => Loc["TmFormulaBuilder_OperatorPlus"],
            "-" => Loc["TmFormulaBuilder_OperatorMinus"],
            "*" => Loc["TmFormulaBuilder_OperatorMultiply"],
            "/" => Loc["TmFormulaBuilder_OperatorDivide"],
            "(" => Loc["TmFormulaBuilder_OperatorLeftParenthesis"],
            ")" => Loc["TmFormulaBuilder_OperatorRightParenthesis"],
            _ => op
        };
    }

    /// <summary>
    /// Inserts a fragment at the textarea's cursor through <c>signing-editor.js</c>; without a JS
    /// runtime (bUnit, static rendering, a disposed circuit) the fragment appends, so the builder
    /// stays usable everywhere.
    /// </summary>
    private async Task InsertTextAsync(string text)
    {
        if (Disabled)
        {
            return;
        }

        _interop ??= new SigningEditorInterop(JS);
        var inserted = await _interop.InsertAtCursorAsync(_textareaHost, text);
        _formulaText = inserted ?? _formulaText + text;
        _lastSourceFormula = _formulaText;
        _error = null;
        await ValueChanged.InvokeAsync(_formulaText);
    }

    private async Task SaveAsync()
    {
        if (Disabled)
        {
            return;
        }

        var result = SigningFormulaHelper.Validate(_formulaText, Fields, EffectiveCurrentFieldUuid);
        if (!result.IsValid)
        {
            _error = string.Join(" ", result.Errors);
            return;
        }

        _error = null;
        _lastSourceFormula = result.Formula;

        if (Field is not null)
        {
            Field.Preferences.Formula = result.Formula;
            Field.ReadOnly = true;
            await FieldChanged.InvokeAsync(Field);
        }

        await Saved.InvokeAsync(result.Formula);
    }

    private bool IsFormulaSourceField(SigningField field)
    {
        return !string.Equals(field.Uuid, EffectiveCurrentFieldUuid, StringComparison.Ordinal)
            && IsNumericFormulaField(field);
    }

    private bool WouldCreateCycle(SigningField sourceField)
    {
        if (string.IsNullOrWhiteSpace(EffectiveCurrentFieldUuid))
        {
            return false;
        }

        var formula = "{{" + sourceField.Uuid + "}}";
        return !SigningFormulaHelper.Validate(formula, Fields, EffectiveCurrentFieldUuid).IsValid;
    }

    private static bool IsNumericFormulaField(SigningField field)
    {
        return field.Type is SigningFieldType.Number or SigningFieldType.Payment
            || (field.Type is SigningFieldType.Select or SigningFieldType.Radio
                && field.Options.Count > 0
                && field.Options.All(option => decimal.TryParse(
                    option.Value,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out _)));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_interop is not null)
        {
            await _interop.DisposeAsync().ConfigureAwait(false);
            _interop = null;
        }
    }
}
