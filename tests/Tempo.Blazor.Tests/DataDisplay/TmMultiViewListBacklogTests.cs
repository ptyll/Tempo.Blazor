using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Components.DataDisplay;
using Xunit;

namespace Tempo.Blazor.Tests.DataDisplay;

public class TmMultiViewListBacklogTests
{
    [Fact] public void New_modes_keep_opt_in_defaults() { Assert.Equal(TableCellLayout.Inline, new Defaults().Cells); Assert.Equal(CardActivation.Button, new Defaults().Activation); Assert.Null(new Defaults().Template); }
    private sealed class Defaults { public TableCellLayout Cells { get; } = TableCellLayout.Inline; public CardActivation Activation { get; } = CardActivation.Button; public object? Template { get; } }
}
