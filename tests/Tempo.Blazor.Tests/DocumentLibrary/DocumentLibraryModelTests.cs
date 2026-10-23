using System.Text.Json;
using System.Text.Json.Serialization;
using Tempo.Blazor.DocumentLibrary;

namespace Tempo.Blazor.Tests.DocumentLibrary;

/// <summary>
/// Tests for the pure-data models the document library exposes to the open dialog and
/// to MCP tooling: <see cref="DocumentLibraryEntry"/>, <see cref="DocumentLibraryFolder"/>,
/// <see cref="DocumentLibraryQuery"/> and <see cref="DocumentLibraryPage"/>.
/// </summary>
public class DocumentLibraryModelTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // ── DocumentLibraryEntry ──────────────────────────────────────────────────

    [Fact]
    public void Entry_RoundTrips_PreservingAllFields()
    {
        var entry = new DocumentLibraryEntry
        {
            Id = Guid.NewGuid(),
            Name = "Checkout flow",
            Kind = TempoDocumentKind.Wireframe,
            FolderPath = "/Designs/Mobile",
            CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2026, 6, 12, 8, 0, 0, DateTimeKind.Utc),
            Author = "pavel",
            PreviewSvg = "<svg/>"
        };

        var clone = JsonSerializer.Deserialize<DocumentLibraryEntry>(
            JsonSerializer.Serialize(entry, Json), Json)!;

        clone.Should().BeEquivalentTo(entry);
    }

    [Fact]
    public void Entry_OptionalFields_DefaultToNull()
    {
        var entry = new DocumentLibraryEntry
        {
            Id = Guid.NewGuid(),
            Name = "Root doc",
            Kind = TempoDocumentKind.Diagram
        };

        entry.FolderPath.Should().BeNull();
        entry.Author.Should().BeNull();
        entry.PreviewSvg.Should().BeNull();
    }

    [Fact]
    public void Entry_KindSerialises_AsCamelCaseString()
    {
        var json = JsonSerializer.Serialize(
            new DocumentLibraryEntry { Id = Guid.Empty, Name = "x", Kind = TempoDocumentKind.Spreadsheet },
            Json);

        json.Should().Contain("\"kind\":\"spreadsheet\"");
    }

    // ── DocumentLibraryFolder ─────────────────────────────────────────────────

    [Fact]
    public void Folder_DefaultsToEmptyChildren()
    {
        var folder = new DocumentLibraryFolder { Path = "/", Name = "Root" };

        folder.Children.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Folder_NestsChildren()
    {
        var root = new DocumentLibraryFolder
        {
            Path = "/",
            Name = "Root",
            Children =
            [
                new DocumentLibraryFolder
                {
                    Path = "/Designs",
                    Name = "Designs",
                    Children = [ new DocumentLibraryFolder { Path = "/Designs/Mobile", Name = "Mobile" } ]
                }
            ]
        };

        root.Children.Should().ContainSingle()
            .Which.Children.Should().ContainSingle()
            .Which.Path.Should().Be("/Designs/Mobile");
    }

    // ── DocumentLibraryQuery ──────────────────────────────────────────────────

    [Fact]
    public void Query_HasSensibleDefaults()
    {
        var query = new DocumentLibraryQuery { Kind = TempoDocumentKind.Wireframe };

        query.FolderPath.Should().BeNull();
        query.Search.Should().BeNull();
        query.SortField.Should().Be(DocumentLibrarySortField.Name);
        query.Descending.Should().BeFalse();
        query.Skip.Should().Be(0);
        query.Take.Should().Be(50);
        query.Scope.Should().Be(DocumentLibraryScope.Folder, "the default keeps the pre-Scope meaning of FolderPath");
    }

    [Fact]
    public void Scope_HasStableNumericValues()
    {
        ((int)DocumentLibraryScope.Folder).Should().Be(0);
        ((int)DocumentLibraryScope.Subtree).Should().Be(1);
        ((int)DocumentLibraryScope.Library).Should().Be(2);
        Enum.GetValues<DocumentLibraryScope>().Should().HaveCount(3);
    }

    // ── DocumentLibraryPage ───────────────────────────────────────────────────

    [Fact]
    public void Page_ExposesItemsAndTotalCount()
    {
        var items = new[]
        {
            new DocumentLibraryEntry { Id = Guid.NewGuid(), Name = "a", Kind = TempoDocumentKind.Wireframe }
        };

        var page = new DocumentLibraryPage { Items = items, TotalCount = 42 };

        page.Items.Should().BeEquivalentTo(items);
        page.TotalCount.Should().Be(42);
    }

    [Fact]
    public void Page_DefaultsToEmpty()
    {
        var page = new DocumentLibraryPage();

        page.Items.Should().NotBeNull().And.BeEmpty();
        page.TotalCount.Should().Be(0);
    }
}
