using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.Api.Data;

public class MockDropdownStore
{
    public List<DropdownItem<string>> Countries { get; } =
    [
        new("af", "Afghanistan"), new("al", "Albania"), new("dz", "Algeria"),
        new("ar", "Argentina"), new("au", "Australia"), new("at", "Austria"),
        new("be", "Belgium"), new("br", "Brazil"), new("bg", "Bulgaria"),
        new("ca", "Canada"), new("cl", "Chile"), new("cn", "China"),
        new("co", "Colombia"), new("hr", "Croatia"), new("cz", "Czech Republic"),
        new("dk", "Denmark"), new("eg", "Egypt"), new("ee", "Estonia"),
        new("fi", "Finland"), new("fr", "France"), new("de", "Germany"),
        new("gr", "Greece"), new("hu", "Hungary"), new("is", "Iceland"),
        new("in", "India"), new("id", "Indonesia"), new("ie", "Ireland"),
        new("il", "Israel"), new("it", "Italy"), new("jp", "Japan"),
        new("kr", "South Korea"), new("lv", "Latvia"), new("lt", "Lithuania"),
        new("lu", "Luxembourg"), new("mx", "Mexico"), new("nl", "Netherlands"),
        new("nz", "New Zealand"), new("no", "Norway"), new("pk", "Pakistan"),
        new("pe", "Peru"), new("ph", "Philippines"), new("pl", "Poland"),
        new("pt", "Portugal"), new("ro", "Romania"), new("ru", "Russia"),
        new("sa", "Saudi Arabia"), new("rs", "Serbia"), new("sg", "Singapore"),
        new("sk", "Slovakia"), new("si", "Slovenia"), new("za", "South Africa"),
        new("es", "Spain"), new("se", "Sweden"), new("ch", "Switzerland"),
        new("tw", "Taiwan"), new("th", "Thailand"), new("tr", "Turkey"),
        new("ua", "Ukraine"), new("ae", "United Arab Emirates"),
        new("gb", "United Kingdom"), new("us", "United States"),
    ];

    public List<DropdownItem<string>> GetUsers(MockPersonStore personStore)
    {
        return personStore.Persons
            .Take(50)
            .Select(p => new DropdownItem<string>(p.Id, p.FullName, p.Department))
            .ToList();
    }
}
