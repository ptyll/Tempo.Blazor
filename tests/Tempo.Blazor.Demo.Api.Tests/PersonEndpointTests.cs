using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Tempo.Blazor.Demo.Shared;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.Api.Tests;

public class PersonEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public PersonEndpointTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task GetPersons_ReturnsPagedResult()
    {
        var result = await _client.GetFromJsonAsync<PagedResult<PersonDto>>(
            "/api/persons?page=1&pageSize=10&sortDescending=false");

        Assert.NotNull(result);
        Assert.Equal(10, result.Items.Count);
        Assert.Equal(200, result.TotalCount);
        Assert.Equal(1, result.Page);
    }

    [Fact]
    public async Task GetPersons_WithSort_ReturnsSorted()
    {
        var result = await _client.GetFromJsonAsync<PagedResult<PersonDto>>(
            "/api/persons?page=1&pageSize=200&sortColumn=LastName&sortDescending=false");

        Assert.NotNull(result);
        var names = result.Items.Select(p => p.LastName).ToList();
        Assert.Equal(names.OrderBy(n => n), names);
    }

    [Fact]
    public async Task GetPersons_WithSearch_FiltersResults()
    {
        var result = await _client.GetFromJsonAsync<PagedResult<PersonDto>>(
            "/api/persons?page=1&pageSize=200&sortDescending=false&searchText=Engineering");

        Assert.NotNull(result);
        Assert.True(result.Items.Count > 0);
        Assert.All(result.Items, p =>
            Assert.Contains("Engineering", p.Department, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetPersons_WithPagination_ReturnsCorrectPage()
    {
        var page1 = await _client.GetFromJsonAsync<PagedResult<PersonDto>>(
            "/api/persons?page=1&pageSize=5&sortDescending=false");
        var page2 = await _client.GetFromJsonAsync<PagedResult<PersonDto>>(
            "/api/persons?page=2&pageSize=5&sortDescending=false");

        Assert.NotNull(page1);
        Assert.NotNull(page2);
        Assert.Equal(5, page1.Items.Count);
        Assert.Equal(5, page2.Items.Count);
        Assert.NotEqual(page1.Items[0].Id, page2.Items[0].Id);
    }

    [Fact]
    public async Task GetPerson_ById_ReturnsCorrectPerson()
    {
        var person = await _client.GetFromJsonAsync<PersonDto>("/api/persons/1");

        Assert.NotNull(person);
        Assert.Equal("1", person.Id);
    }

    [Fact]
    public async Task GetPerson_NotFound_Returns404()
    {
        var response = await _client.GetAsync("/api/persons/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDepartments_ReturnsList()
    {
        var departments = await _client.GetFromJsonAsync<List<string>>("/api/persons/departments");

        Assert.NotNull(departments);
        Assert.Contains("Engineering", departments);
        Assert.Contains("Sales", departments);
    }
}
