using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Demo.Api.Data;

public class MockNotionDataStore
{
    private readonly Dictionary<Guid, NotionPage> _pages = new();
    private readonly Dictionary<string, NotionSpaceDto> _spaces = new(StringComparer.OrdinalIgnoreCase);

    public sealed record CopyPageTreeResult(NotionPage RootPage, IReadOnlyDictionary<Guid, Guid> PageIdMap);

    public MockNotionDataStore()
    {
        InitializeMockData();
    }

    // Fixed page IDs for stable cross-references
    public static readonly Guid Page1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Page2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Page3Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Page4Id = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid Page5Id = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid Page6Id = Guid.Parse("66666666-6666-6666-6666-666666666666");
    public static readonly Guid SidebarDeepRunbookId = Guid.Parse("eb110000-0000-0000-0000-000000000001");
    public static readonly Guid SidebarDeepApiContractId = Guid.Parse("eb110000-0000-0000-0000-000000000002");
    public static readonly Guid SidebarDeepReleaseChecklistId = Guid.Parse("eb110000-0000-0000-0000-000000000003");
    public static readonly Guid SidebarDeepRiskRegisterId = Guid.Parse("eb110000-0000-0000-0000-000000000004");
    public static readonly Guid SidebarTrashLegacyDraftId = Guid.Parse("eb110000-0000-0000-0000-000000000101");
    public static readonly Guid SidebarTrashRetiredSpecId = Guid.Parse("eb110000-0000-0000-0000-000000000102");
    public static readonly Guid SidebarTrashOrphanedChildId = Guid.Parse("eb110000-0000-0000-0000-000000000103");

    private void InitializeMockData()
    {
        var now = DateTime.UtcNow;

        _pages[Page1Id] = new NotionPage
        {
            Id                 = Page1Id,
            ParentId           = null,
            Title              = "Getting Started with Notion Editor",
            Description        = "A demo page to test the Notion Editor functionality",
            SpaceId            = "getting-started",
            IconEmoji          = "📝",
            CreatedAt          = now.AddDays(-7),
            CreatedByUserId    = "alice",
            LastEditedAt       = now.AddHours(-2),
            LastEditedByUserId = "demo",
            IsFavorite         = true,
            Labels             = ["getting-started", "demo"]
        };

        _pages[Page2Id] = new NotionPage
        {
            Id                 = Page2Id,
            ParentId           = null,
            Title              = "Product Roadmap",
            Description        = "Quarterly planning and feature roadmap",
            SpaceId            = "product",
            IconEmoji          = "📌",
            CreatedAt          = now.AddDays(-14),
            CreatedByUserId    = "alice",
            LastEditedAt       = now.AddDays(-1),
            LastEditedByUserId = "alice",
            IsFavorite         = true,
            Labels             = ["product", "roadmap"]
        };

        _pages[Page3Id] = new NotionPage
        {
            Id                 = Page3Id,
            ParentId           = null,
            Title              = "Meeting Notes",
            Description        = "Weekly team meeting notes and action items",
            SpaceId            = "team",
            IconEmoji          = "🗒️",
            CreatedAt          = now.AddDays(-5),
            CreatedByUserId    = "bob",
            LastEditedAt       = now.AddHours(-1),
            LastEditedByUserId = "bob",
            IsFavorite         = false,
            Labels             = ["meeting", "team"]
        };

        _pages[Page4Id] = new NotionPage
        {
            Id                 = Page4Id,
            ParentId           = null,
            Title              = "Engineering Wiki",
            Description        = "Technical documentation and architecture guides",
            SpaceId            = "engineering",
            IconEmoji          = "💻",
            CreatedAt          = now.AddDays(-30),
            CreatedByUserId    = "charlie",
            LastEditedAt       = now.AddDays(-3),
            LastEditedByUserId = "charlie",
            IsFavorite         = false,
            Labels             = ["engineering", "wiki"]
        };

        _pages[Page5Id] = new NotionPage
        {
            Id                 = Page5Id,
            ParentId           = Page4Id,
            Title              = "Architecture Guide",
            Description        = "System architecture, patterns, and design decisions",
            SpaceId            = "engineering",
            IconEmoji          = "🏗️",
            CreatedAt          = now.AddDays(-25),
            CreatedByUserId    = "charlie",
            LastEditedAt       = now.AddDays(-5),
            LastEditedByUserId = "charlie",
            IsFavorite         = false,
            Labels             = ["engineering", "architecture"]
        };

        _pages[Page6Id] = new NotionPage
        {
            Id                 = Page6Id,
            ParentId           = Page4Id,
            Title              = "Development Setup",
            Description        = "Local environment setup and tooling guide",
            SpaceId            = "engineering",
            IconEmoji          = "🔧",
            CreatedAt          = now.AddDays(-20),
            CreatedByUserId    = "bob",
            LastEditedAt       = now.AddDays(-2),
            LastEditedByUserId = "bob",
            IsFavorite         = false,
            Labels             = ["engineering", "setup"]
        };
    }

    public async Task<INotionPage> GetPageAsync(string pageId)
    {
        if (Guid.TryParse(pageId, out var id) && _pages.TryGetValue(id, out var page))
        {
            return await Task.FromResult(page);
        }
        throw new KeyNotFoundException($"Page {pageId} not found");
    }

    public async Task<IEnumerable<INotionPage>> GetChildPagesAsync(string? parentId)
    {
        var parentGuid = parentId != null ? Guid.Parse(parentId) : (Guid?)null;
        var children = _pages.Values
            .Where(p => !p.IsDeleted)
            .Where(p => p.ParentId == parentGuid)
            .Cast<INotionPage>();
        return await Task.FromResult(children);
    }

    public async Task<IEnumerable<INotionPage>> GetFavoritesAsync()
    {
        var favorites = _pages.Values
            .Where(p => !p.IsDeleted)
            .Where(p => p.IsFavorite)
            .Cast<INotionPage>();
        return await Task.FromResult(favorites);
    }

    public async Task<IEnumerable<INotionPage>> GetRecentPagesAsync(int count)
    {
        var recent = _pages.Values
            .Where(p => !p.IsDeleted)
            .OrderByDescending(p => p.LastEditedAt)
            .Take(count)
            .Cast<INotionPage>();
        return await Task.FromResult(recent);
    }

    public async Task<IEnumerable<INotionPage>> GetTrashAsync()
    {
        var trash = _pages.Values.Where(p => p.IsDeleted).Cast<INotionPage>();
        return await Task.FromResult(trash);
    }

    public Task<IReadOnlyList<INotionPage>> GetPagesByLabelAsync(string label, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeLabel(label);
        if (string.IsNullOrWhiteSpace(normalized))
            return Task.FromResult<IReadOnlyList<INotionPage>>([]);

        var pages = _pages.Values
            .Where(page => !page.IsDeleted)
            .Where(page => page.Labels.Any(existing => string.Equals(NormalizeLabel(existing), normalized, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(page => page.Title, StringComparer.OrdinalIgnoreCase)
            .Cast<INotionPage>()
            .ToArray();

        return Task.FromResult<IReadOnlyList<INotionPage>>(pages);
    }

    public Task<IReadOnlyList<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default)
    {
        var labels = _pages.Values
            .Where(page => !page.IsDeleted)
            .SelectMany(page => page.Labels)
            .Select(NormalizeLabel)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(labels);
    }

    public Task SetPageLabelsAsync(Guid pageId, IReadOnlyList<string> labels, CancellationToken cancellationToken = default)
    {
        if (!_pages.TryGetValue(pageId, out var page))
            throw new KeyNotFoundException($"Page {pageId} not found");

        page.Labels = NormalizeLabels(labels);
        page.LastEditedAt = DateTime.UtcNow;
        page.LastEditedByUserId = "demo-user";
        _pages[pageId] = page;
        return Task.CompletedTask;
    }

    public async Task<INotionPage> CreatePageAsync(string? parentId, string title)
    {
        var parentGuid = parentId != null ? Guid.Parse(parentId) : (Guid?)null;
        var page = new NotionPage
        {
            Id = Guid.NewGuid(),
            ParentId = parentGuid,
            Title = title,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = "demo-user",
            LastEditedAt = DateTime.UtcNow,
            LastEditedByUserId = "demo-user"
        };

        _pages[page.Id] = page;
        return await Task.FromResult(page);
    }

    public async Task UpdatePageAsync(INotionPage page)
    {
        if (page is NotionPage notionPage && _pages.ContainsKey(notionPage.Id))
        {
            notionPage.LastEditedAt = DateTime.UtcNow;
            notionPage.LastEditedByUserId = "demo-user";
            _pages[notionPage.Id] = notionPage;
        }
        await Task.CompletedTask;
    }

    public async Task DeletePageAsync(string pageId)
    {
        if (Guid.TryParse(pageId, out var id) && _pages.TryGetValue(id, out var page))
        {
            page.IsDeleted = true;
            page.DeletedAt = DateTime.UtcNow;
            _pages[id] = page;
        }
        await Task.CompletedTask;
    }

    public async Task RestorePageAsync(string pageId)
    {
        if (Guid.TryParse(pageId, out var id) && _pages.TryGetValue(id, out var page))
        {
            page.IsDeleted = false;
            page.DeletedAt = null;
            _pages[id] = page;
        }
        await Task.CompletedTask;
    }

    public async Task PermanentlyDeletePageAsync(string pageId)
    {
        if (Guid.TryParse(pageId, out var id))
        {
            _pages.Remove(id);
        }
        await Task.CompletedTask;
    }

    public async Task ToggleFavoriteAsync(string pageId, bool isFavorite)
    {
        if (Guid.TryParse(pageId, out var id) && _pages.TryGetValue(id, out var page))
        {
            page.IsFavorite = isFavorite;
            _pages[id] = page;
        }
        await Task.CompletedTask;
    }

    public async Task MovePageAsync(string pageId, string? newParentId)
    {
        if (Guid.TryParse(pageId, out var id) && _pages.TryGetValue(id, out var page))
        {
            page.ParentId = newParentId != null ? Guid.Parse(newParentId) : null;
            _pages[id] = page;
        }
        await Task.CompletedTask;
    }

    public async Task<INotionPage> DuplicatePageAsync(string pageId)
    {
        if (Guid.TryParse(pageId, out var id) && _pages.TryGetValue(id, out var originalPage))
        {
            var duplicated = new NotionPage
            {
                Id = Guid.NewGuid(),
                ParentId = originalPage.ParentId,
                Title = $"{originalPage.Title} (Copy)",
                Description = originalPage.Description,
                SpaceId = originalPage.SpaceId,
                IconEmoji = originalPage.IconEmoji,
                IconImageUrl = originalPage.IconImageUrl,
                CoverImageUrl = originalPage.CoverImageUrl,
                CoverImagePositionY = originalPage.CoverImagePositionY,
                IsFullWidth = originalPage.IsFullWidth,
                IsSmallText = originalPage.IsSmallText,
                IsLocked = originalPage.IsLocked,
                Labels = originalPage.Labels.ToArray(),
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = "demo-user",
                LastEditedAt = DateTime.UtcNow,
                LastEditedByUserId = "demo-user"
            };

            _pages[duplicated.Id] = duplicated;
            return await Task.FromResult(duplicated);
        }
        throw new KeyNotFoundException($"Page {pageId} not found");
    }

    public IEnumerable<INotionPage> GetAllPages()
    {
        return _pages.Values.Cast<INotionPage>();
    }

    public Task<IReadOnlyList<INotionPage>> GetPagesInSpaceAsync(string spaceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(spaceId))
        {
            var visiblePages = _pages.Values
                .Where(page => !page.IsDeleted)
                .Cast<INotionPage>()
                .ToArray();

            return Task.FromResult<IReadOnlyList<INotionPage>>(visiblePages);
        }

        var normalized = spaceId.Trim();
        var pages = _pages.Values
            .Where(page => !page.IsDeleted)
            .Where(page =>
            {
                if (string.Equals(page.SpaceId, normalized, StringComparison.OrdinalIgnoreCase))
                    return true;

                var root = ResolveRootPage(page);
                return root is not null &&
                       (string.Equals(root.Id.ToString("D"), normalized, StringComparison.OrdinalIgnoreCase) ||
                        root.Title.Contains(normalized, StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(page => page.Title, StringComparer.OrdinalIgnoreCase)
            .Cast<INotionPage>()
            .ToArray();

        return Task.FromResult<IReadOnlyList<INotionPage>>(pages);
    }

    public Task<IReadOnlyList<NotionSpaceDto>> GetSpacesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var inferredSpaces = _pages.Values
            .Where(page => !page.IsDeleted)
            .Where(page => !string.IsNullOrWhiteSpace(page.SpaceId))
            .GroupBy(page => page.SpaceId!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var inferred = CreateSpace(group.Key, group);
                return _spaces.TryGetValue(inferred.Id, out var explicitSpace)
                    ? CopySpaceWithHomePage(explicitSpace, inferred.HomePageId)
                    : inferred;
            });

        var spaces = _spaces.Values
            .Concat(inferredSpaces)
            .GroupBy(space => space.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(space => SpaceOrder(space.Id))
            .ThenBy(space => space.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<NotionSpaceDto>>(spaces);
    }

    public async Task<NotionSpaceDto?> GetSpaceAsync(string spaceId, CancellationToken cancellationToken = default)
    {
        var spaces = await GetSpacesAsync(cancellationToken);
        return spaces.FirstOrDefault(space => string.Equals(space.Id, spaceId, StringComparison.OrdinalIgnoreCase));
    }

    public Task<NotionSpaceDto> CreateSpaceAsync(NotionSpaceDto space, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(space);

        var id = string.IsNullOrWhiteSpace(space.Id)
            ? NormalizeSpaceId(space.Name)
            : NormalizeSpaceId(space.Id);

        var created = new NotionSpaceDto
        {
            Id = id,
            Key = string.IsNullOrWhiteSpace(space.Key) ? id.ToUpperInvariant() : space.Key,
            Name = string.IsNullOrWhiteSpace(space.Name) ? ToSpaceName(id) : space.Name,
            Description = space.Description,
            IconEmoji = space.IconEmoji,
            HomePageId = space.HomePageId,
            Type = space.Type
        };

        _spaces[id] = created;
        return Task.FromResult(created);
    }

    public Task MovePageToSpaceAsync(string pageId, string spaceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Guid.TryParse(pageId, out var id) || !_pages.TryGetValue(id, out var page))
            throw new KeyNotFoundException($"Page {pageId} not found");

        page.SpaceId = NormalizeSpaceId(spaceId);
        page.LastEditedAt = DateTime.UtcNow;
        page.LastEditedByUserId = "demo";
        return Task.CompletedTask;
    }

    public void SeedE2ESpacesPage()
    {
        _pages.Clear();
        _spaces.Clear();
        SeedSpace("team");
        SeedSpace("personal");
        SeedSpace("archive");
        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "CF29 Team Launch Plan", "Team launch workspace.", "team", "alice", "Alice Morgan", "alice", "Alice Morgan");
        _pages[Page2Id] = CreateSeedPage(Page2Id, null, "CF29 Personal Notes", "Personal workspace notes.", "personal", "demo", "Demo User", "demo", "Demo User");
        _pages[Page4Id] = CreateSeedPage(Page4Id, Page1Id, "CF29 Launch Child", "Team launch child page.", "team", "alice", "Alice Morgan", "alice", "Alice Morgan");
    }

    public void SeedE2ELabelsPage()
    {
        _pages.Clear();
        _spaces.Clear();
        SeedSpace("cf6");

        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "CF6 Labels Baseline", "Label editing baseline page.", "cf6", "ada", "Ada Lovelace", "grace", "Grace Hopper");
        _pages[Page1Id].Labels =
        [
            "release",
            "ops",
            "design",
            "quality",
            "roadmap",
            "customer success",
            "governance",
            "documentation"
        ];

        _pages[Page2Id] = CreateSeedPage(Page2Id, null, "CF6 Release Companion", "Companion page used by label filter navigation.", "cf6", "ada", "Ada Lovelace", "grace", "Grace Hopper");
        _pages[Page2Id].Labels = ["release", "customer success", "team notes"];

        _pages[Page3Id] = CreateSeedPage(Page3Id, null, "CF6 Empty Labels", "Page with no labels.", "cf6", "demo", "Demo User", "demo", "Demo User");
        _pages[Page3Id].Labels = [];
    }

    public void SeedE2EContentByLabelPage()
    {
        _pages.Clear();
        _spaces.Clear();
        SeedSpace("cf7");

        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "CF7 Content by Label", "Content by label macro baseline page.", "cf7", "ada", "Ada Lovelace", "grace", "Grace Hopper");
        _pages[Page1Id].Labels = ["macro", "cf7"];

        _pages[Page2Id] = CreateSeedPage(Page2Id, null, "CF7 Alpha Release", "Release page used by configured content-by-label macro.", "cf7", "ada", "Ada Lovelace", "ada", "Ada Lovelace");
        _pages[Page2Id].Labels = ["release"];
        _pages[Page2Id].LastEditedAt = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);

        _pages[Page3Id] = CreateSeedPage(Page3Id, null, "CF7 Beta Release", "Newest release page used by max-items edge state.", "cf7", "grace", "Grace Hopper", "grace", "Grace Hopper");
        _pages[Page3Id].Labels = ["release"];
        _pages[Page3Id].LastEditedAt = new DateTime(2026, 1, 6, 10, 0, 0, DateTimeKind.Utc);

        _pages[Page4Id] = CreateSeedPage(Page4Id, null, "CF7 Deleted Release", "Deleted release page excluded from macro output.", "cf7", "demo", "Demo User", "demo", "Demo User");
        _pages[Page4Id].Labels = ["release"];
        _pages[Page4Id].IsDeleted = true;
        _pages[Page4Id].DeletedAt = new DateTime(2026, 1, 7, 10, 0, 0, DateTimeKind.Utc);
    }

    public void SeedE2EManySpacesPage()
    {
        _pages.Clear();
        _spaces.Clear();
        SeedSpace("team");
        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "CF29 Team Launch Plan", "Team launch workspace.", "team", "alice", "Alice Morgan", "alice", "Alice Morgan");

        for (var i = 1; i <= 24; i++)
        {
            var id = Guid.Parse($"cf290000-0000-0000-0001-{i:000000000000}");
            var spaceId = $"space-{i:00}";
            SeedSpace(spaceId);
            _pages[id] = CreateSeedPage(id, null, $"CF29 Space {i:00} Page", "Many spaces seed page.", spaceId, "demo", "Demo User", "demo", "Demo User");
        }
    }

    public void SeedE2ESearchPage()
    {
        _pages.Clear();
        var created = new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);

        _pages[Page1Id] = new NotionPage
        {
            Id = Page1Id,
            Title = "CF22 Knowledge Space",
            Description = "Engineering reference space",
            SpaceId = "cf22-knowledge",
            IconEmoji = "K",
            CreatedAt = created,
            CreatedByUserId = "alice",
            LastEditedAt = new DateTime(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc),
            LastEditedByUserId = "alice",
            Labels = ["engineering", "knowledge"]
        };

        _pages[Page2Id] = new NotionPage
        {
            Id = Page2Id,
            Title = "CF22 Produktová strategie",
            Description = "Produktový plán pro český trh",
            SpaceId = "cf22-product",
            IconEmoji = "P",
            CreatedAt = created.AddDays(1),
            CreatedByUserId = "bob",
            LastEditedAt = new DateTime(2026, 1, 22, 12, 0, 0, DateTimeKind.Utc),
            LastEditedByUserId = "bob",
            Labels = ["product"]
        };

        _pages[Page3Id] = new NotionPage
        {
            Id = Page3Id,
            Title = "CF22 Support Space",
            Description = "Support knowledge base",
            SpaceId = "cf22-support",
            IconEmoji = "S",
            CreatedAt = created.AddDays(2),
            CreatedByUserId = "dana",
            LastEditedAt = new DateTime(2026, 1, 23, 12, 0, 0, DateTimeKind.Utc),
            LastEditedByUserId = "dana",
            Labels = ["support"]
        };

        _pages[Page4Id] = new NotionPage
        {
            Id = Page4Id,
            ParentId = Page3Id,
            Title = "CF22 Escalation Notes",
            Description = "Customer escalation details",
            SpaceId = "cf22-support",
            IconEmoji = "E",
            CreatedAt = created.AddDays(3),
            CreatedByUserId = "dana",
            LastEditedAt = new DateTime(2026, 1, 24, 12, 0, 0, DateTimeKind.Utc),
            LastEditedByUserId = "dana",
            Labels = ["support", "customer"]
        };
    }

    public void SeedE2EBulkPages()
    {
        _pages.Clear();
        var now = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc);

        _pages[Page1Id] = new NotionPage
        {
            Id = Page1Id,
            Title = "CF24 Source Root",
            SpaceId = "cf24-source",
            CreatedAt = now,
            LastEditedAt = now,
            CreatedByUserId = "alice",
            LastEditedByUserId = "alice",
            Labels = ["bulk"]
        };

        _pages[Page2Id] = new NotionPage
        {
            Id = Page2Id,
            ParentId = Page1Id,
            Title = "CF24 Child A",
            SpaceId = "cf24-source",
            CreatedAt = now.AddMinutes(1),
            LastEditedAt = now.AddMinutes(1),
            CreatedByUserId = "alice",
            LastEditedByUserId = "alice",
            Labels = ["bulk"]
        };

        _pages[Page3Id] = new NotionPage
        {
            Id = Page3Id,
            ParentId = Page2Id,
            Title = "CF24 Grandchild A1",
            SpaceId = "cf24-source",
            CreatedAt = now.AddMinutes(2),
            LastEditedAt = now.AddMinutes(2),
            CreatedByUserId = "alice",
            LastEditedByUserId = "alice",
            Labels = ["bulk"]
        };

        _pages[Page4Id] = new NotionPage
        {
            Id = Page4Id,
            Title = "CF24 Target Space",
            SpaceId = "cf24-source",
            CreatedAt = now.AddMinutes(3),
            LastEditedAt = now.AddMinutes(3),
            CreatedByUserId = "bob",
            LastEditedByUserId = "bob",
            Labels = ["bulk-target"]
        };

        _pages[Page5Id] = new NotionPage
        {
            Id = Page5Id,
            Title = "CF24 Delete Candidate A",
            SpaceId = "cf24-source",
            CreatedAt = now.AddMinutes(4),
            LastEditedAt = now.AddMinutes(4),
            CreatedByUserId = "carol",
            LastEditedByUserId = "carol",
            Labels = ["bulk-delete"]
        };

        _pages[Page6Id] = new NotionPage
        {
            Id = Page6Id,
            Title = "CF24 Delete Candidate B",
            SpaceId = "cf24-source",
            CreatedAt = now.AddMinutes(5),
            LastEditedAt = now.AddMinutes(5),
            CreatedByUserId = "carol",
            LastEditedByUserId = "carol",
            Labels = ["bulk-delete"]
        };
    }

    public void SeedE2EExportPage()
    {
        _pages.Clear();
        var now = new DateTime(2026, 2, 10, 9, 0, 0, DateTimeKind.Utc);

        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "CF25 Export Bridge", "Export root page.", "cf25", "ada", "Ada Lovelace", "grace", "Grace Hopper");
        _pages[Page1Id].CreatedAt = now;
        _pages[Page1Id].LastEditedAt = now.AddMinutes(30);

        _pages[Page2Id] = CreateSeedPage(Page2Id, Page1Id, "CF25 Export Child", "Export child page.", "cf25", "ada", "Ada Lovelace", "grace", "Grace Hopper");
        _pages[Page2Id].CreatedAt = now.AddMinutes(1);
        _pages[Page2Id].LastEditedAt = now.AddMinutes(31);

        _pages[Page3Id] = CreateSeedPage(Page3Id, Page2Id, "CF25 Export Grandchild", "Export grandchild page.", "cf25", "ada", "Ada Lovelace", "grace", "Grace Hopper");
        _pages[Page3Id].CreatedAt = now.AddMinutes(2);
        _pages[Page3Id].LastEditedAt = now.AddMinutes(32);
    }

    public void SeedE2ERestrictionsPage()
    {
        SeedE2EBulkPages();
        _pages[Page1Id].Title = "CF20 Restricted Workspace";
        _pages[Page1Id].Description = "Workspace with page-level access controls.";
        _pages[Page1Id].SpaceId = "cf20";
        _pages[Page2Id].Title = "CF20 Child Inherits Restrictions";
        _pages[Page2Id].Description = "Child page that inherits the root restrictions.";
        _pages[Page2Id].SpaceId = "cf20";
        _pages[Page3Id].Title = "CF20 Grandchild Inherits Restrictions";
        _pages[Page3Id].SpaceId = "cf20";
    }

    public void SeedE2ESidebarEmptyNavigation()
    {
        _pages.Clear();
        _pages[Page1Id] = new NotionPage
        {
            Id = Page1Id,
            Title = "Hidden task host",
            SpaceId = "hidden",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            LastEditedAt = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = "system",
            LastEditedByUserId = "system",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        };
    }

    public void SeedE2ESidebarDeepNavigation()
    {
        _pages.Clear();
        _spaces.Clear();
        SeedSpace("eb11");

        var created = new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);

        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "EB11 Knowledge Hub", "Sidebar deep-tree root page.", "eb11", "ada", "Ada Lovelace", "grace", "Grace Hopper");
        _pages[Page1Id].IconEmoji = "K";
        _pages[Page1Id].IsFavorite = true;
        _pages[Page1Id].CreatedAt = created;
        _pages[Page1Id].LastEditedAt = created.AddHours(7);

        _pages[Page2Id] = CreateSeedPage(Page2Id, null, "EB11 Product Roadmap", "Favorite roadmap page used by EB11.", "eb11", "grace", "Grace Hopper", "ada", "Ada Lovelace");
        _pages[Page2Id].IconEmoji = "R";
        _pages[Page2Id].IsFavorite = true;
        _pages[Page2Id].CreatedAt = created.AddMinutes(5);
        _pages[Page2Id].LastEditedAt = created.AddHours(6);

        _pages[Page3Id] = CreateSeedPage(Page3Id, null, "EB11 Engineering Handbook", "Root page with nested sidebar children.", "eb11", "bob", "Bob Stone", "bob", "Bob Stone");
        _pages[Page3Id].IconEmoji = "E";
        _pages[Page3Id].CreatedAt = created.AddMinutes(10);
        _pages[Page3Id].LastEditedAt = created.AddHours(5);

        _pages[Page4Id] = CreateSeedPage(Page4Id, Page3Id, "EB11 Architecture", "First nested handbook section.", "eb11", "bob", "Bob Stone", "dana", "Dana Fox");
        _pages[Page4Id].IconEmoji = "A";
        _pages[Page4Id].CreatedAt = created.AddMinutes(15);
        _pages[Page4Id].LastEditedAt = created.AddHours(4);

        _pages[Page5Id] = CreateSeedPage(Page5Id, Page4Id, "EB11 API Contracts", "Second nested handbook section.", "eb11", "dana", "Dana Fox", "dana", "Dana Fox");
        _pages[Page5Id].IconEmoji = "C";
        _pages[Page5Id].CreatedAt = created.AddMinutes(20);
        _pages[Page5Id].LastEditedAt = created.AddHours(3);

        _pages[Page6Id] = CreateSeedPage(Page6Id, Page5Id, "EB11 Release Runbook", "Third nested handbook section.", "eb11", "dana", "Dana Fox", "ada", "Ada Lovelace");
        _pages[Page6Id].IconEmoji = "B";
        _pages[Page6Id].CreatedAt = created.AddMinutes(25);
        _pages[Page6Id].LastEditedAt = created.AddHours(2);

        _pages[SidebarDeepRunbookId] = CreateSeedPage(SidebarDeepRunbookId, Page6Id, "EB11 Incident Checklist", "Fourth nested child used to verify deep indentation.", "eb11", "ada", "Ada Lovelace", "ada", "Ada Lovelace");
        _pages[SidebarDeepRunbookId].IconEmoji = "I";
        _pages[SidebarDeepRunbookId].CreatedAt = created.AddMinutes(30);
        _pages[SidebarDeepRunbookId].LastEditedAt = created.AddMinutes(95);

        _pages[SidebarDeepApiContractId] = CreateSeedPage(SidebarDeepApiContractId, Page4Id, "EB11 API Review Notes", "Sibling branch used to verify scan density.", "eb11", "grace", "Grace Hopper", "grace", "Grace Hopper");
        _pages[SidebarDeepApiContractId].IconEmoji = "N";
        _pages[SidebarDeepApiContractId].CreatedAt = created.AddMinutes(35);
        _pages[SidebarDeepApiContractId].LastEditedAt = created.AddMinutes(80);

        _pages[SidebarDeepReleaseChecklistId] = CreateSeedPage(SidebarDeepReleaseChecklistId, null, "EB11 Release Checklist", "Root sibling page used for drag reparenting.", "eb11", "alice", "Alice Morgan", "alice", "Alice Morgan");
        _pages[SidebarDeepReleaseChecklistId].IconEmoji = "L";
        _pages[SidebarDeepReleaseChecklistId].CreatedAt = created.AddMinutes(40);
        _pages[SidebarDeepReleaseChecklistId].LastEditedAt = created.AddMinutes(70);

        _pages[SidebarDeepRiskRegisterId] = CreateSeedPage(SidebarDeepRiskRegisterId, null, "EB11 Risk Register", "Root sibling page for sidebar density.", "eb11", "alice", "Alice Morgan", "bob", "Bob Stone");
        _pages[SidebarDeepRiskRegisterId].IconEmoji = "Q";
        _pages[SidebarDeepRiskRegisterId].CreatedAt = created.AddMinutes(45);
        _pages[SidebarDeepRiskRegisterId].LastEditedAt = created.AddMinutes(60);
    }

    public void SeedE2ESidebarTrashNavigation()
    {
        _pages.Clear();
        _spaces.Clear();
        SeedSpace("eb11");

        var created = new DateTime(2026, 3, 3, 9, 0, 0, DateTimeKind.Utc);
        var deletedAt = new DateTime(2026, 3, 5, 11, 30, 0, DateTimeKind.Utc);

        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "EB11 Trash Workspace", "Active page used while the trash panel is open.", "eb11", "ada", "Ada Lovelace", "grace", "Grace Hopper");
        _pages[Page1Id].IconEmoji = "T";
        _pages[Page1Id].CreatedAt = created;
        _pages[Page1Id].LastEditedAt = created.AddHours(4);

        _pages[Page2Id] = CreateSeedPage(Page2Id, null, "EB11 Active Reference", "Visible page that keeps navigation populated.", "eb11", "grace", "Grace Hopper", "grace", "Grace Hopper");
        _pages[Page2Id].IconEmoji = "A";
        _pages[Page2Id].CreatedAt = created.AddMinutes(10);
        _pages[Page2Id].LastEditedAt = created.AddHours(3);

        _pages[SidebarTrashLegacyDraftId] = CreateSeedPage(SidebarTrashLegacyDraftId, null, "EB11 Legacy Draft", "Deleted draft used for trash restore testing.", "eb11", "bob", "Bob Stone", "bob", "Bob Stone");
        _pages[SidebarTrashLegacyDraftId].IconEmoji = "D";
        _pages[SidebarTrashLegacyDraftId].CreatedAt = created.AddMinutes(20);
        _pages[SidebarTrashLegacyDraftId].LastEditedAt = deletedAt;
        _pages[SidebarTrashLegacyDraftId].IsDeleted = true;
        _pages[SidebarTrashLegacyDraftId].DeletedAt = deletedAt;

        _pages[SidebarTrashRetiredSpecId] = CreateSeedPage(SidebarTrashRetiredSpecId, null, "EB11 Retired Specification", "Deleted specification used for permanent delete testing.", "eb11", "dana", "Dana Fox", "dana", "Dana Fox");
        _pages[SidebarTrashRetiredSpecId].IconEmoji = "S";
        _pages[SidebarTrashRetiredSpecId].CreatedAt = created.AddMinutes(30);
        _pages[SidebarTrashRetiredSpecId].LastEditedAt = deletedAt.AddMinutes(8);
        _pages[SidebarTrashRetiredSpecId].IsDeleted = true;
        _pages[SidebarTrashRetiredSpecId].DeletedAt = deletedAt.AddMinutes(8);
    }

    public void SeedE2ESidebarTrashChildPage()
    {
        _pages.Clear();
        _spaces.Clear();
        SeedSpace("eb11");

        var created = new DateTime(2026, 3, 3, 9, 0, 0, DateTimeKind.Utc);
        var deletedAt = new DateTime(2026, 3, 5, 11, 30, 0, DateTimeKind.Utc);

        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "EB11 Trash Workspace", "Active page used while the trash panel is open.", "eb11", "ada", "Ada Lovelace", "grace", "Grace Hopper");
        _pages[Page1Id].IconEmoji = "T";
        _pages[Page1Id].CreatedAt = created;
        _pages[Page1Id].LastEditedAt = created.AddHours(4);

        // The surviving parent — the trashed child renders its breadcrumb ("EB11 Active Reference").
        _pages[Page2Id] = CreateSeedPage(Page2Id, null, "EB11 Active Reference", "Parent page that keeps the trashed child's path resolvable.", "eb11", "grace", "Grace Hopper", "grace", "Grace Hopper");
        _pages[Page2Id].IconEmoji = "A";
        _pages[Page2Id].CreatedAt = created.AddMinutes(10);
        _pages[Page2Id].LastEditedAt = created.AddHours(3);

        // A deleted child with NO icon (the trash row falls back to the lucide file-text glyph)
        // and a deleted root page without a path, for contrast.
        _pages[SidebarTrashOrphanedChildId] = CreateSeedPage(SidebarTrashOrphanedChildId, Page2Id, "EB11 Orphaned Child Page", "Deleted child page used for the trash path and icon-fallback capture.", "eb11", "bob", "Bob Stone", "bob", "Bob Stone");
        _pages[SidebarTrashOrphanedChildId].IconEmoji = string.Empty;
        _pages[SidebarTrashOrphanedChildId].CreatedAt = created.AddMinutes(20);
        _pages[SidebarTrashOrphanedChildId].LastEditedAt = deletedAt;
        _pages[SidebarTrashOrphanedChildId].IsDeleted = true;
        _pages[SidebarTrashOrphanedChildId].DeletedAt = deletedAt;

        _pages[SidebarTrashLegacyDraftId] = CreateSeedPage(SidebarTrashLegacyDraftId, null, "EB11 Legacy Draft", "Deleted draft used for trash restore testing.", "eb11", "bob", "Bob Stone", "bob", "Bob Stone");
        _pages[SidebarTrashLegacyDraftId].IconEmoji = string.Empty;
        _pages[SidebarTrashLegacyDraftId].CreatedAt = created.AddMinutes(25);
        _pages[SidebarTrashLegacyDraftId].LastEditedAt = deletedAt.AddMinutes(4);
        _pages[SidebarTrashLegacyDraftId].IsDeleted = true;
        _pages[SidebarTrashLegacyDraftId].DeletedAt = deletedAt.AddMinutes(4);
    }

    public void SeedE2ESimplePage(string title, string? description = null)
    {
        _pages.Clear();
        _pages[Page1Id] = CreateSeedPage(Page1Id, null, title, description ?? title, "e2e", "ada", "Ada Lovelace", "grace", "Grace Hopper");
    }

    public void SeedE2EPageSettingsPage()
    {
        _pages.Clear();
        _pages[Page1Id] = CreateSeedPage(
            Page1Id,
            null,
            "EB12 Page Settings",
            "Cover, icon, typography and lock settings recovery page.",
            "e2e",
            "ada",
            "Ada Lovelace",
            "grace",
            "Grace Hopper");
        _pages[Page1Id].IconEmoji = "🧭";
        _pages[Page1Id].CoverImageUrl = "data:image/svg+xml;base64,PHN2ZyB3aWR0aD0nMTYwMCcgaGVpZ2h0PSc0MDAnIHZpZXdCb3g9JzAgMCAxNjAwIDQwMCcgeG1sbnM9J2h0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnJz48ZGVmcz48bGluZWFyR3JhZGllbnQgaWQ9J2cnIHgxPScwJyB4Mj0nMScgeTE9JzAnIHkyPScxJz48c3RvcCBvZmZzZXQ9JzAlJyBzdG9wLWNvbG9yPScjMGY3NjYwJy8+PHN0b3Agb2Zmc2V0PSc0NSUnIHN0b3AtY29sb3I9JyMyNTYzZWInLz48c3RvcCBvZmZzZXQ9JzEwMCUnIHN0b3AtY29sb3I9JyNmNTliMWInLz48L2xpbmVhckdyYWRpZW50PjwvZGVmcz48cmVjdCB3aWR0aD0nMTYwMCcgaGVpZ2h0PSc0MDAnIGZpbGw9J3VybCgjZyknLz48Y2lyY2xlIGN4PScxMjgwJyBjeT0nODAnIHI9JzE4MCcgZmlsbD0nI2ZmZmZmZicgZmlsbC1vcGFjaXR5PScwLjE2Jy8+PHBhdGggZD0nTTAgMzIwQzI0MCAyNTAgNDAwIDM0MCA2NDAgMjgwQzg4MCAyMjAgMTA0MCAyNjAgMTYwMCAxODBWNDAwSDBaJyBmaWxsPScjZmZmZmZmJyBmaWxsLW9wYWNpdHk9JzAuMTInLz48L3N2Zz4=";
        _pages[Page1Id].CoverImagePositionY = 35;
        _pages[Page1Id].IsFullWidth = false;
        _pages[Page1Id].IsSmallText = false;
        _pages[Page1Id].IsLocked = false;
    }

    public void SeedE2EMentionTokenPage()
    {
        _pages.Clear();
        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "EB5 Mentions and Tokens", "Mention and token seed page.", "e2e", "ada", "Ada Lovelace", "grace", "Grace Hopper");
        _pages[Page2Id] = CreateSeedPage(
            Page2Id,
            null,
            "EB5 Very Long Page Link Title That Should Truncate Gracefully Inside The Inline Mention Chip Without Breaking Editor Layout",
            "Long title target for page-link chip recovery screenshots.",
            "e2e",
            "grace",
            "Grace Hopper",
            "ada",
            "Ada Lovelace");
    }

    public void SeedE2EPageInfoPage()
    {
        _pages.Clear();
        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "CF16 Page Info Workspace", "CF16 metadata and statistics page.", "cf16", "ada", "Ada Lovelace", "grace", "Grace Hopper");
    }

    public void SeedE2EEmptyPageInfoPage()
    {
        var created = new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc);
        _pages.Clear();
        _pages[Page1Id] = new NotionPage
        {
            Id = Page1Id,
            Title = "CF16 Empty Page Info Workspace",
            Description = "CF16 metadata edge-state page.",
            SpaceId = "cf16",
            IconEmoji = "📄",
            CreatedAt = created,
            LastEditedAt = created.AddHours(2),
            Labels = ["cf16", "edge-state"]
        };
    }

    public void SeedE2EAnalyticsPage()
    {
        _pages.Clear();
        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "CF31 Adoption Report", "Analytics source page.", "cf31", "ada", "Ada Lovelace", "grace", "Grace Hopper");
        _pages[Page2Id] = CreateSeedPage(Page2Id, null, "CF31 Usage Overview", "Top page candidate.", "cf31", "bob", "Bob Stone", "bob", "Bob Stone");
        _pages[Page4Id] = CreateSeedPage(Page4Id, null, "CF31 Team Metrics", "Secondary top page candidate.", "cf31", "demo", "Demo User", "demo", "Demo User");
    }

    public void SeedE2EAuditPage()
    {
        _pages.Clear();
        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "CF32 Audit Workspace", "Audit source page.", "cf32", "alice", "Alice Morgan", "demo", "Demo User");
        _pages[Page2Id] = CreateSeedPage(Page2Id, null, "CF32 Destination", "Audit move destination.", "cf32", "bob", "Bob Stone", "bob", "Bob Stone");
    }

    public void SeedE2EPublicSharePage()
    {
        _pages.Clear();
        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "CF33 Public Share Workspace", "Public read-only page shared through a tokenized link.", "cf33", "alice", "Alice Morgan", "demo", "Demo User");
    }

    public void SeedE2EIncludePage()
    {
        _pages.Clear();
        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "CF12 Include Page Target", "Include Page macro target.", "cf12", "ada", "Ada Lovelace", "ada", "Ada Lovelace");
        _pages[Page2Id] = CreateSeedPage(Page2Id, null, "CF12 Source Handbook", "Page with reusable included content.", "cf12", "bob", "Bob Stone", "bob", "Bob Stone");
        _pages[Page3Id] = CreateSeedPage(Page3Id, null, "CF12 Empty Source", "Source page with no blocks.", "cf12", "bob", "Bob Stone", "bob", "Bob Stone");
        _pages[Page4Id] = CreateSeedPage(Page4Id, Page2Id, "CF12 Nested Source", "Nested source page.", "cf12", "bob", "Bob Stone", "bob", "Bob Stone");
        _pages[Page5Id] = CreateSeedPage(Page5Id, null, "CF12 Deleted Source", "Deleted source page.", "cf12", "bob", "Bob Stone", "bob", "Bob Stone");
        _pages[Page5Id].IsDeleted = true;
        _pages[Page5Id].DeletedAt = DateTime.UtcNow;
    }

    public void SeedE2EChildrenDisplayPage()
    {
        _pages.Clear();
        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "CF13 Children Display Target", "Children Display macro target.", "cf13", "ada", "Ada Lovelace", "ada", "Ada Lovelace");
        _pages[Page2Id] = CreateSeedPage(Page2Id, Page1Id, "CF13 Product Space", "First child.", "cf13", "bob", "Bob Stone", "bob", "Bob Stone");
        _pages[Page3Id] = CreateSeedPage(Page3Id, Page2Id, "CF13 API Guide", "Nested child.", "cf13", "bob", "Bob Stone", "bob", "Bob Stone");
        _pages[Page4Id] = CreateSeedPage(Page4Id, Page3Id, "CF13 Deep Troubleshooting", "Deep nested child.", "cf13", "bob", "Bob Stone", "bob", "Bob Stone");
        _pages[Page5Id] = CreateSeedPage(Page5Id, null, "CF13 Empty Root", "Root without children.", "cf13", "demo", "Demo User", "demo", "Demo User");
        _pages[Page6Id] = CreateSeedPage(Page6Id, null, "CF13 Many Children Root", "Root with many children.", "cf13", "demo", "Demo User", "demo", "Demo User");

        for (var i = 1; i <= 16; i++)
        {
            var id = Guid.Parse($"cf130000-0000-0000-0001-{i:000000000000}");
            var title = i == 1
                ? "CF13 Child 01 with a very long title that should truncate cleanly in the tree row without pushing the navigation arrow away"
                : $"CF13 Child {i:00}";
            _pages[id] = CreateSeedPage(id, Page6Id, title, "Many child seed.", "cf13", "demo", "Demo User", "demo", "Demo User");
        }
    }

    public void SeedE2EExcerptPage()
    {
        _pages.Clear();
        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "CF14 Excerpt Include Target", "Excerpt macro target.", "cf14", "ada", "Ada Lovelace", "ada", "Ada Lovelace");
        _pages[Page2Id] = CreateSeedPage(Page2Id, null, "CF14 Source With Excerpt", "Source page with an excerpt.", "cf14", "bob", "Bob Stone", "bob", "Bob Stone");
        _pages[Page3Id] = CreateSeedPage(Page3Id, null, "CF14 Source Without Excerpt", "Source page without an excerpt.", "cf14", "bob", "Bob Stone", "bob", "Bob Stone");
        _pages[Page4Id] = CreateSeedPage(Page4Id, null, "CF14 Deleted Source", "Deleted source page.", "cf14", "bob", "Bob Stone", "bob", "Bob Stone");
        _pages[Page4Id].IsDeleted = true;
        _pages[Page4Id].DeletedAt = DateTime.UtcNow;
    }

    public void SeedE2EPagePropertiesPage()
    {
        _pages.Clear();
        _pages[Page1Id] = CreateSeedPage(Page1Id, null, "CF15 Page Properties Target", "Page properties and report seed page.", "cf15", "ada", "Ada Lovelace", "ada", "Ada Lovelace");
        _pages[Page2Id] = CreateSeedPage(Page2Id, null, "CF15 Alpha Project", "Report source with complete properties.", "cf15", "bob", "Bob Stone", "bob", "Bob Stone");
        _pages[Page2Id].Labels = ["cf15-report", "project"];
        _pages[Page2Id].IconEmoji = "A";
        _pages[Page3Id] = CreateSeedPage(Page3Id, null, "CF15 Beta Project", "Report source with a missing property.", "cf15", "demo", "Demo User", "demo", "Demo User");
        _pages[Page3Id].Labels = ["cf15-report", "project"];
        _pages[Page3Id].IconEmoji = "B";
        _pages[Page4Id] = CreateSeedPage(Page4Id, null, "CF15 Unmatched Archive", "Archive page outside the report label filter.", "cf15", "demo", "Demo User", "demo", "Demo User");
        _pages[Page4Id].Labels = ["archive"];
    }

    public Task<CopyPageTreeResult> CopyPageTreeAsync(string pageId, string? newParentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceId = Guid.Parse(pageId);
        Guid? parentId = string.IsNullOrWhiteSpace(newParentId) ? null : Guid.Parse(newParentId);

        if (!_pages.TryGetValue(sourceId, out var source))
            throw new KeyNotFoundException($"Page {pageId} not found");

        var map = new Dictionary<Guid, Guid>();
        var copiedRoot = CopyPageRecursive(source, parentId, true, map, cancellationToken);
        return Task.FromResult(new CopyPageTreeResult(copiedRoot, map));
    }

    public Task MovePagesAsync(IReadOnlyList<string> pageIds, string? newParentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = pageIds.Select(Guid.Parse).Distinct().ToArray();
        Guid? parentId = string.IsNullOrWhiteSpace(newParentId) ? null : Guid.Parse(newParentId);

        if (parentId is not null)
        {
            foreach (var id in ids)
            {
                if (id == parentId.Value || IsDescendantOf(parentId.Value, id))
                    throw new InvalidOperationException("Pages cannot be moved into their own descendants.");
            }
        }

        foreach (var id in ids)
        {
            if (!_pages.TryGetValue(id, out var page))
                throw new KeyNotFoundException($"Page {id} not found");
        }

        foreach (var id in ids)
        {
            var page = _pages[id];
            page.ParentId = parentId;
            page.LastEditedAt = DateTime.UtcNow;
            page.LastEditedByUserId = "demo-user";
        }

        return Task.CompletedTask;
    }

    public Task DeletePagesAsync(IReadOnlyList<string> pageIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = pageIds.Select(Guid.Parse).Distinct().ToArray();
        var toDelete = ids.SelectMany(GetPageAndDescendants).Distinct().ToArray();
        var now = DateTime.UtcNow;

        foreach (var id in toDelete)
        {
            if (!_pages.TryGetValue(id, out var page))
                continue;

            page.IsDeleted = true;
            page.DeletedAt = now;
            page.LastEditedAt = now;
            page.LastEditedByUserId = "demo-user";
        }

        return Task.CompletedTask;
    }

    public void Reset()
    {
        _pages.Clear();
        InitializeMockData();
    }

    private static string NormalizeLabel(string? label)
        => label?.Trim() ?? string.Empty;

    private static IReadOnlyList<string> NormalizeLabels(IEnumerable<string>? labels)
    {
        var normalized = new List<string>();
        foreach (var label in labels ?? [])
        {
            var value = NormalizeLabel(label);
            if (string.IsNullOrWhiteSpace(value) ||
                normalized.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
                continue;

            normalized.Add(value);
        }

        return normalized;
    }

    private static NotionPage CreateSeedPage(
        Guid id,
        Guid? parentId,
        string title,
        string description,
        string spaceId,
        string createdByUserId,
        string createdByDisplayName,
        string lastEditedByUserId,
        string lastEditedByDisplayName)
    {
        var created = new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc);
        return new NotionPage
        {
            Id = id,
            ParentId = parentId,
            Title = title,
            Description = description,
            SpaceId = spaceId,
            IconEmoji = "📄",
            CreatedAt = created,
            CreatedByUserId = createdByUserId,
            LastEditedAt = created.AddHours(2),
            LastEditedByUserId = lastEditedByUserId,
            Labels = [spaceId, createdByDisplayName, lastEditedByDisplayName]
        };
    }

    private static NotionSpaceDto CreateSpace(string spaceId, IEnumerable<NotionPage> pages)
    {
        var normalizedId = NormalizeSpaceId(spaceId);
        var firstPage = pages.OrderBy(page => page.Title, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

        return new NotionSpaceDto
        {
            Id = normalizedId,
            Key = normalizedId.ToUpperInvariant().Replace('-', '_'),
            Name = ToSpaceName(normalizedId),
            Description = normalizedId switch
            {
                "team" => "Shared launch and team planning space.",
                "personal" => "Private notes and drafts.",
                "archive" => "Archived workspace pages.",
                _ => $"Workspace for {ToSpaceName(normalizedId)}."
            },
            IconEmoji = normalizedId switch
            {
                "team" => "T",
                "personal" => "P",
                "archive" => "A",
                _ => normalizedId.Length > 0 ? normalizedId[0].ToString().ToUpperInvariant() : "S"
            },
            HomePageId = firstPage?.Id.ToString("D"),
            Type = normalizedId switch
            {
                "personal" => NotionSpaceType.Personal,
                "archive" => NotionSpaceType.Public,
                _ => NotionSpaceType.Team
            }
        };
    }

    private void SeedSpace(string spaceId)
    {
        var normalizedId = NormalizeSpaceId(spaceId);
        _spaces[normalizedId] = CreateSpace(normalizedId, _pages.Values.Where(page =>
            string.Equals(page.SpaceId, normalizedId, StringComparison.OrdinalIgnoreCase)));
    }

    private static NotionSpaceDto CopySpaceWithHomePage(NotionSpaceDto space, string? homePageId) => new()
    {
        Id = space.Id,
        Key = space.Key,
        Name = space.Name,
        Description = space.Description,
        IconEmoji = space.IconEmoji,
        HomePageId = homePageId,
        Type = space.Type
    };

    private static int SpaceOrder(string spaceId) => NormalizeSpaceId(spaceId) switch
    {
        "team" => 0,
        "personal" => 1,
        "archive" => 2,
        _ => 10
    };

    private static string NormalizeSpaceId(string value)
        => string.IsNullOrWhiteSpace(value) ? "team" : value.Trim().ToLowerInvariant();

    private static string ToSpaceName(string spaceId)
        => NormalizeSpaceId(spaceId) switch
        {
            "team" => "Team",
            "personal" => "Personal",
            "archive" => "Archive",
            var id when id.StartsWith("space-", StringComparison.OrdinalIgnoreCase) => $"Space {id[^2..]}",
            var id => string.Join(" ", id.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]))
        };

    private NotionPage? ResolveRootPage(NotionPage page)
    {
        var current = page;
        while (current.ParentId is { } parentId)
        {
            if (!_pages.TryGetValue(parentId, out var parent))
                return current;

            current = parent;
        }

        return current;
    }

    private NotionPage CopyPageRecursive(
        NotionPage source,
        Guid? parentId,
        bool isRoot,
        Dictionary<Guid, Guid> map,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var copy = new NotionPage
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            Title = isRoot ? $"{source.Title} (Copy)" : source.Title,
            Description = source.Description,
            SpaceId = source.SpaceId,
            Labels = source.Labels.ToArray(),
            IconEmoji = source.IconEmoji,
            IconImageUrl = source.IconImageUrl,
            CoverImageUrl = source.CoverImageUrl,
            CoverImagePositionY = source.CoverImagePositionY,
            IsFullWidth = source.IsFullWidth,
            IsSmallText = source.IsSmallText,
            IsLocked = source.IsLocked,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = "demo-user",
            LastEditedAt = DateTime.UtcNow,
            LastEditedByUserId = "demo-user",
            IsFavorite = false
        };

        map[source.Id] = copy.Id;
        _pages[copy.Id] = copy;

        var children = _pages.Values
            .Where(page => page.ParentId == source.Id && page.Id != copy.Id)
            .OrderBy(page => page.CreatedAt)
            .ToArray();

        foreach (var child in children)
            CopyPageRecursive(child, copy.Id, false, map, cancellationToken);

        return copy;
    }

    private bool IsDescendantOf(Guid candidateId, Guid ancestorId)
    {
        var currentId = candidateId;
        var visited = new HashSet<Guid>();

        while (visited.Add(currentId) && _pages.TryGetValue(currentId, out var page) && page.ParentId is { } parentId)
        {
            if (parentId == ancestorId)
                return true;

            currentId = parentId;
        }

        return false;
    }

    private IEnumerable<Guid> GetPageAndDescendants(Guid pageId)
    {
        yield return pageId;

        foreach (var childId in _pages.Values.Where(page => page.ParentId == pageId).Select(page => page.Id).ToArray())
        {
            foreach (var descendantId in GetPageAndDescendants(childId))
                yield return descendantId;
        }
    }
}
