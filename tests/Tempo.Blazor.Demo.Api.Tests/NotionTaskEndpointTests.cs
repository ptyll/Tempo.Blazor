using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Demo.Api.Data;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Demo.Api.Tests;

public class NotionTaskEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public NotionTaskEndpointTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task QueryTasks_ReturnsTodoBlocksAcrossSeededPages()
    {
        await SeedActionItemsAsync();

        var response = await _client.PostAsJsonAsync("/api/notion/tasks/query", new TmWorkItemQuery
        {
            IncludeCompleted = true,
            Skip = 0,
            Take = 20
        });

        response.EnsureSuccessStatusCode();
        var tasks = await response.Content.ReadFromJsonAsync<Tempo.Blazor.Models.PagedResult<TmWorkItem>>();

        Assert.NotNull(tasks);
        Assert.True(tasks.TotalCount >= 4);
        Assert.Contains(tasks.Items, task => task.Title == "Overdue task with an owner"
            && task.Assignees.Any(a => a.Id == "alice")
            && task.OriginPageTitle == "CF3 Action Items");
        Assert.Contains(tasks.Items, task => task.Title == "Completed historical action item"
            && task.IsCompleted);
    }

    [Fact]
    public async Task SetCompleted_UpdatesUnderlyingTodoBlock()
    {
        await SeedActionItemsAsync();

        var taskId = "cf300000-0000-0000-0000-000000000002";
        var updateResponse = await _client.PutAsJsonAsync($"/api/notion/tasks/{taskId}/completed", new { completed = true });
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var tasks = await (await _client.PostAsJsonAsync("/api/notion/tasks/query", new TmWorkItemQuery
        {
            IncludeCompleted = true,
            Skip = 0,
            Take = 20
        })).Content.ReadFromJsonAsync<Tempo.Blazor.Models.PagedResult<TmWorkItem>>();

        Assert.NotNull(tasks);
        Assert.Contains(tasks.Items, task => task.Id == taskId && task.IsCompleted);
    }

    [Fact]
    public async Task TaskProvider_IgnoresTodoBlocksFromHiddenPages()
    {
        var pageStore = new MockNotionDataStore();
        var blockStore = new MockNotionBlockStore();
        pageStore.SeedE2ESidebarEmptyNavigation();

        await blockStore.CreateBlockAsync(MockNotionDataStore.Page1Id.ToString("D"), new PageBlock
        {
            Type = BlockType.TodoItem,
            Content = new TodoBlockContent { Html = "Hidden navigation task", IsChecked = false }
        }, null);

        var provider = new DemoNotionTaskProvider(pageStore, blockStore, new DemoNotionAggregateStore(pageStore, blockStore));
        var result = await provider.SearchAsync(new TmWorkItemQuery
        {
            IncludeCompleted = true,
            Take = 100
        });

        Assert.DoesNotContain(result.Items, task => task.Title == "Hidden navigation task");
        Assert.DoesNotContain(result.Items, task => Guid.TryParse(task.OriginPageTitle, out _));
    }

    private async Task SeedActionItemsAsync()
    {
        var response = await _client.PostAsync("/api/notion/e2e/seed/action-items", null);
        response.EnsureSuccessStatusCode();
    }
}
