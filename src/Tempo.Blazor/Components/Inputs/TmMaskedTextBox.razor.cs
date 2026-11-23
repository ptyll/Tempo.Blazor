using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Tempo.Blazor.Components.Inputs;

/// <summary>A masked text input that enforces a specific format as the user types.</summary>
public partial class TmMaskedTextBox : ComponentBase
{
    private string _value = string.Empty;

    /// <summary>The mask pattern. Use '0' for digits, 'L' for letters, 'A' for alphanumeric, '&amp;' for any char. Other characters are treated as literals.</summary>
    [Parameter] public string Mask { get; set; } = string.Empty;

    /// <summary>The character displayed in empty positions. Defaults to '_'.</summary>
    [Parameter] public char PromptChar { get; set; } = '_';

    /// <summary>Whether the value includes literal characters from the mask. When false, only user-typed characters are stored. Defaults to true.</summary>
    [Parameter] public bool IncludeLiterals { get; set; } = true;

    /// <summary>The current value. When <see cref="IncludeLiterals"/> is true, this includes mask literals. When false, only user-typed characters are stored.</summary>
    [Parameter] public string Value
    {
        get => _value;
        set
        {
            var normalized = NormalizeIncomingValue(value);
            if (_value != normalized)
            {
                _value = normalized;
            }
        }
    }

    /// <summary>Event fired when the value changes.</summary>
    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    /// <summary>The value without mask literals and prompt characters.</summary>
    public string UnmaskedValue => MaskHelper.RemoveMask(_value, Mask, PromptChar, IncludeLiterals);

    /// <summary>Label shown above the input.</summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>
    /// Content rendered at the END of the label row. Rendered only with the slot set; without it
    /// the label markup is unchanged.
    /// </summary>
    [Parameter] public RenderFragment? LabelTrailing { get; set; }

    /// <summary>Placeholder text shown when the input is empty.</summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>Whether the input is disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Whether the input is read-only.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Additional CSS classes for the input element.</summary>
    [Parameter] public string? AdditionalCssClass { get; set; }

    /// <summary>HTML id attribute for the input.</summary>
    [Parameter] public string? Id { get; set; }

    /// <summary>Additional attributes to be applied to the input element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The value displayed in the input, including prompt characters for empty positions.</summary>
    private string DisplayValue => GetDisplayValue();

    private string NormalizeIncomingValue(string value)
    {
        if (string.IsNullOrEmpty(Mask))
            return value ?? string.Empty;

        if (IncludeLiterals)
            return value ?? string.Empty;

        // When IncludeLiterals is false, strip any literals that might have been provided
        var clean = MaskHelper.ExtractValidChars(value ?? string.Empty, Mask, PromptChar);
        return clean;
    }

    private string GetDisplayValue()
    {
        if (string.IsNullOrEmpty(Mask))
            return _value;

        var clean = IncludeLiterals ? MaskHelper.ExtractValidChars(_value, Mask, PromptChar) : _value;
        return MaskHelper.ApplyMask(clean, Mask, PromptChar, true);
    }

    private async Task HandleInputAsync(ChangeEventArgs e)
    {
        if (Disabled || ReadOnly)
            return;

        var rawValue = e.Value?.ToString() ?? string.Empty;

        if (string.IsNullOrEmpty(Mask))
        {
            if (_value != rawValue)
            {
                _value = rawValue;
                await ValueChanged.InvokeAsync(_value);
            }
            return;
        }

        // Determine if this is a deletion by comparing lengths with display value
        var currentDisplay = GetDisplayValue();
        var isDeletion = rawValue.Length < currentDisplay.Length;

        string newClean;
        if (isDeletion)
        {
            // For deletions, extract valid chars from the raw value
            newClean = MaskHelper.ExtractValidChars(rawValue, Mask, PromptChar);
        }
        else
        {
            // For insertions, process character by character
            newClean = MaskHelper.ProcessInput(rawValue, Mask, PromptChar, _value);
        }

        var previousClean = IncludeLiterals
            ? MaskHelper.ExtractValidChars(_value, Mask, PromptChar)
            : _value;

        if (newClean != previousClean)
        {
            var newValue = IncludeLiterals
                ? MaskHelper.ApplyMask(newClean, Mask, PromptChar, true)
                : newClean;

            _value = newValue;
            await ValueChanged.InvokeAsync(_value);
        }
    }

    private async Task HandleKeyDownAsync(KeyboardEventArgs e)
    {
        if (Disabled || ReadOnly)
            return;

        if (e.Key == "Backspace" && !string.IsNullOrEmpty(Mask) && !string.IsNullOrEmpty(_value))
        {
            var clean = IncludeLiterals
                ? MaskHelper.ExtractValidChars(_value, Mask, PromptChar)
                : _value;

            if (clean.Length > 0)
            {
                clean = clean[..^1];
                var newValue = IncludeLiterals
                    ? MaskHelper.ApplyMask(clean, Mask, PromptChar, true)
                    : clean;

                if (_value != newValue)
                {
                    _value = newValue;
                    await ValueChanged.InvokeAsync(_value);
                }
            }
        }
    }
}

/// <summary>Helper class for mask parsing and validation.</summary>
public static class MaskHelper
{
    /// <summary>Validates whether a character matches the given mask rule.</summary>
    public static bool IsValidForMask(char input, char maskRule)
    {
        return maskRule switch
        {
            '0' => char.IsDigit(input),
            '9' => char.IsDigit(input) || input == ' ',
            'L' => char.IsLetter(input),
            '?' => char.IsLetter(input) || input == ' ',
            'A' => char.IsLetterOrDigit(input),
            'a' => char.IsLetterOrDigit(input) || input == ' ',
            '&' => true,
            'C' => true,
            _ => false // literals are handled separately
        };
    }

    /// <summary>Checks whether the given mask character is a rule (not a literal).</summary>
    public static bool IsMaskRule(char maskChar)
    {
        return maskChar is '0' or '9' or 'L' or '?' or 'A' or 'a' or '&' or 'C';
    }

    /// <summary>Applies the mask to a value, filling empty positions with the prompt character.</summary>
    public static string ApplyMask(string value, string mask, char promptChar, bool includeLiterals)
    {
        if (string.IsNullOrEmpty(mask))
            return value;

        var result = new System.Text.StringBuilder();
        var valueIndex = 0;

        foreach (var maskChar in mask)
        {
            if (IsMaskRule(maskChar))
            {
                if (valueIndex < value.Length)
                {
                    result.Append(value[valueIndex]);
                    valueIndex++;
                }
                else
                {
                    result.Append(promptChar);
                }
            }
            else
            {
                result.Append(maskChar);
            }
        }

        return result.ToString();
    }

    /// <summary>Processes raw input and returns a masked value.</summary>
    public static string ProcessInput(string rawInput, string mask, char promptChar, bool includeLiterals, string previousValue)
    {
        if (string.IsNullOrEmpty(mask))
            return rawInput;

        // Extract only valid characters from the raw input
        var cleanInput = new System.Text.StringBuilder();
        var maskIndex = 0;
        var inputIndex = 0;

        while (maskIndex < mask.Length && inputIndex < rawInput.Length)
        {
            var maskChar = mask[maskIndex];
            var inputChar = rawInput[inputIndex];

            if (IsMaskRule(maskChar))
            {
                if (IsValidForMask(inputChar, maskChar))
                {
                    cleanInput.Append(inputChar);
                    maskIndex++;
                }
                inputIndex++;
            }
            else
            {
                // Literal in mask - skip if input matches, otherwise just advance mask
                if (inputChar == maskChar)
                {
                    inputIndex++;
                }
                maskIndex++;
            }
        }

        var result = cleanInput.ToString();

        // If no valid chars were entered, keep previous value if it was just a single char attempt
        if (result.Length == 0 && rawInput.Length > 0 && previousValue.Length > 0)
        {
            // Check if this was an invalid char attempt
            var prevMasked = ApplyMask(previousValue, mask, promptChar, includeLiterals);
            var rawClean = rawInput.Replace(promptChar.ToString(), "").Replace("(", "").Replace(")", "").Replace("-", "").Replace(" ", "");
            if (rawClean.Length <= previousValue.Length)
            {
                return previousValue;
            }
        }

        return result;
    }

    /// <summary>Removes mask literals and prompt characters to produce an unmasked value.</summary>
    public static string RemoveMask(string value, string mask, char promptChar, bool includeLiterals)
    {
        if (string.IsNullOrEmpty(mask) || string.IsNullOrEmpty(value))
            return value;

        if (!includeLiterals)
            return value;

        var result = new System.Text.StringBuilder();
        var valueIndex = 0;

        foreach (var maskChar in mask)
        {
            if (valueIndex >= value.Length)
                break;

            if (IsMaskRule(maskChar))
            {
                var ch = value[valueIndex];
                if (ch != promptChar)
                {
                    result.Append(ch);
                }
                valueIndex++;
            }
            else
            {
                valueIndex++;
            }
        }

        // Append any remaining value chars beyond mask length
        while (valueIndex < value.Length)
        {
            var ch = value[valueIndex];
            if (ch != promptChar)
            {
                result.Append(ch);
            }
            valueIndex++;
        }

        return result.ToString();
    }

    /// <summary>Extracts only characters that are valid for their corresponding mask positions.</summary>
    public static string ExtractValidChars(string input, string mask, char promptChar)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(mask))
            return input ?? string.Empty;

        var result = new System.Text.StringBuilder();
        var inputIndex = 0;

        foreach (var maskChar in mask)
        {
            if (inputIndex >= input.Length)
                break;

            if (IsMaskRule(maskChar))
            {
                if (input[inputIndex] != promptChar && IsValidForMask(input[inputIndex], maskChar))
                {
                    result.Append(input[inputIndex]);
                }
                inputIndex++;
            }
            else
            {
                // Literal in mask - skip it if present in input
                if (input[inputIndex] == maskChar)
                {
                    inputIndex++;
                }
            }
        }

        // Append any remaining valid chars beyond mask length
        while (inputIndex < input.Length)
        {
            if (input[inputIndex] != promptChar)
            {
                result.Append(input[inputIndex]);
            }
            inputIndex++;
        }

        return result.ToString();
    }

    /// <summary>Processes raw input against a mask, returning only valid characters.</summary>
    public static string ProcessInput(string rawInput, string mask, char promptChar, string previousValue)
    {
        if (string.IsNullOrEmpty(mask))
            return rawInput;

        var extracted = ExtractValidChars(rawInput, mask, promptChar);

        // If nothing valid was extracted and previous value had content, 
        // check if this was an invalid char attempt (keep previous)
        if (extracted.Length == 0 && rawInput.Length > 0 && previousValue.Length > 0)
        {
            var prevClean = ExtractValidChars(previousValue, mask, promptChar);
            // If raw input length is similar to previous display, it was likely an invalid char
            var prevDisplay = ApplyMask(prevClean, mask, promptChar, true);
            if (rawInput.Length <= prevDisplay.Length && rawInput.Length >= prevClean.Length)
            {
                return previousValue;
            }
        }

        return extracted;
    }
}
