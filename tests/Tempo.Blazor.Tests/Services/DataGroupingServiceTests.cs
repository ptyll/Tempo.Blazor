using FluentAssertions;
using Tempo.Blazor.Models;
using Tempo.Blazor.Services;

namespace Tempo.Blazor.Tests.Services;

public class DataGroupingServiceTests
{
    private record Employee(string Name, string Department, string Status, decimal Salary);

    private static readonly List<Employee> TestData =
    [
        new("Alice", "Engineering", "Active", 90000m),
        new("Bob", "Engineering", "Active", 85000m),
        new("Charlie", "Engineering", "Inactive", 70000m),
        new("Diana", "Marketing", "Active", 75000m),
        new("Eve", "Marketing", "Inactive", 65000m),
        new("Frank", "Sales", "Active", 80000m),
    ];

    // --- GroupItems tests ---

    [Fact]
    public void GroupItems_SingleLevel_GroupsByKey()
    {
        var levels = new List<GroupingLevel<Employee>>
        {
            new("Department", e => e.Department)
        };

        var groups = DataGroupingService.GroupItems(TestData, levels);

        groups.Should().HaveCount(3);
        groups.Select(g => g.Key).Should().Contain("Engineering");
        groups.Select(g => g.Key).Should().Contain("Marketing");
        groups.Select(g => g.Key).Should().Contain("Sales");
    }

    [Fact]
    public void GroupItems_SingleLevel_SetsCorrectCount()
    {
        var levels = new List<GroupingLevel<Employee>>
        {
            new("Department", e => e.Department)
        };

        var groups = DataGroupingService.GroupItems(TestData, levels);

        var engineering = groups.First(g => (string)g.Key! == "Engineering");
        engineering.Count.Should().Be(3);
        engineering.Items.Should().HaveCount(3);
    }

    [Fact]
    public void GroupItems_SingleLevel_SetsFieldName()
    {
        var levels = new List<GroupingLevel<Employee>>
        {
            new("Department", e => e.Department)
        };

        var groups = DataGroupingService.GroupItems(TestData, levels);

        groups.Should().AllSatisfy(g => g.FieldName.Should().Be("Department"));
    }

    [Fact]
    public void GroupItems_MultiLevel_CreatesNestedGroups()
    {
        var levels = new List<GroupingLevel<Employee>>
        {
            new("Department", e => e.Department),
            new("Status", e => e.Status)
        };

        var groups = DataGroupingService.GroupItems(TestData, levels);

        groups.Should().HaveCount(3); // 3 departments

        var engineering = groups.First(g => (string)g.Key! == "Engineering");
        engineering.SubGroups.Should().HaveCount(2); // Active + Inactive
        engineering.Items.Should().BeEmpty(); // Items are in sub-groups

        var engActive = engineering.SubGroups.First(g => (string)g.Key! == "Active");
        engActive.Items.Should().HaveCount(2);
        engActive.Count.Should().Be(2);
    }

    [Fact]
    public void GroupItems_EmptyCollection_ReturnsEmpty()
    {
        var levels = new List<GroupingLevel<Employee>>
        {
            new("Department", e => e.Department)
        };

        var groups = DataGroupingService.GroupItems(Array.Empty<Employee>(), levels);

        groups.Should().BeEmpty();
    }

    [Fact]
    public void GroupItems_NullKey_GroupsNullTogether()
    {
        var data = new List<Employee>
        {
            new("Alice", "Engineering", "Active", 90000m),
            new("Bob", null!, "Active", 85000m),
            new("Charlie", null!, "Inactive", 70000m),
        };

        var levels = new List<GroupingLevel<Employee>>
        {
            new("Department", e => e.Department)
        };

        var groups = DataGroupingService.GroupItems(data, levels);

        groups.Should().HaveCount(2);
        var nullGroup = groups.First(g => g.Key is null);
        nullGroup.Count.Should().Be(2);
    }

    [Fact]
    public void GroupItems_NoLevels_ReturnsSingleGroupWithAllItems()
    {
        var groups = DataGroupingService.GroupItems(TestData, []);

        groups.Should().BeEmpty();
    }

    [Fact]
    public void GroupItems_DisplayFormatter_UsedForDisplayValue()
    {
        var levels = new List<GroupingLevel<Employee>>
        {
            new("Department", e => e.Department, DisplayFormatter: v => $"Dept: {v}")
        };

        var groups = DataGroupingService.GroupItems(TestData, levels);

        var engineering = groups.First(g => (string)g.Key! == "Engineering");
        engineering.DisplayValue.Should().Be("Dept: Engineering");
    }

    [Fact]
    public void GroupItems_NoDisplayFormatter_UsesToString()
    {
        var levels = new List<GroupingLevel<Employee>>
        {
            new("Department", e => e.Department)
        };

        var groups = DataGroupingService.GroupItems(TestData, levels);

        var engineering = groups.First(g => (string)g.Key! == "Engineering");
        engineering.DisplayValue.Should().Be("Engineering");
    }

    // --- ComputeAggregate tests ---

    [Fact]
    public void ComputeAggregate_Sum_CalculatesCorrectly()
    {
        Func<Employee, object?> accessor = e => e.Salary;

        var result = DataGroupingService.ComputeAggregate(
            TestData, accessor, [AggregateType.Sum]);

        result.Sum.Should().Be(465000m);
    }

    [Fact]
    public void ComputeAggregate_Average_CalculatesCorrectly()
    {
        Func<Employee, object?> accessor = e => e.Salary;

        var result = DataGroupingService.ComputeAggregate(
            TestData, accessor, [AggregateType.Average]);

        result.Average.Should().Be(77500m);
    }

    [Fact]
    public void ComputeAggregate_MinMax_CalculatesCorrectly()
    {
        Func<Employee, object?> accessor = e => e.Salary;

        var result = DataGroupingService.ComputeAggregate(
            TestData, accessor, [AggregateType.Min, AggregateType.Max]);

        result.Min.Should().Be(65000m);
        result.Max.Should().Be(90000m);
    }

    [Fact]
    public void ComputeAggregate_Count_ReturnsItemCount()
    {
        Func<Employee, object?> accessor = e => e.Salary;

        var result = DataGroupingService.ComputeAggregate(
            TestData, accessor, [AggregateType.Count]);

        result.Count.Should().Be(6);
    }

    [Fact]
    public void ComputeAggregate_NonNumericField_ReturnsNullForSumAvg()
    {
        Func<Employee, object?> accessor = e => e.Name;

        var result = DataGroupingService.ComputeAggregate(
            TestData, accessor, [AggregateType.Sum, AggregateType.Average]);

        result.Sum.Should().BeNull();
        result.Average.Should().BeNull();
        result.Count.Should().Be(6);
    }

    [Fact]
    public void ComputeAggregate_EmptyCollection_ReturnsZeroCount()
    {
        Func<Employee, object?> accessor = e => e.Salary;

        var result = DataGroupingService.ComputeAggregate(
            Array.Empty<Employee>(), accessor, [AggregateType.Sum, AggregateType.Count]);

        result.Count.Should().Be(0);
        result.Sum.Should().Be(0m);
    }

    [Fact]
    public void ComputeAggregate_AllTypes_ComputesAll()
    {
        Func<Employee, object?> accessor = e => e.Salary;

        var result = DataGroupingService.ComputeAggregate(
            TestData, accessor,
            [AggregateType.Count, AggregateType.Sum, AggregateType.Average,
             AggregateType.Min, AggregateType.Max]);

        result.Count.Should().Be(6);
        result.Sum.Should().Be(465000m);
        result.Average.Should().Be(77500m);
        result.Min.Should().Be(65000m);
        result.Max.Should().Be(90000m);
    }

    // --- GroupItems with Aggregations ---

    [Fact]
    public void GroupItems_WithAggregateAccessors_ComputesAggregations()
    {
        var levels = new List<GroupingLevel<Employee>>
        {
            new("Department", e => e.Department,
                AggregateAccessors: new Dictionary<string, Func<Employee, object?>>
                {
                    ["Salary"] = e => e.Salary
                },
                AggregateTypes: [AggregateType.Sum, AggregateType.Average])
        };

        var groups = DataGroupingService.GroupItems(TestData, levels);

        var engineering = groups.First(g => (string)g.Key! == "Engineering");
        engineering.Aggregations.Should().ContainKey("Salary");
        engineering.Aggregations["Salary"].Sum.Should().Be(245000m);
        engineering.Aggregations["Salary"].Average.Should().BeApproximately(81666.67m, 0.01m);
    }

    // --- Integer and double support ---

    [Fact]
    public void ComputeAggregate_IntegerValues_CalculatesCorrectly()
    {
        var data = new[] { ("A", 10), ("B", 20), ("C", 30) };
        Func<(string, int), object?> accessor = x => x.Item2;

        var result = DataGroupingService.ComputeAggregate(
            data, accessor, [AggregateType.Sum, AggregateType.Average]);

        result.Sum.Should().Be(60m);
        result.Average.Should().Be(20m);
    }
}
