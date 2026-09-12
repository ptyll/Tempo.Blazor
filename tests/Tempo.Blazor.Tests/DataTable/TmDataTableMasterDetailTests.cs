using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>Master-detail expandable rows: expander toggle, detail template, lazy load.</summary>
public class TmDataTableMasterDetailTests : LocalizationTestBase
{
    public record Person(string Name);

    private IRenderedComponent<TmDataTable<Person>> Render(
        RenderFragment<Person>? detail = null,
        Func<Person, Task>? onLoad = null,
        Action<Person>? onRowClick = null)
        => Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, "md-test");
            p.Add(c => c.Items, new List<Person> { new("Ann") });
            if (detail is not null) p.Add(c => c.DetailTemplate, detail);
            if (onLoad is not null) p.Add(c => c.OnLoadDetail, onLoad);
            if (onRowClick is not null)
                p.Add(c => c.OnRowClick, EventCallback.Factory.Create<Person>(this, onRowClick));
            p.AddChildContent(b =>
            {
                var seq = 0;
                b.OpenComponent<TmDataTableColumn<Person>>(seq++);
                b.AddAttribute(seq++, "Title", "Name");
                b.AddAttribute(seq++, "Field", (Func<Person, object?>)(x => x.Name));
                b.CloseComponent();
            });
        });

    private static RenderFragment<Person> Detail() =>
        item => b => b.AddContent(0, $"Detail of {item.Name}");

    [Fact]
    public void NoDetailTemplate_NoExpanderColumn()
    {
        var cut = Render();

        cut.FindAll(".tm-col-expander").Should().BeEmpty();
        cut.FindAll("[data-testid='expander']").Should().BeEmpty();
    }

    [Fact]
    public void DetailTemplate_RendersExpander_CollapsedByDefault()
    {
        var cut = Render(Detail());

        cut.FindAll("[data-testid='expander']").Should().ContainSingle();
        cut.FindAll("[data-testid='row-detail']").Should().BeEmpty();
    }

    [Fact]
    public void ClickExpander_ShowsDetailRow()
    {
        var cut = Render(Detail());

        cut.Find("[data-testid='expander']").Click();

        cut.FindAll("[data-testid='row-detail']").Should().ContainSingle();
        cut.Find("[data-testid='row-detail']").TextContent.Should().Contain("Detail of Ann");
    }

    [Fact]
    public void ToggleExpander_CollapsesDetail()
    {
        var cut = Render(Detail());

        cut.Find("[data-testid='expander']").Click();
        cut.FindAll("[data-testid='row-detail']").Should().ContainSingle();

        cut.Find("[data-testid='expander']").Click();
        cut.FindAll("[data-testid='row-detail']").Should().BeEmpty();
    }

    [Fact]
    public void LazyLoad_InvokedOnceAcrossReExpands()
    {
        var loads = 0;
        var cut = Render(Detail(), _ => { loads++; return Task.CompletedTask; });

        cut.Find("[data-testid='expander']").Click();   // expand → load
        loads.Should().Be(1);

        cut.Find("[data-testid='expander']").Click();   // collapse
        cut.Find("[data-testid='expander']").Click();   // re-expand → no reload

        loads.Should().Be(1);
        cut.FindAll("[data-testid='row-detail']").Should().ContainSingle();
    }

    [Fact]
    public void EnterOnExpander_TogglesDetail_WithoutRowClick()
    {
        // The row maps Enter/Space to OnRowClick (a non-native <tr tabindex>
        // needs that emulation); the expander is a native <button> whose
        // keydown must not reach it — @onkeydown:stopPropagation makes the
        // row handler unreachable, which bUnit reports by throwing.
        var rowClicks = 0;
        var cut = Render(Detail(), onRowClick: _ => rowClicks++);

        var expander = cut.Find("[data-testid='expander']");
        var act = () => expander.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        act.Should().Throw<MissingEventHandlerException>(
            "the expander isolates its keydown from the row's Enter/Space handler");

        // The native click still toggles the detail — exactly once.
        cut.Find("[data-testid='expander']").Click();
        cut.FindAll("[data-testid='row-detail']").Should().ContainSingle();
        rowClicks.Should().Be(0);
    }
}
