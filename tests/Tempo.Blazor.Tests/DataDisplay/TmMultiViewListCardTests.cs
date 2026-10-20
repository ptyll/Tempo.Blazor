using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

public class TmMultiViewListCardTests : LocalizationTestBase
{
    private record TestItem(string Id, string Title, string? AvatarUrl = null) : IMultiViewListItem
    {
        public string? SubTitle => null;
        public IReadOnlyList<ITag>? Tags => null;
        public string? StatusLabel => null;
        public string? StatusColor => null;
        public DateTimeOffset? Date => null;
    }

    private static IReadOnlyList<TestItem> Items => [new("1", "Ada Lovelace")];

    [Fact]
    public void VirtualizedCards_UseSingleColumnGridModifier()
    {
        var cut = Render<TmMultiViewList<TestItem>>(p => p
            .Add(c => c.Items, Items)
            .Add(c => c.ViewMode, ListViewMode.Card)
            .Add(c => c.ScrollMode, DataTableScrollMode.Virtualized));

        cut.Find(".tm-mvl-card-grid").ClassList.Should().Contain("tm-mvl-card-grid--virtualized");
    }

    [Fact]
    public void None_DoesNotRenderAvatarWhenUrlIsMissing()
    {
        var cut = Render<TmMultiViewList<TestItem>>(p => p
            .Add(c => c.Items, Items)
            .Add(c => c.ViewMode, ListViewMode.Card));

        cut.FindAll(".tm-avatar-fallback").Should().BeEmpty();
        cut.FindAll(".tm-mvl-card-avatar").Should().BeEmpty();
    }

    [Fact]
    public void Initials_RendersAvatarInitialsFromTitleWhenUrlIsMissing()
    {
        var cut = Render<TmMultiViewList<TestItem>>(p => p
            .Add(c => c.Items, Items)
            .Add(c => c.ViewMode, ListViewMode.Card)
            .Add(c => c.AvatarFallback, AvatarFallback.Initials));

        cut.Find(".tm-avatar-fallback").TextContent.Should().Be("AL");
    }
}
