using global::FluentValidation;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Localization;

namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>
/// The network names of <c>mj-social-element</c> the item dialog offers (§3.9): the names MJML itself
/// documents for its built-in icons. Any other network is the "custom" choice and then requires an
/// explicit icon URL, because nothing would render without one.
/// </summary>
public static class EmailSocialNetworks
{
    /// <summary>The names offered by the network select, in the order the dialog shows them.</summary>
    public static readonly IReadOnlyList<string> Supported =
    [
        "facebook", "instagram", "linkedin", "twitter", "x", "youtube", "github", "pinterest",
        "tumblr", "xing", "snapchat", "vimeo", "medium", "soundcloud", "dribbble", "web",
    ];

    /// <summary>Answers whether <paramref name="name"/> is a supported network (case-insensitive).</summary>
    public static bool IsSupported(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && Supported.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The localization keys of the item and table-cell dialog validation. They live in the package's own table
/// (<c>TmEmailResources*.json</c>), so the messages follow the editor's culture and never a validator's
/// English default.
/// </summary>
public static class EmailItemValidationKeys
{
    /// <summary>The value is a link outside the URL allow-list (§3.7a).</summary>
    public const string UrlScheme = "EmailEditor_Item_Error_UrlScheme";

    /// <summary>A required display text is missing.</summary>
    public const string TextRequired = "EmailEditor_Item_Error_TextRequired";

    /// <summary>A required link target is missing.</summary>
    public const string HrefRequired = "EmailEditor_Item_Error_HrefRequired";

    /// <summary>A required image source is missing.</summary>
    public const string SrcRequired = "EmailEditor_Item_Error_SrcRequired";

    /// <summary>A required alternative text is missing.</summary>
    public const string AltRequired = "EmailEditor_Item_Error_AltRequired";

    /// <summary>A required title is missing.</summary>
    public const string TitleRequired = "EmailEditor_Item_Error_TitleRequired";

    /// <summary>The background colour is outside the colour allow-list.</summary>
    public const string InvalidColor = "EmailEditor_Item_Error_InvalidColor";

    /// <summary>A column/row span is not a number between 1 and the documented maximum.</summary>
    public const string InvalidSpan = "EmailEditor_Item_Error_InvalidSpan";

    /// <summary>
    /// No <see cref="IValidator{T}"/> is registered for the item. The dialog refuses to save rather than
    /// skip validation and accept a value the pipeline would later drop.
    /// </summary>
    public const string ValidatorMissing = "EmailEditor_Item_Error_ValidatorMissing";
}

/// <summary>Validates a social element before the dialog may apply it.</summary>
public sealed class EmailSocialElementValidator : AbstractValidator<EmailSocialElement>
{
    /// <summary>Initializes the validator with the editor's localizer.</summary>
    public EmailSocialElementValidator(ITmEmailLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(x => x.Href)
            .Must(EmailUrlPolicy.IsAllowed)
            .WithMessage(_ => localizer[EmailItemValidationKeys.UrlScheme]);

        RuleFor(x => x.Src)
            .Must(EmailUrlPolicy.IsAllowed)
            .WithMessage(_ => localizer[EmailItemValidationKeys.UrlScheme]);

        // A supported network name carries MJML's own icon; anything else ("vlastní") has none, so the URL
        // of the icon is the only thing that can render it.
        RuleFor(x => x.Src)
            .Must(src => !string.IsNullOrWhiteSpace(src))
            .When(x => !EmailSocialNetworks.IsSupported(x.Name))
            .WithMessage(_ => localizer[EmailItemValidationKeys.SrcRequired]);

        // background-color of mj-social-element is an MJML attribute as well: the inspector's colour rule
        // applies, not the cell-bgcolor allow-list (review F9).
        RuleFor(x => x.BackgroundColor)
            .Must(PropertyReflection.IsValidColor)
            .When(x => !string.IsNullOrWhiteSpace(x.BackgroundColor))
            .WithMessage(_ => localizer[EmailItemValidationKeys.InvalidColor]);
    }
}

/// <summary>Validates a navbar link before the dialog may apply it.</summary>
public sealed class EmailNavbarLinkValidator : AbstractValidator<EmailNavbarLink>
{
    /// <summary>Initializes the validator with the editor's localizer.</summary>
    public EmailNavbarLinkValidator(ITmEmailLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(x => x.Text)
            .Must(text => !string.IsNullOrWhiteSpace(text))
            .WithMessage(_ => localizer[EmailItemValidationKeys.TextRequired]);

        RuleFor(x => x.Href)
            .Must(href => !string.IsNullOrWhiteSpace(href))
            .WithMessage(_ => localizer[EmailItemValidationKeys.HrefRequired]);

        RuleFor(x => x.Href)
            .Must(EmailUrlPolicy.IsAllowed)
            .WithMessage(_ => localizer[EmailItemValidationKeys.UrlScheme]);

        // A navbar link's colour is an MJML attribute, not the cell background of a table: it is written
        // into the element's style, so it follows the inspector's rule (PropertyReflection.IsValidColor —
        // hex, rgb()/hsl() or a keyword). EmailColorPolicy stays the narrower allow-list of the cell
        // bgcolor attribute (review F9).
        RuleFor(x => x.Color)
            .Must(PropertyReflection.IsValidColor)
            .When(x => !string.IsNullOrWhiteSpace(x.Color))
            .WithMessage(_ => localizer[EmailItemValidationKeys.InvalidColor]);
    }
}

/// <summary>Validates a carousel image before the dialog may apply it.</summary>
public sealed class EmailCarouselImageValidator : AbstractValidator<EmailCarouselImage>
{
    /// <summary>Initializes the validator with the editor's localizer.</summary>
    public EmailCarouselImageValidator(ITmEmailLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(x => x.Src)
            .Must(src => !string.IsNullOrWhiteSpace(src))
            .WithMessage(_ => localizer[EmailItemValidationKeys.SrcRequired]);

        RuleFor(x => x.Src)
            .Must(EmailUrlPolicy.IsAllowed)
            .WithMessage(_ => localizer[EmailItemValidationKeys.UrlScheme]);

        RuleFor(x => x.Alt)
            .Must(alt => !string.IsNullOrWhiteSpace(alt))
            .WithMessage(_ => localizer[EmailItemValidationKeys.AltRequired]);

        RuleFor(x => x.Href)
            .Must(EmailUrlPolicy.IsAllowed)
            .WithMessage(_ => localizer[EmailItemValidationKeys.UrlScheme]);
    }
}

/// <summary>Validates an accordion item before the dialog may apply it.</summary>
public sealed class EmailAccordionItemValidator : AbstractValidator<EmailAccordionItem>
{
    /// <summary>Initializes the validator with the editor's localizer.</summary>
    public EmailAccordionItemValidator(ITmEmailLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(x => x.Title)
            .Must(title => !string.IsNullOrWhiteSpace(title))
            .WithMessage(_ => localizer[EmailItemValidationKeys.TitleRequired]);

        // MJML attributes (written into the element's style), so the inspector's colour rule applies — not
        // the narrower cell-bgcolor allow-list (review F9).
        RuleFor(x => x.BackgroundColor)
            .Must(PropertyReflection.IsValidColor)
            .When(x => !string.IsNullOrWhiteSpace(x.BackgroundColor))
            .WithMessage(_ => localizer[EmailItemValidationKeys.InvalidColor]);

        RuleFor(x => x.TitleColor)
            .Must(PropertyReflection.IsValidColor)
            .When(x => !string.IsNullOrWhiteSpace(x.TitleColor))
            .WithMessage(_ => localizer[EmailItemValidationKeys.InvalidColor]);
    }
}

/// <summary>Validates a table cell before the cell dialog may apply it.</summary>
public sealed class EmailTableCellValidator : AbstractValidator<EmailTableCell>
{
    /// <summary>The largest row/column span the dialog accepts.</summary>
    public const int MaxSpan = 20;

    /// <summary>Initializes the validator with the editor's localizer.</summary>
    public EmailTableCellValidator(ITmEmailLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(x => x.Href)
            .Must(EmailUrlPolicy.IsAllowed)
            .WithMessage(_ => localizer[EmailItemValidationKeys.UrlScheme]);

        RuleFor(x => x.BackgroundColor)
            .Must(EmailColorPolicy.IsAllowed)
            .When(x => !string.IsNullOrWhiteSpace(x.BackgroundColor))
            .WithMessage(_ => localizer[EmailItemValidationKeys.InvalidColor]);

        RuleFor(x => x.ColSpan)
            .Must(span => span is null or >= 1 and <= MaxSpan)
            .WithMessage(_ => localizer[EmailItemValidationKeys.InvalidSpan]);

        RuleFor(x => x.RowSpan)
            .Must(span => span is null or >= 1 and <= MaxSpan)
            .WithMessage(_ => localizer[EmailItemValidationKeys.InvalidSpan]);
    }
}
