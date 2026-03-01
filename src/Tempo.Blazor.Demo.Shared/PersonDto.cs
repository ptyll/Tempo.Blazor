namespace Tempo.Blazor.Demo.Shared;

public record PersonDto(
    string Id,
    string FirstName,
    string LastName,
    string Email,
    string Department,
    string Role,
    bool IsActive,
    DateOnly HiredAt)
{
    public string FullName => $"{FirstName} {LastName}";
}
