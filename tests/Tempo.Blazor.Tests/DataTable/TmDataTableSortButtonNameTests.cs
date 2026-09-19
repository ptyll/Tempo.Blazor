using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// The accessible-name contract of the <c>.tm-th-sort</c> button.
/// <para>
/// WCAG 4.1.2: every control must have a name a screen reader can announce — and a sort button's
/// name cannot be the sort ICON, because the icon span is <c>aria-hidden</c>. Up to 2.8.26 the
/// button named itself only on the paths that happened to have text: a column with a plain
/// <c>Title</c> was named by its content, and a templated header fell back to
/// <c>aria-label = Title ?? "Sort ascending"</c>. The combination nobody covered was the
/// title-LESS icon-only header — <c>PropertyName</c> set, no <c>Title</c>, no template — whose
/// button contained only aria-hidden spans and announced NOTHING.
/// </para>
/// <para>
/// The name is now emitted as an explicit <c>aria-label</c> on every render — derived as
/// <c>SortLabel ?? Title ?? "Sort ascending"</c> — so the guarantee lives in the markup rather
/// than in the luck of whether the button happens to hold visible text. <c>SortLabel</c> exists
/// for the consumer who wants the name to say more than the column caption (e.g. "Sort by last
/// name" while the visible label stays "Name").
/// </para>
/// </summary>
public class TmDataTableSortButtonNameTests : LocalizationTestBase
{
    private sealed record Person(string Name);

    private static readonly List<Person> People = [new("Alice"), new("Bob")];

    private IRenderedComponent<TmDataTable<Person>> RenderTable(
        Action<Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder> configureColumn)
        => Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<Person>>(0);
                configureColumn(b);
                b.AddAttribute(90, "Field", (Func<Person, object?>)(x => x.Name));
                b.CloseComponent();
            });
        });

    [Fact]
    public void ASortButton_Names_Itself_From_The_Column_Title()
    {
        var cut = RenderTable(b =>
        {
            b.AddAttribute(1, "Title", "Name");
            b.AddAttribute(2, "Sortable", true);
        });

        cut.Find("button.tm-th-sort").GetAttribute("aria-label").Should().Be("Name",
            "viditelný text JE jméno — ale garance má stát v markupu, ne v náhodě obsahu");
    }

    [Fact]
    public void AnIconOnlySortableHeader_Still_Has_An_Accessible_Name()
    {
        // PropertyName carries the column key; no Title, no template. Before 2.9.0 this button
        // contained ONLY aria-hidden spans — a real <button> that announced nothing.
        var cut = RenderTable(b =>
        {
            b.AddAttribute(1, "PropertyName", "Name");
            b.AddAttribute(2, "Sortable", true);
        });

        cut.Find("button.tm-th-sort").GetAttribute("aria-label").Should().Be("Sort ascending",
            "tlačítko, jehož jediný obsah je skrytá ikona, musí nést jméno v aria-label");
    }

    [Fact]
    public void AHeaderTemplate_Keeps_The_Title_As_The_Button_Name()
    {
        var cut = RenderTable(b =>
        {
            b.AddAttribute(1, "Title", "Name");
            b.AddAttribute(2, "Sortable", true);
            b.AddAttribute(3, "HeaderTemplate",
                (RenderFragment)(hb => hb.AddContent(0, "filter ui")));
        });

        cut.Find("button.tm-th-sort").GetAttribute("aria-label").Should().Be("Name",
            "templatovaná hlavička nemá viditelný text — jméno jí dává Title");
    }

    [Fact]
    public void AHeaderTemplate_Without_A_Title_Falls_Back_To_The_Localized_Name()
    {
        var cut = RenderTable(b =>
        {
            b.AddAttribute(1, "PropertyName", "Name");
            b.AddAttribute(2, "Sortable", true);
            b.AddAttribute(3, "HeaderTemplate",
                (RenderFragment)(hb => hb.AddContent(0, "filter ui")));
        });

        cut.Find("button.tm-th-sort").GetAttribute("aria-label").Should().Be("Sort ascending",
            "bez Title je posledním zdrojem jména lokalizovaná akce, ne prázdný atribut");
    }

    [Fact]
    public void SortLabel_Overrides_The_Derived_Name()
    {
        var cut = RenderTable(b =>
        {
            b.AddAttribute(1, "Title", "Name");
            b.AddAttribute(2, "PropertyName", "Name");
            b.AddAttribute(3, "Sortable", true);
            b.AddAttribute(4, "SortLabel", "Sort by last name");
        });

        cut.Find("button.tm-th-sort").GetAttribute("aria-label").Should().Be("Sort by last name",
            "SortLabel je explicitní slib consumerovi — má přednost před Title");
    }

    [Fact]
    public void SortLabel_Also_Wins_When_The_Title_Is_Empty()
    {
        var cut = RenderTable(b =>
        {
            b.AddAttribute(1, "PropertyName", "Name");
            b.AddAttribute(2, "Sortable", true);
            b.AddAttribute(3, "SortLabel", "Sort people");
        });

        cut.Find("button.tm-th-sort").GetAttribute("aria-label").Should().Be("Sort people");
    }

    [Fact]
    public void Every_Sortable_Column_Gets_A_Name_Not_Just_The_First()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<Person>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "Sortable", true);
                b.AddAttribute(3, "Field", (Func<Person, object?>)(x => x.Name));
                b.CloseComponent();

                b.OpenComponent<TmDataTableColumn<Person>>(4);
                b.AddAttribute(5, "PropertyName", "Name");
                b.AddAttribute(6, "Sortable", true);
                b.AddAttribute(7, "Field", (Func<Person, object?>)(x => x.Name));
                b.CloseComponent();
            });
        });

        var buttons = cut.FindAll("button.tm-th-sort");
        buttons.Should().HaveCount(2);
        buttons.Should().AllSatisfy(button => button.GetAttribute("aria-label").Should().NotBeNullOrEmpty(
            "záruka platí pro každý sort button, ne jen pro ten, který se testuje"));
    }
}
