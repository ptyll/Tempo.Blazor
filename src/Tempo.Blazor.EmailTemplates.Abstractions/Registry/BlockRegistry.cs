using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Registry;

/// <summary>Default <see cref="IBlockRegistry"/> seeded with the 14 built-in MJML block kinds.</summary>
public sealed class BlockRegistry : IBlockRegistry
{
    private const string CategoryContent = "content";
    private const string CategoryMedia = "media";
    private const string CategoryLayout = "layout";
    private const string CategoryAdvanced = "advanced";

    private readonly List<BlockDescriptor> _descriptors;

    /// <summary>Initializes a new registry containing the built-in block descriptors.</summary>
    public BlockRegistry()
    {
        _descriptors = new List<BlockDescriptor>
        {
            new("text", BlockType.Text, "block.text.name", "type", CategoryContent, () => new EmailTextBlock()),
            new("button", BlockType.Button, "block.button.name", "mouse-pointer", CategoryContent, () => new EmailButtonBlock()),
            new("image", BlockType.Image, "block.image.name", "image", CategoryMedia, () => new EmailImageBlock()),
            new("divider", BlockType.Divider, "block.divider.name", "minus", CategoryLayout, () => new EmailDividerBlock()),
            new("spacer", BlockType.Spacer, "block.spacer.name", "between-horizontal-start", CategoryLayout, () => new EmailSpacerBlock()),
            new("raw", BlockType.Raw, "block.raw.name", "code", CategoryAdvanced, () => new EmailRawBlock()),
            new("table", BlockType.Table, "block.table.name", "table", CategoryContent, () => new EmailTableBlock()),
            new("social", BlockType.Social, "block.social.name", "share", CategoryMedia, () => new EmailSocialBlock()),
            new("hero", BlockType.Hero, "block.hero.name", "layout-template", CategoryLayout, () => new EmailHeroBlock()),
            new("navbar", BlockType.Navbar, "block.navbar.name", "menu", CategoryContent, () => new EmailNavbarBlock()),
            new("carousel", BlockType.Carousel, "block.carousel.name", "images", CategoryMedia, () => new EmailCarouselBlock()),
            new("accordion", BlockType.Accordion, "block.accordion.name", "list", CategoryContent, () => new EmailAccordionBlock()),
            new("wrapper", BlockType.Wrapper, "block.wrapper.name", "box", CategoryLayout, () => new EmailWrapperBlock()),
            new("group", BlockType.Group, "block.group.name", "columns", CategoryLayout, () => new EmailGroupBlock()),
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<BlockDescriptor> GetAll() => _descriptors;

    /// <inheritdoc />
    public EmailBlockBase CreateInstance(BlockType type)
    {
        var descriptor = _descriptors.FirstOrDefault(d => d.Type == type)
            ?? throw new ArgumentException($"No block descriptor registered for type '{type}'.", nameof(type));
        return descriptor.Factory();
    }

    /// <inheritdoc />
    public EmailBlockBase CreateById(string id)
    {
        var descriptor = _descriptors.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException($"No block descriptor registered with id '{id}'.", nameof(id));
        return descriptor.Factory();
    }

    /// <inheritdoc />
    public void RegisterCustom(BlockDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _descriptors.Add(descriptor);
    }
}
