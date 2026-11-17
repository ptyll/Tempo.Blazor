using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Fáze 7 bugfix: an uncontrolled table (<c>PageSize</c> is null) with a non-positive
/// <c>DefaultPageSize</c> silently rendered an empty table and sent <c>PageSize=0</c> to the
/// provider. The strict throw contract that controlled <c>PageSize</c> has had since 2.8.12 now
/// covers <c>DefaultPageSize</c> too — it throws <see cref="ArgumentOutOfRangeException"/> during
/// initialization. No silent clamp (decision EMUSCZ-2.12-UPSTREAM-PRUNING).
/// </summary>
public class TmDataTableDefaultPageSizeContractTests : LocalizationTestBase
{
    private record ContractPerson(string Name);

    [Fact]
    public void DefaultPageSize_Zero_WithUncontrolledPageSize_Throws()
    {
        FluentActions.Invoking(() => Render<TmDataTable<ContractPerson>>(p =>
            p.Add(c => c.Items, new List<ContractPerson> { new("Alice") })
             .Add(c => c.DefaultPageSize, 0)))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("DefaultPageSize");
    }

    [Fact]
    public void DefaultPageSize_Negative_WithUncontrolledPageSize_Throws()
    {
        FluentActions.Invoking(() => Render<TmDataTable<ContractPerson>>(p =>
            p.Add(c => c.Items, new List<ContractPerson> { new("Alice") })
             .Add(c => c.DefaultPageSize, -5)))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DefaultPageSize_Zero_IsIgnored_WhenControlledPageSizeWins()
    {
        // A controlled PageSize is validated on its own; DefaultPageSize is never read, so the
        // render must succeed.
        var cut = Render<TmDataTable<ContractPerson>>(p =>
            p.Add(c => c.Items, new List<ContractPerson> { new("Alice") })
             .Add(c => c.PageSize, 10)
             .Add(c => c.DefaultPageSize, 0));

        cut.FindAll("tbody tr").Count.Should().Be(1);
    }
}
