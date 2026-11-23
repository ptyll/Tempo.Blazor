using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Components.Forms;

namespace Tempo.Blazor.Components.Forms;

/// <summary>
/// Enumerates the <see cref="FieldIdentifier"/>s currently tracked by an <see cref="EditContext"/>.
/// The framework only exposes messages, not the field keys; <c>_fieldStates</c> is the documented
/// store <c>GetValidationMessages()</c> itself walks.
/// </summary>
internal static class EditContextFieldEnumerator
{
    private static readonly FieldInfo? FieldStatesField = typeof(EditContext)
        .GetField("_fieldStates", BindingFlags.Instance | BindingFlags.NonPublic);

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "EditContext._fieldStates is the only way to pair each message with its FieldIdentifier.")]
    internal static IEnumerable<FieldIdentifier> Fields(EditContext context)
    {
        if (FieldStatesField?.GetValue(context) is not IDictionary dict)
        {
            yield break;
        }

        foreach (var key in dict.Keys)
        {
            if (key is FieldIdentifier field)
            {
                yield return field;
            }
        }
    }
}
