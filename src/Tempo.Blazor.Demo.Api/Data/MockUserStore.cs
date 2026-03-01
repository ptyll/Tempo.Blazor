using Tempo.Blazor.Demo.Shared;

namespace Tempo.Blazor.Demo.Api.Data;

/// <summary>
/// Mock data store for users with mention support.
/// </summary>
public class MockUserStore
{
    private static readonly string[] FirstNames =
    [
        "Jan", "Petr", "Martin", "Tomáš", "Pavel", "Jaroslav", "Miroslav", "Zdeněk", "Václav", "Michal",
        "Karel", "František", "Josef", "Jiří", "Lukáš", "David", "Ondřej", "Filip", "Adam", "Matěj",
        "Jana", "Marie", "Eva", "Hana", "Anna", "Lenka", "Kateřina", "Věra", "Alena", "Jaroslava",
        "Lucie", "Martina", "Petra", "Veronika", "Michaela", "Monika", "Markéta", "Helena", "Zdeňka", "Ivana"
    ];

    private static readonly string[] LastNames =
    [
        "Novák", "Svoboda", "Novotný", "Dvořák", "Černý", "Procházka", "Kučera", "Veselý", "Horák", "Němec",
        "Marek", "Pospíšil", "Hájek", "Král", "Jelínek", "Růžička", "Beneš", "Fiala", "Sedláček", "Doležal",
        "Zeman", "Kolář", "Krejčí", "Navrátil", "Čermák", "Urban", "Vaněk", "Blažek", "Kříž", "Kopecký"
    ];

    private static readonly string[] Departments =
    [
        "Vývoj", "Prodej", "HR", "Finance", "Marketing", "Podpora", "Produkt", "Design", "IT", "Právo"
    ];

    private static readonly string?[] Avatars =
    [
        "https://api.dicebear.com/7.x/avataaars/svg?seed=1",
        "https://api.dicebear.com/7.x/avataaars/svg?seed=2",
        "https://api.dicebear.com/7.x/avataaars/svg?seed=3",
        "https://api.dicebear.com/7.x/avataaars/svg?seed=4",
        "https://api.dicebear.com/7.x/avataaars/svg?seed=5",
        "https://api.dicebear.com/7.x/avataaars/svg?seed=6",
        "https://api.dicebear.com/7.x/avataaars/svg?seed=7",
        "https://api.dicebear.com/7.x/avataaars/svg?seed=8",
        null, null // Some users without avatars
    ];

    public List<UserDto> Users { get; }

    public MockUserStore()
    {
        var random = new Random(42); // Fixed seed for consistent data
        
        Users = Enumerable.Range(1, 50)
            .Select(i =>
            {
                var firstName = FirstNames[random.Next(FirstNames.Length)];
                var lastName = LastNames[random.Next(LastNames.Length)];
                var userName = $"{firstName.ToLowerInvariant()}.{lastName.ToLowerInvariant()}";
                var department = Departments[random.Next(Departments.Length)];
                var avatar = Avatars[random.Next(Avatars.Length)];
                
                return new UserDto(
                    Id: i.ToString(),
                    UserName: userName,
                    DisplayName: $"{firstName} {lastName}",
                    AvatarUrl: avatar?.Replace("seed=", $"seed={i}"),
                    Email: $"{userName}@company.cz",
                    Department: department
                );
            })
            .ToList();
    }

    /// <summary>
    /// Search users by query string.
    /// </summary>
    public IReadOnlyList<UserDto> SearchUsers(string query, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Users.Take(limit).ToList();
        }

        var normalizedQuery = query.ToLowerInvariant();
        
        return Users
            .Where(u => u.SearchText.Contains(normalizedQuery))
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Get a user by ID.
    /// </summary>
    public UserDto? GetById(string id)
    {
        return Users.FirstOrDefault(u => u.Id == id);
    }
}
