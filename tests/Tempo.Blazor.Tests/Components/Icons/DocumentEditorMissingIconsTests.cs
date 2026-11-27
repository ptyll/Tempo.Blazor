using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.DocumentEditor.Registry;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Icons;

/// <summary>
/// Fáze 14: TmIcon.GetBuiltInSvg vrací pro neznámý název prázdný string a komponenta pak renderuje
/// prázdný <span class="tm-icon-unknown"> — tlačítko v toolbaru má místo ikony díru. Audit našel
/// 25 názvů používaných DocumentEditorem, které v built-in sadě chyběly, + ikonové duplicity
/// (numbered list = bullet list, doubleStrikethrough = strikethrough, insertEquation = superscript,
/// insertSymbol = pilcrow).
/// </summary>
public class DocumentEditorMissingIconsTests : LocalizationTestBase
{
    /// <summary>25 názvů z auditu Fáze 14 + sémantické a rozlišovací ikony přidané touto fází.</summary>
    public static TheoryData<string> RequiredIconNames() => new()
    {
        // 25 chybějících názvů používaných DocumentEditorem
        "ban", "between-horizontal-end", "between-vertical-end", "book-open", "book-plus",
        "captions", "case-sensitive", "case-upper", "circle-minus", "clipboard-list",
        "columns-3", "file-type", "gallery-horizontal-end", "history", "list-tree",
        "message-square-plus", "paintbrush", "printer", "refresh-ccw", "rows-3",
        "scan-text", "signature", "subscript", "superscript", "wand-sparkles",
        // rozlišení duplicit + sémantické ikony + doplněné selecty
        "list-ordered", "double-strikethrough", "sigma", "omega",
        "a-large-small", "arrow-up-from-line", "arrow-down-to-line",
    };

    [Theory]
    [MemberData(nameof(RequiredIconNames))]
    public void GetBuiltInSvg_ResolvesRequiredDocumentEditorIcon(string name)
    {
        TmIcon.GetBuiltInSvg(name).Should().NotBeNullOrEmpty(
            $"ikona '{name}' je používaná DocumentEditorem a bez built-in SVG renderuje prázdný tm-icon-unknown span");
    }

    [Fact]
    public void EveryBuiltInToolbarItemIcon_IsResolvable()
    {
        foreach (var item in DocumentEditorBuiltInToolbar.DefaultItems)
        {
            if (string.IsNullOrEmpty(item.Icon))
            {
                continue;
            }

            TmIcon.GetBuiltInSvg(item.Icon).Should().NotBeNullOrEmpty(
                $"toolbar item '{item.Id}' deklaruje ikonu '{item.Icon}', která musí existovat v built-in sadě");
        }
    }

    [Fact]
    public void FormattingSelectItems_HaveIcons()
    {
        // Fáze 14: fontFamily/fontSize/spacingBefore/spacingAfter měly Icon = null → prázdné místo
        // v overflow menu. Ikony doplněny (type / a-large-small / arrow-up-from-line / arrow-down-to-line).
        var byId = DocumentEditorBuiltInToolbar.DefaultItems.ToDictionary(i => i.Id);
        byId["fontFamily"].Icon.Should().Be("type");
        byId["fontSize"].Icon.Should().Be("a-large-small");
        byId["spacingBefore"].Icon.Should().Be("arrow-up-from-line");
        byId["spacingAfter"].Icon.Should().Be("arrow-down-to-line");
    }

    [Fact]
    public void InsertEquationAndSymbol_UseSemanticIcons()
    {
        var byId = DocumentEditorBuiltInToolbar.DefaultItems.ToDictionary(i => i.Id);
        byId["insertEquation"].Icon.Should().Be("sigma", "rovnice = sigma, ne superscript toggle glyph");
        byId["mathInsertEquation"].Icon.Should().Be("sigma");
        byId["insertSymbol"].Icon.Should().Be("omega", "symbol = omega, ne pilcrow (ten patří show-blocks)");
    }

    [Fact]
    public void DoubleStrikethrough_GlyphDiffersFromStrikethrough()
    {
        var single = TmIcon.GetBuiltInSvg("strikethrough");
        var dbl = TmIcon.GetBuiltInSvg("double-strikethrough");
        dbl.Should().NotBeNullOrEmpty();
        dbl.Should().NotBe(single, "doubleStrikethrough musí být vizuálně rozlišitelný od strikethrough");
    }

    [Fact]
    public void Toolbar_NumberedList_UsesListOrderedIcon_DistinctFromBulletList()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        var bullet = cut.Find("[data-testid='document-bullet-list'] svg").InnerHtml;
        var numbered = cut.Find("[data-testid='document-numbered-list'] svg").InnerHtml;
        numbered.Should().NotBe(bullet, "číslovaný seznam musí mít vlastní ikonu (list-ordered), ne stejnou jako odrážkový");
    }

    [Fact]
    public void Toolbar_DoubleStrikethroughButton_UsesDistinctGlyph()
    {
        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.ShowAdvancedCharacterFormatting, true));

        var single = cut.Find("[data-testid='document-strikethrough'] svg").InnerHtml;
        // The less frequent character formats live in the core "More" menu (OverflowOnly); the entry carries the icon.
        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();
        var dbl = cut.Find("[role='menu'] [data-testid='document-double-strikethrough'] svg").InnerHtml;
        dbl.Should().NotBe(single);
    }

    [Fact]
    public void IconNames_ExposeNewConstants()
    {
        IconNames.ListOrdered.Should().Be("list-ordered");
        IconNames.Printer.Should().Be("printer");
        IconNames.History.Should().Be("history");
        IconNames.Sigma.Should().Be("sigma");
        IconNames.Omega.Should().Be("omega");
        IconNames.DoubleStrikethrough.Should().Be("double-strikethrough");
        IconNames.Signature.Should().Be("signature");
        IconNames.WandSparkles.Should().Be("wand-sparkles");
    }
}
