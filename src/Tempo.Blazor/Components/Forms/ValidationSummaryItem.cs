using Microsoft.AspNetCore.Components.Forms;

namespace Tempo.Blazor.Components.Forms;

/// <summary>
/// One validation message together with the field it was attached to. Used by
/// <see cref="TmValidationSummary"/> when the host opts into field-identity grouping, click
/// handling or templating (<c>GroupItemsBy</c> / <c>OnItemClick</c> / <c>ItemTemplateWithField</c>).
/// </summary>
/// <param name="Field">The model field the message was stored against.</param>
/// <param name="Message">The validation message text.</param>
public sealed record ValidationSummaryItem(FieldIdentifier Field, string Message);
