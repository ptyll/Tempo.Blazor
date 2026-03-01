using Tempo.Blazor.Demo.Shared;

namespace Tempo.Blazor.Demo.Api.Data;

public class MockPersonStore
{
    private static readonly string[] FirstNames =
        ["Alice", "Bob", "Carol", "David", "Eva", "Frank", "Grace", "Hana", "Ivan", "Jana",
         "Karel", "Lucie", "Martin", "Nina", "Ondřej", "Petra", "Radek", "Simona", "Tomáš", "Věra"];

    private static readonly string[] LastNames =
        ["Novák", "Svoboda", "Novotná", "Dvořák", "Černá", "Procházka", "Kučera", "Veselý", "Blažek", "Horák",
         "Šťastný", "Králová", "Fiala", "Říha", "Malý", "Doležal", "Sedláček", "Zeman", "Pokorný", "Urban"];

    private static readonly string[] Departments =
        ["Engineering", "Sales", "HR", "Finance", "Marketing", "Support"];

    private static readonly string[] Roles =
        ["Junior", "Medior", "Senior", "Lead", "Manager", "Director"];

    public List<PersonDto> Persons { get; } = Enumerable.Range(1, 200)
        .Select(i => new PersonDto(
            Id: i.ToString(),
            FirstName: FirstNames[i % FirstNames.Length],
            LastName: LastNames[i % LastNames.Length],
            Email: $"user{i}@example.com",
            Department: Departments[i % Departments.Length],
            Role: Roles[i % Roles.Length],
            IsActive: i % 7 != 0,
            HiredAt: DateOnly.FromDateTime(DateTime.Today.AddDays(-(i * 17)))
        )).ToList();
}
