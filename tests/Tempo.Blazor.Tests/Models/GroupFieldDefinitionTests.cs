using FluentAssertions;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.Models;

public class GroupFieldDefinitionTests
{
    private record Employee(string Name, string Department, decimal Salary);

    [Fact]
    public void GroupFieldDefinition_FieldAccessor_ExtractsValue()
    {
        var definition = new GroupFieldDefinition<Employee>
        {
            FieldName = "Department",
            Label = "Department",
            FieldAccessor = e => e.Department
        };

        var employee = new Employee("Alice", "Engineering", 80000m);
        var value = definition.FieldAccessor(employee);

        value.Should().Be("Engineering");
    }

    [Fact]
    public void GroupFieldDefinition_DisplayFormatter_FormatsValue()
    {
        var definition = new GroupFieldDefinition<Employee>
        {
            FieldName = "Department",
            Label = "Department",
            FieldAccessor = e => e.Department,
            DisplayFormatter = v => $"Dept: {v}"
        };

        var result = definition.DisplayFormatter!("Engineering");

        result.Should().Be("Dept: Engineering");
    }

    [Fact]
    public void GroupFieldDefinition_Aggregations_DefaultsToCount()
    {
        var definition = new GroupFieldDefinition<Employee>
        {
            FieldName = "Department",
            Label = "Department",
            FieldAccessor = e => e.Department
        };

        definition.Aggregations.Should().HaveCount(1);
        definition.Aggregations[0].Should().Be(AggregateType.Count);
    }

    [Fact]
    public void GroupFieldDefinition_Aggregations_CanBeCustomized()
    {
        var definition = new GroupFieldDefinition<Employee>
        {
            FieldName = "Salary",
            Label = "Salary",
            FieldAccessor = e => e.Salary,
            Aggregations = [AggregateType.Sum, AggregateType.Average, AggregateType.Min, AggregateType.Max]
        };

        definition.Aggregations.Should().HaveCount(4);
        definition.Aggregations.Should().Contain(AggregateType.Sum);
        definition.Aggregations.Should().Contain(AggregateType.Average);
    }

    [Fact]
    public void GroupFieldDefinition_DefaultValues_AreCorrect()
    {
        var definition = new GroupFieldDefinition<Employee>();

        definition.FieldName.Should().BeEmpty();
        definition.Label.Should().BeEmpty();
        definition.DisplayFormatter.Should().BeNull();
    }

    [Fact]
    public void AggregateType_HasExpectedValues()
    {
        Enum.GetValues<AggregateType>().Should().Contain(AggregateType.Count);
        Enum.GetValues<AggregateType>().Should().Contain(AggregateType.Sum);
        Enum.GetValues<AggregateType>().Should().Contain(AggregateType.Average);
        Enum.GetValues<AggregateType>().Should().Contain(AggregateType.Min);
        Enum.GetValues<AggregateType>().Should().Contain(AggregateType.Max);
    }
}
