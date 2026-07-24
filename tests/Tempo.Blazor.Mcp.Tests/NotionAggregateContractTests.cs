using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Mcp.Tests;

public sealed class NotionAggregateContractTests
{
    [Fact]
    public void PageSnapshot_RoundTripsStableJsonContract()
    {
        var pageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var tableId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var rowId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var snapshot = new NotionPageSnapshot
        {
            SchemaVersion = NotionPageSnapshot.CurrentSchemaVersion,
            Page = new NotionPageState
            {
                Id = pageId,
                ParentPageId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Title = "Atomic authoring",
                Labels = ["contract", "notion"]
            },
            Blocks =
            [
                new NotionBlockSnapshot
                {
                    Id = tableId,
                    PageId = pageId,
                    Type = BlockType.Table,
                    Order = 4,
                    Content = JsonSerializer.SerializeToElement(
                        new NotionAuthoringTable
                        {
                            ColumnCount = 2,
                            HasHeaderRow = true,
                            ColumnAlignments =
                            [
                                NotionTableHorizontalAlignment.Left,
                                NotionTableHorizontalAlignment.Right
                            ],
                            ColumnWidths = [160, 90]
                        },
                        NotionAggregateJson.Options)
                },
                new NotionBlockSnapshot
                {
                    Id = rowId,
                    PageId = pageId,
                    ParentBlockId = tableId,
                    Type = BlockType.TableRow,
                    Order = 0,
                    Content = JsonSerializer.SerializeToElement(
                        new NotionAuthoringTableRow
                        {
                            Cells = [CreateRichCell()]
                        },
                        NotionAggregateJson.Options)
                }
            ],
            ConcurrencyToken = "opaque:server-owned-token",
            Digest = "sha256:4e98f451"
        };

        var json = JsonSerializer.Serialize(snapshot, NotionAggregateJson.Options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        root.GetProperty("page").GetProperty("parentPageId").GetGuid()
            .Should().Be(snapshot.Page.ParentPageId!.Value);
        root.GetProperty("blocks")[1].GetProperty("parentBlockId").GetGuid()
            .Should().Be(tableId);
        root.GetProperty("blocks")[1].GetProperty("order").GetInt32().Should().Be(0);
        root.GetProperty("concurrencyToken").GetString().Should().Be("opaque:server-owned-token");
        root.GetProperty("digest").GetString().Should().Be("sha256:4e98f451");

        var restored = JsonSerializer.Deserialize<NotionPageSnapshot>(json, NotionAggregateJson.Options);

        restored.Should().NotBeNull();
        restored!.SchemaVersion.Should().Be(NotionPageSnapshot.CurrentSchemaVersion);
        restored.Page.Id.Should().Be(pageId);
        restored.Blocks.Select(block => block.Id).Should().Equal(tableId, rowId);
        restored.Blocks[1].ParentBlockId.Should().Be(tableId);
        restored.Blocks[1].Order.Should().Be(0);
        restored.ConcurrencyToken.Should().Be(snapshot.ConcurrencyToken);
        restored.Digest.Should().Be(snapshot.Digest);
    }

    [Fact]
    public void RichTableCell_RoundTripsLogicalAuthoringFieldsWithoutPhysicalMergeMarkers()
    {
        var cell = CreateRichCell();

        var json = JsonSerializer.Serialize(cell, NotionAggregateJson.Options);
        var restored = JsonSerializer.Deserialize<NotionAuthoringTableCell>(
            json,
            NotionAggregateJson.Options);

        restored.Should().BeEquivalentTo(cell);
        json.Should().Contain("\"html\"");
        json.Should().Contain("\"inlines\"");
        json.Should().Contain("\"backgroundColor\"");
        json.Should().Contain("\"textColor\"");
        json.Should().Contain("\"horizontalAlignment\":\"center\"");
        json.Should().Contain("\"verticalAlignment\":\"middle\"");
        json.Should().Contain("\"rowSpan\":2");
        json.Should().Contain("\"columnSpan\":3");
        json.Should().Contain("\"width\":144");
        json.Should().Contain("\"borders\"");

        typeof(NotionAuthoringTableCell).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Should().NotContain(["IsMergeHidden", "MergeOriginRow", "MergeOriginColumn"]);
    }

    [Fact]
    public void AtomicSaveContracts_RoundTripTokensDigestsConflictsAndStructuredIssues()
    {
        var pageA = CreatePageSnapshot("aaaaaaaa-0000-0000-0000-000000000001", "token-a");
        var pageB = CreatePageSnapshot("bbbbbbbb-0000-0000-0000-000000000002", "token-b");
        var request = new NotionAggregateSaveRequest
        {
            Pages =
            [
                new NotionPageSave
                {
                    Snapshot = pageA,
                    BaseConcurrencyToken = pageA.ConcurrencyToken
                },
                new NotionPageSave
                {
                    Snapshot = pageB,
                    BaseConcurrencyToken = pageB.ConcurrencyToken
                }
            ]
        };
        var result = new NotionAggregateSaveResult
        {
            Success = false,
            Conflict = true,
            Conflicts =
            [
                new NotionPageConflict
                {
                    PageId = pageB.Page.Id,
                    ExpectedConcurrencyToken = "token-b",
                    CurrentConcurrencyToken = "token-b2",
                    CurrentDigest = "sha256:current-b"
                }
            ],
            Issues =
            [
                new NotionAggregateIssue
                {
                    Code = "concurrency_conflict",
                    Severity = NotionIssueSeverity.Error,
                    Message = "The page changed after it was loaded.",
                    Path = "$.pages[1].baseConcurrencyToken",
                    SuggestedFix = "Reload the page and reapply the operation."
                }
            ]
        };

        var requestJson = JsonSerializer.Serialize(request, NotionAggregateJson.Options);
        var resultJson = JsonSerializer.Serialize(result, NotionAggregateJson.Options);
        var restoredRequest = JsonSerializer.Deserialize<NotionAggregateSaveRequest>(
            requestJson,
            NotionAggregateJson.Options);
        var restoredResult = JsonSerializer.Deserialize<NotionAggregateSaveResult>(
            resultJson,
            NotionAggregateJson.Options);

        restoredRequest!.Pages.Select(page => page.Snapshot.Page.Id)
            .Should().Equal(pageA.Page.Id, pageB.Page.Id);
        restoredRequest.Pages.Select(page => page.BaseConcurrencyToken)
            .Should().Equal("token-a", "token-b");
        restoredResult.Should().BeEquivalentTo(result);
        resultJson.Should().Contain("\"atomic\":true");
        resultJson.Should().Contain("\"severity\":\"error\"");
        resultJson.Should().Contain("\"path\":\"$.pages[1].baseConcurrencyToken\"");
    }

    [Fact]
    public void AggregateProvider_ExposesOnlyAggregateLoadsAndOneAtomicSaveBoundary()
    {
        var methods = typeof(INotionAggregateProvider)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToList();

        methods.Select(method => method.Name)
            .Should().Equal("LoadBlockAsync", "LoadPageAsync", "SaveAsync");
        methods.Single(method => method.Name == "LoadPageAsync").ReturnType
            .Should().Be<Task<NotionAggregateLoadResult>>();
        methods.Single(method => method.Name == "LoadBlockAsync").ReturnType
            .Should().Be<Task<NotionAggregateLoadResult>>();
        methods.Single(method => method.Name == "SaveAsync")
            .GetParameters()[0].ParameterType.Should().Be<NotionAggregateSaveRequest>();
        methods.Should().NotContain(method =>
            method.Name.Contains("Create", StringComparison.Ordinal) ||
            method.Name.Contains("Update", StringComparison.Ordinal) ||
            method.Name.Contains("Delete", StringComparison.Ordinal) ||
            method.Name.Contains("Reorder", StringComparison.Ordinal));
    }

    [Fact]
    public void IdempotentAggregateProvider_AddsOneTransactionalExecutionBoundary()
    {
        typeof(INotionIdempotentAggregateProvider).GetInterfaces()
            .Should().ContainSingle(type => type == typeof(INotionAggregateProvider));

        var method = typeof(INotionIdempotentAggregateProvider)
            .GetMethod(nameof(INotionIdempotentAggregateProvider.ExecuteIdempotentAsync));

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be<Task<NotionIdempotentExecutionResult>>();
        method.GetParameters().Select(parameter => parameter.ParameterType)
            .Should().Equal(
                typeof(NotionIdempotentExecutionRequest),
                typeof(Func<
                    INotionAggregateProvider,
                    CancellationToken,
                    Task<string>>),
                typeof(CancellationToken));
    }

    [Fact]
    public void WireContracts_PinEveryPublicPropertyNameExplicitly()
    {
        var wireTypes = new[]
        {
            typeof(NotionPageSnapshot),
            typeof(NotionPageState),
            typeof(NotionBlockSnapshot),
            typeof(NotionAggregateLoadResult),
            typeof(NotionAggregateSaveRequest),
            typeof(NotionPageSave),
            typeof(NotionAggregateSaveResult),
            typeof(NotionIdempotentExecutionRequest),
            typeof(NotionIdempotentExecutionResult),
            typeof(NotionSavedPage),
            typeof(NotionPageConflict),
            typeof(NotionAggregateIssue),
            typeof(NotionAuthoringTable),
            typeof(NotionAuthoringTableRow),
            typeof(NotionAuthoringTableCell),
            typeof(NotionRichTextInline),
            typeof(NotionTableCellBorders),
            typeof(NotionTableBorder)
        };

        foreach (var type in wireTypes)
        {
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Should()
                .OnlyContain(
                    property => property.GetCustomAttribute<JsonPropertyNameAttribute>() != null,
                    $"{type.Name} is a public wire contract and cannot rely on a host naming policy");
        }
    }

    private static NotionAuthoringTableCell CreateRichCell() => new()
    {
        Html = "<strong>Maximum loss</strong>",
        Inlines =
        [
            new NotionRichTextInline
            {
                Text = "Maximum loss",
                Bold = true,
                TextColor = "#7F6000"
            }
        ],
        BackgroundColor = "#FDE9D9",
        TextColor = "#7F6000",
        HorizontalAlignment = NotionTableHorizontalAlignment.Center,
        VerticalAlignment = NotionTableVerticalAlignment.Middle,
        RowSpan = 2,
        ColumnSpan = 3,
        Width = 144,
        Borders = new NotionTableCellBorders
        {
            Top = new NotionTableBorder
            {
                Style = NotionTableBorderStyle.Solid,
                Color = "#000000",
                Width = 1
            },
            Bottom = new NotionTableBorder
            {
                Style = NotionTableBorderStyle.Double,
                Color = "#76933C",
                Width = 1.5
            }
        }
    };

    private static NotionPageSnapshot CreatePageSnapshot(string id, string token)
    {
        var pageId = Guid.Parse(id);
        return new NotionPageSnapshot
        {
            Page = new NotionPageState { Id = pageId, Title = id },
            ConcurrencyToken = token,
            Digest = $"sha256:{pageId:N}"
        };
    }
}
