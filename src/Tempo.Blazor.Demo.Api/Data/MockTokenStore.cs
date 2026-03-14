using Tempo.Blazor.Demo.Shared;

namespace Tempo.Blazor.Demo.Api.Data;

/// <summary>
/// Mock data store for template tokens/variables.
/// </summary>
public class MockTokenStore
{
    public List<TokenDto> Tokens { get; } = new()
    {
        // User tokens
        new("user.name", "Jméno uživatele", "Celé jméno přihlášeného uživatele", "Uživatel"),
        new("user.email", "E-mail uživatele", "E-mailová adresa přihlášeného uživatele", "Uživatel"),
        new("user.username", "Uživatelské jméno", "Přihlašovací jméno uživatele", "Uživatel"),
        new("user.department", "Oddělení", "Oddělení, do kterého uživatel patří", "Uživatel"),
        new("user.phone", "Telefon uživatele", "Telefonní číslo uživatele", "Uživatel"),
        new("user.position", "Pozice", "Pracovní pozice uživatele", "Uživatel"),

        // System tokens
        new("system.date", "Aktuální datum", "Dnešní datum ve formátu dd.MM.yyyy", "Systém"),
        new("system.time", "Aktuální čas", "Aktuální čas ve formátu HH:mm", "Systém"),
        new("system.datetime", "Datum a čas", "Aktuální datum a čas", "Systém"),
        new("system.year", "Rok", "Aktuální rok", "Systém"),
        new("system.appname", "Název aplikace", "Název běžící aplikace", "Systém"),
        new("system.version", "Verze", "Aktuální verze aplikace", "Systém"),

        // Document tokens
        new("doc.number", "Číslo dokumentu", "Automaticky generované číslo dokumentu", "Dokument"),
        new("doc.title", "Název dokumentu", "Název aktuálního dokumentu", "Dokument"),
        new("doc.created", "Datum vytvoření", "Datum vytvoření dokumentu", "Dokument"),
        new("doc.modified", "Datum úpravy", "Datum poslední úpravy dokumentu", "Dokument"),
        new("doc.author", "Autor", "Autor dokumentu", "Dokument"),
        new("doc.status", "Stav", "Aktuální stav dokumentu", "Dokument"),

        // Organization tokens
        new("org.name", "Název organizace", "Název společnosti/organizace", "Organizace"),
        new("org.ico", "IČO", "Identifikační číslo organizace", "Organizace"),
        new("org.address", "Adresa", "Sídlo organizace", "Organizace"),
        new("org.email", "E-mail organizace", "Kontaktní e-mail organizace", "Organizace"),
        new("org.phone", "Telefon organizace", "Kontaktní telefon organizace", "Organizace"),
    };

    /// <summary>
    /// Add a new token to the store.
    /// </summary>
    public TokenDto AddToken(string key, string displayName, string? description, string? category)
    {
        var token = new TokenDto(key, displayName, description, category);
        Tokens.Add(token);
        return token;
    }

    /// <summary>
    /// Search tokens by query string.
    /// </summary>
    public IReadOnlyList<TokenDto> SearchTokens(string query, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Tokens.Take(limit).ToList();
        }

        var normalizedQuery = query.ToLowerInvariant();

        return Tokens
            .Where(t => t.SearchText.Contains(normalizedQuery))
            .Take(limit)
            .ToList();
    }
}
