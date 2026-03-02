using FluentAssertions;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.Models;

public class DataGroupTests
{
    [Fact]
    public void DataGroup_Items_DefaultsToEmpty()
    {
        var group = new DataGroup<string>();

        group.Items.Should().NotBeNull();
        group.Items.Should().BeEmpty();
    }

    [Fact]
    public void DataGroup_SubGroups_DefaultsToEmpty()
    {
        var group = new DataGroup<string>();

        group.SubGroups.Should().NotBeNull();
        group.SubGroups.Should().BeEmpty();
    }

    [Fact]
    public void DataGroup_Aggregations_DefaultsToEmpty()
    {
        var group = new DataGroup<string>();

        group.Aggregations.Should().NotBeNull();
        group.Aggregations.Should().BeEmpty();
    }

    [Fact]
    public void DataGroup_SubGroups_SupportsMultiLevel()
    {
        var group = new DataGroup<string>
        {
            FieldName = "Department",
            Key = "Engineering",
            DisplayValue = "Engineering",
            Count = 5,
            SubGroups =
            [
                new DataGroup<string>
                {
                    FieldName = "Status",
                    Key = "Active",
                    DisplayValue = "Active",
                    Count = 3,
                    Items = ["Alice", "Bob", "Charlie"]
                },
                new DataGroup<string>
                {
                    FieldName = "Status",
                    Key = "Inactive",
                    DisplayValue = "Inactive",
                    Count = 2,
                    Items = ["Dave", "Eve"]
                }
            ]
        };

        group.SubGroups.Should().HaveCount(2);
        group.SubGroups[0].Items.Should().HaveCount(3);
        group.SubGroups[1].Items.Should().HaveCount(2);
    }

    [Fact]
    public void DataGroup_Properties_CanBeSet()
    {
        var group = new DataGroup<int>
        {
            FieldName = "Category",
            Key = "Electronics",
            DisplayValue = "Electronics",
            Count = 42,
            Items = [1, 2, 3]
        };

        group.FieldName.Should().Be("Category");
        group.Key.Should().Be("Electronics");
        group.DisplayValue.Should().Be("Electronics");
        group.Count.Should().Be(42);
        group.Items.Should().HaveCount(3);
    }

    [Fact]
    public void DataGroup_Aggregations_CanContainValues()
    {
        var group = new DataGroup<string>
        {
            Aggregations = new Dictionary<string, AggregateValue>
            {
                ["Price"] = new AggregateValue(Sum: 1500m, Average: 300m, Min: 100m, Max: 500m, Count: 5),
                ["Quantity"] = new AggregateValue(Sum: 50m, Average: 10m, Min: 2m, Max: 20m, Count: 5)
            }
        };

        group.Aggregations.Should().HaveCount(2);
        group.Aggregations["Price"].Sum.Should().Be(1500m);
        group.Aggregations["Price"].Average.Should().Be(300m);
        group.Aggregations["Quantity"].Min.Should().Be(2m);
    }

    [Fact]
    public void AggregateValue_Record_EqualityWorks()
    {
        var a = new AggregateValue(Sum: 100m, Average: 50m, Min: 10m, Max: 90m, Count: 2);
        var b = new AggregateValue(Sum: 100m, Average: 50m, Min: 10m, Max: 90m, Count: 2);
        var c = new AggregateValue(Sum: 200m, Average: 50m, Min: 10m, Max: 90m, Count: 2);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void AggregateValue_NullableFields_AllowNull()
    {
        var value = new AggregateValue(Sum: null, Average: null, Min: null, Max: null, Count: 5);

        value.Sum.Should().BeNull();
        value.Average.Should().BeNull();
        value.Count.Should().Be(5);
    }
}
