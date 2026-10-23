using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Tempo.Blazor.Demo.Shared;
using Tempo.Blazor.DocumentLibrary;

namespace Tempo.Blazor.Demo.Api.Tests;

/// <summary>Integration tests for the document-library REST surface.</summary>
public class DocumentLibraryEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public DocumentLibraryEndpointTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    private async Task<DocumentLibraryMetadataDto> CreateAsync(string name, string folder = "/")
    {
        var resp = await _client.PostAsJsonAsync("/api/document-library/wireframe/documents",
            new DocumentLibraryCreateRequest { Name = name, FolderPath = folder, PayloadJson = "{\"v\":1}" });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<DocumentLibraryMetadataDto>())!;
    }

    [Fact]
    public async Task Create_Then_GetPayload_RoundTrips()
    {
        var meta = await CreateAsync("Endpoint home");

        var payload = await _client.GetStringAsync(
            $"/api/document-library/wireframe/documents/{meta.Id}/payload");

        payload.Should().Contain("\"v\":1");
    }

    [Fact]
    public async Task Browse_ReturnsCreatedDocument()
    {
        var folder = "/EpTest-" + Guid.NewGuid().ToString("N")[..8];
        await _client.PostAsJsonAsync("/api/document-library/wireframe/folders",
            new DocumentLibraryCreateFolderRequest { ParentPath = "/", Name = folder.TrimStart('/') });
        await CreateAsync("Browsable", folder);

        var page = await _client.GetFromJsonAsync<DocumentLibraryPage>(
            $"/api/document-library/wireframe/browse?folderPath={Uri.EscapeDataString(folder)}");

        page!.Items.Should().ContainSingle(i => i.Name == "Browsable");
    }

    [Fact]
    public async Task Browse_ScopeParameter_WidensTheQuery_AndDefaultsToFolder()
    {
        var parent = "/EpScope-" + Guid.NewGuid().ToString("N")[..8];
        await _client.PostAsJsonAsync("/api/document-library/wireframe/folders",
            new DocumentLibraryCreateFolderRequest { ParentPath = "/", Name = parent.TrimStart('/') });
        await _client.PostAsJsonAsync("/api/document-library/wireframe/folders",
            new DocumentLibraryCreateFolderRequest { ParentPath = parent, Name = "Sub" });
        await CreateAsync("Scope parent doc", parent);
        await CreateAsync("Scope child doc", parent + "/Sub");
        var escaped = Uri.EscapeDataString(parent);

        var folder = await _client.GetFromJsonAsync<DocumentLibraryPage>(
            $"/api/document-library/wireframe/browse?folderPath={escaped}");
        var subtree = await _client.GetFromJsonAsync<DocumentLibraryPage>(
            $"/api/document-library/wireframe/browse?folderPath={escaped}&scope=Subtree");
        var library = await _client.GetFromJsonAsync<DocumentLibraryPage>(
            "/api/document-library/wireframe/browse?scope=Library&search=Scope%20child");

        folder!.Items.Select(i => i.Name).Should().Equal("Scope parent doc");
        subtree!.Items.Select(i => i.Name).Should().BeEquivalentTo(["Scope parent doc", "Scope child doc"]);
        library!.Items.Should().ContainSingle(i => i.Name == "Scope child doc");
    }

    [Theory]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("Everything")]
    public async Task Browse_UndefinedScope_IsABadRequest(string scope)
    {
        var resp = await _client.GetAsync($"/api/document-library/wireframe/browse?scope={scope}");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "numeric values outside DocumentLibraryScope would otherwise bind and silently behave like Folder");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("subtree")]
    public async Task Browse_DefinedScope_IsAccepted(string scope)
    {
        var resp = await _client.GetAsync($"/api/document-library/wireframe/browse?scope={scope}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tree_IncludesCreatedFolder()
    {
        var name = "EpTree-" + Guid.NewGuid().ToString("N")[..8];
        await _client.PostAsJsonAsync("/api/document-library/wireframe/folders",
            new DocumentLibraryCreateFolderRequest { ParentPath = "/", Name = name });

        var tree = await _client.GetFromJsonAsync<DocumentLibraryFolder>(
            "/api/document-library/wireframe/tree");

        tree!.Children.Should().Contain(c => c.Path == "/" + name);
    }

    [Fact]
    public async Task CreateFolder_Duplicate_ReturnsConflict()
    {
        var name = "EpDup-" + Guid.NewGuid().ToString("N")[..8];
        await _client.PostAsJsonAsync("/api/document-library/wireframe/folders",
            new DocumentLibraryCreateFolderRequest { ParentPath = "/", Name = name });

        var second = await _client.PostAsJsonAsync("/api/document-library/wireframe/folders",
            new DocumentLibraryCreateFolderRequest { ParentPath = "/", Name = name });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SavePayload_WithStaleExpectedModifiedAt_ReturnsConflict()
    {
        var meta = await CreateAsync("EpConflict");

        var resp = await _client.PutAsJsonAsync(
            $"/api/document-library/wireframe/documents/{meta.Id}/payload",
            new DocumentLibrarySaveRequest
            {
                PayloadJson = "{\"v\":2}",
                ExpectedModifiedAt = meta.ModifiedAt.AddMinutes(-10)
            });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RenameDocument_UpdatesName()
    {
        var folder = "/EpRen-" + Guid.NewGuid().ToString("N")[..8];
        await _client.PostAsJsonAsync("/api/document-library/wireframe/folders",
            new DocumentLibraryCreateFolderRequest { ParentPath = "/", Name = folder.TrimStart('/') });
        var meta = await CreateAsync("Before", folder);

        var resp = await _client.PutAsJsonAsync(
            $"/api/document-library/wireframe/documents/{meta.Id}/rename",
            new DocumentLibraryRenameDocumentRequest { NewName = "After" });
        resp.EnsureSuccessStatusCode();

        var page = await _client.GetFromJsonAsync<DocumentLibraryPage>(
            $"/api/document-library/wireframe/browse?folderPath={Uri.EscapeDataString(folder)}");
        page!.Items.Should().ContainSingle().Which.Name.Should().Be("After");
    }

    [Fact]
    public async Task DeleteDocuments_RemovesThem()
    {
        var folder = "/EpDel-" + Guid.NewGuid().ToString("N")[..8];
        await _client.PostAsJsonAsync("/api/document-library/wireframe/folders",
            new DocumentLibraryCreateFolderRequest { ParentPath = "/", Name = folder.TrimStart('/') });
        var meta = await CreateAsync("Doomed", folder);

        var resp = await _client.PostAsJsonAsync(
            "/api/document-library/wireframe/documents/delete",
            new DocumentLibraryDeleteDocumentsRequest { Ids = [meta.Id] });
        resp.EnsureSuccessStatusCode();

        var page = await _client.GetFromJsonAsync<DocumentLibraryPage>(
            $"/api/document-library/wireframe/browse?folderPath={Uri.EscapeDataString(folder)}");
        page!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMissingDocument_ReturnsNotFound()
    {
        var resp = await _client.GetAsync(
            $"/api/document-library/wireframe/documents/{Guid.NewGuid()}/payload");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
