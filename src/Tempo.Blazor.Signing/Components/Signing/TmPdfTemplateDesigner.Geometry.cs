using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

public partial class TmPdfTemplateDesigner
{
    /// <summary>
    /// The designer root is a size container, so a fixed menu is positioned against the root rather than
    /// the viewport. Subtracting this origin turns a click's viewport coordinates into menu coordinates.
    /// </summary>
    private async Task<FixedOrigin> GetFixedOriginAsync()
    {
        if (_workspaceRef.Id is null)
        {
            return new FixedOrigin();
        }

        try
        {
            _jsModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Tempo.Blazor.Signing/js/pdf-template-designer.js");
            return await _jsModule.InvokeAsync<FixedOrigin>("getFixedOrigin", _workspaceRef) ?? new FixedOrigin();
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            return new FixedOrigin();
        }
    }

    private async Task<(double X, double Y)> ToPointAsync(SigningDocumentPage page, MouseEventArgs args)
    {
        if (_pageSurfaceRefs.TryGetValue(GetPageKey(page), out var reference))
        {
            try
            {
                _jsModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Tempo.Blazor.Signing/js/pdf-template-designer.js");
                if (_jsModule is null)
                {
                    return ToPointFromOffset(page, args);
                }

                var rect = await _jsModule.InvokeAsync<PageSurfaceRect>("getSurfaceRect", args.ClientX, args.ClientY, GetPageKey(page));
                if (rect is { Width: > 0, Height: > 0 } && (args.ClientX != 0 || args.ClientY != 0))
                {
                    return (
                        Clamp((args.ClientX - rect.Left) / rect.Width, 0, 1),
                        Clamp((args.ClientY - rect.Top) / rect.Height, 0, 1));
                }
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException)
            {
                // Fall back to event offsets when JavaScript is unavailable in tests or prerendering.
            }
            catch (InvalidOperationException)
            {
                // Fall back to event offsets when JavaScript is unavailable in tests or prerendering.
            }
        }

        return ToPointFromOffset(page, args);
    }

    private static (double X, double Y) ToPointFromOffset(SigningDocumentPage page, MouseEventArgs args)
    {
        var width = page.Width > 0 ? page.Width : DefaultPageWidth;
        var height = page.Height > 0 ? page.Height : DefaultPageHeight;
        return (
            Clamp(args.OffsetX / width, 0, 1),
            Clamp(args.OffsetY / height, 0, 1));
    }

    private static SigningFieldArea CreateAreaFromPoints(SigningDocumentPage page, double startX, double startY, double endX, double endY, string uuid)
    {
        var x = Math.Min(startX, endX);
        var y = Math.Min(startY, endY);
        var width = Math.Abs(endX - startX);
        var height = Math.Abs(endY - startY);

        return new SigningFieldArea
        {
            Uuid = uuid,
            AttachmentUuid = page.AttachmentUuid,
            Page = page.PageIndex,
            X = x,
            Y = y,
            Width = width,
            Height = height
        };
    }

    private static SigningFieldArea CreateDefaultAreaAtPoint(SigningDocumentPage page, SigningFieldType type, double x, double y)
    {
        var (width, height) = GetDefaultAreaSize(type);
        return new SigningFieldArea
        {
            Uuid = Guid.NewGuid().ToString("N"),
            AttachmentUuid = page.AttachmentUuid,
            Page = page.PageIndex,
            X = Clamp(x - width / 2, 0, 1 - width),
            Y = Clamp(y - height / 2, 0, 1 - height),
            Width = width,
            Height = height
        };
    }

    private static (double Width, double Height) GetDefaultAreaSize(SigningFieldType type)
    {
        return type switch
        {
            SigningFieldType.Signature => (0.34, 0.065),
            SigningFieldType.Initials => (0.16, 0.065),
            SigningFieldType.Date or SigningFieldType.DateNow => (0.22, 0.055),
            SigningFieldType.Number => (0.2, 0.055),
            SigningFieldType.Checkbox => (0.08, 0.05),
            SigningFieldType.Radio or SigningFieldType.Select or SigningFieldType.Multiple => (0.32, 0.06),
            SigningFieldType.File or SigningFieldType.Image or SigningFieldType.Stamp => (0.26, 0.07),
            SigningFieldType.Phone => (0.28, 0.055),
            _ => (0.3, 0.055)
        };
    }

    private static bool Intersects(SigningFieldArea a, SigningFieldArea b)
    {
        return a.X < b.X + b.Width
            && a.X + a.Width > b.X
            && a.Y < b.Y + b.Height
            && a.Y + a.Height > b.Y;
    }

    private static string GetAreaStyle(SigningFieldArea area)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"left: {area.X * 100}%; top: {area.Y * 100}%; width: {area.Width * 100}%; height: {area.Height * 100}%;");
    }

    private static string GetRectangleStyle(SigningRectangle rectangle)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"left: {rectangle.X * 100}%; top: {rectangle.Y * 100}%; width: {rectangle.Width * 100}%; height: {rectangle.Height * 100}%;");
    }

    private string GetPaletteItemClass(SigningFieldType type)
    {
        return _drawType == type
            ? "tm-pdf-template-designer__palette-item tm-pdf-template-designer__palette-item--active"
            : "tm-pdf-template-designer__palette-item";
    }

    /// <summary>
    /// Swaps the area for a new instance. The overlay skips its render when <c>Area</c> is the same object,
    /// so an in-place edit never reaches the positioned box.
    /// </summary>
    private static void ReplaceArea(SigningField field, SigningFieldArea current, SigningFieldArea replacement)
    {
        var index = field.Areas.IndexOf(current);
        if (index < 0)
        {
            return;
        }

        replacement.Uuid = current.Uuid;
        replacement.AttachmentUuid = current.AttachmentUuid;
        replacement.Page = current.Page;
        field.Areas[index] = replacement;
    }

    private static void CopyInto(SigningFieldArea target, SigningFieldArea source)
    {
        target.X = source.X;
        target.Y = source.Y;
        target.Width = source.Width;
        target.Height = source.Height;
        target.CellWidth = source.CellWidth;
        target.OptionUuid = source.OptionUuid;
    }

    private static SigningField Clone(SigningField field)
    {
        return new SigningField
        {
            Uuid = field.Uuid,
            SubmitterUuid = field.SubmitterUuid,
            Name = field.Name,
            Labels = Clone(field.Labels),
            Title = field.Title,
            Titles = Clone(field.Titles),
            Description = field.Description,
            Descriptions = Clone(field.Descriptions),
            Placeholders = Clone(field.Placeholders),
            Type = field.Type,
            Required = field.Required,
            ReadOnly = field.ReadOnly,
            Prefillable = field.Prefillable,
            DefaultValue = field.DefaultValue,
            Preferences = Clone(field.Preferences),
            Validation = field.Validation is null ? null : Clone(field.Validation),
            Conditions = field.Conditions.Select(Clone).ToList(),
            Options = field.Options.Select(Clone).ToList(),
            Areas = field.Areas.Select(Clone).ToList()
        };
    }

    private static SigningFieldPreferences Clone(SigningFieldPreferences preferences)
    {
        return new SigningFieldPreferences
        {
            Color = preferences.Color,
            Align = preferences.Align,
            Format = preferences.Format,
            FontFamily = preferences.FontFamily,
            FontSize = preferences.FontSize,
            WithSignatureId = preferences.WithSignatureId,
            WithLogo = preferences.WithLogo,
            ReasonFieldUuid = preferences.ReasonFieldUuid,
            Formula = preferences.Formula,
            Currency = preferences.Currency,
            Price = preferences.Price,
            PriceId = preferences.PriceId,
            PaymentLinkId = preferences.PaymentLinkId,
            AdditionalSettings = new Dictionary<string, object?>(preferences.AdditionalSettings)
        };
    }

    private static SigningFieldValidation Clone(SigningFieldValidation validation)
    {
        return new SigningFieldValidation
        {
            Pattern = validation.Pattern,
            Message = validation.Message,
            Messages = Clone(validation.Messages),
            Min = validation.Min,
            Max = validation.Max,
            Step = validation.Step
        };
    }

    private static SigningFieldCondition Clone(SigningFieldCondition condition)
    {
        return new SigningFieldCondition
        {
            FieldUuid = condition.FieldUuid,
            Action = condition.Action,
            Value = condition.Value,
            Operation = condition.Operation
        };
    }

    private static SigningFieldOption Clone(SigningFieldOption option)
    {
        return new SigningFieldOption
        {
            Uuid = option.Uuid,
            Labels = Clone(option.Labels),
            Value = option.Value
        };
    }

    private static SigningLocalizedText Clone(SigningLocalizedText text)
    {
        return new SigningLocalizedText
        {
            Default = text.Default,
            Translations = new Dictionary<string, string>(text.Translations, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static SigningFieldArea Clone(SigningFieldArea area)
    {
        return new SigningFieldArea
        {
            Uuid = area.Uuid,
            AttachmentUuid = area.AttachmentUuid,
            Page = area.Page,
            X = area.X,
            Y = area.Y,
            Width = area.Width,
            Height = area.Height,
            CellWidth = area.CellWidth,
            OptionUuid = area.OptionUuid
        };
    }
}
