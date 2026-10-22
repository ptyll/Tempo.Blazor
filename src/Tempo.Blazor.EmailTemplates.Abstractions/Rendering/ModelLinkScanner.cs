using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Finds the blocked URL schemes of the MODEL links the <see cref="MjmlGenerator"/> rewrites to <c>#</c>
/// <b>before</b> the generated markup is checked.
/// <para>
/// The generator fail-closes every link it emits (a blocked scheme becomes <c>#</c>) so the EXPORT — which
/// runs without <see cref="MarkupGuard"/> — can never carry a <c>javascript:</c> URL. The consequence is that
/// the guard no longer recognises the scheme in the markup, so this scanner is what keeps the render's warning
/// and audit (§3.7a): it reports the scheme of every link the model carries, and the renderer deduplicates the
/// result against the guard's own findings.
/// </para>
/// </summary>
internal static class ModelLinkScanner
{
    /// <summary>The distinct blocked schemes of the model links, in a stable order.</summary>
    public static IReadOnlyList<string> BlockedModelSchemes(EmailTemplateDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var schemes = new SortedSet<string>(StringComparer.Ordinal);

        void Add(string? url)
        {
            if (UrlSchemePolicy.FindBlockedScheme(url) is { } scheme) schemes.Add(scheme);
        }

        foreach (var font in document.Styles.Fonts) Add(font.Href);
        foreach (var section in DocumentTree.AllSections(document)) Add(section.BackgroundUrl);
        foreach (var block in DocumentTree.AllBlocks(document)) AddBlockLinks(block, Add);

        return schemes.ToList();
    }

    /// <summary>Adds the scheme of every link a single block carries.</summary>
    private static void AddBlockLinks(EmailBlockBase block, Action<string?> add)
    {
        switch (block)
        {
            case EmailButtonBlock button:
                add(button.Href);
                break;
            case EmailImageBlock image:
                add(image.Src);
                add(image.Href);
                break;
            case EmailTableBlock table:
                foreach (var cell in table.Rows.SelectMany(row => row.Cells)) add(cell.Href);
                break;
            case EmailNavbarBlock navbar:
                foreach (var link in navbar.Links) add(link.Href);
                break;
            case EmailSocialBlock social:
                foreach (var element in social.Elements)
                {
                    add(element.Href);
                    add(element.Src);
                }

                break;
            case EmailCarouselBlock carousel:
                foreach (var image in carousel.Images)
                {
                    add(image.Src);
                    add(image.Href);
                    add(image.ThumbnailsSrc);
                }

                break;
            case EmailHeroBlock hero:
                add(hero.BackgroundUrl);
                break;
            case EmailWrapperBlock wrapper:
                add(wrapper.BackgroundUrl);
                break;
        }
    }
}
