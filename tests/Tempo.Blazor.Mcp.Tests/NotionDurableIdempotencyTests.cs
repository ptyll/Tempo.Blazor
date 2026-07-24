using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Mcp.Notion;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.NotionEditor.Testing;

namespace Tempo.Blazor.Mcp.Tests;

public sealed class NotionDurableIdempotencyTests
{
    [Fact]
    public async Task ApplyBlockOperations_DurableProvider_DoesNotRequireInternalReceiptRuntime()
    {
        var page = Page("11111111-1111-1111-1111-111111111111", "token-1");
        var provider = new DurableAggregateProvider(page);
        using var services = new ServiceCollection().BuildServiceProvider();

        var json = await NotionBlockTools.ApplyBlockOperations(
            services,
            provider,
            "durable-tool-without-fallback",
            """
            [{
              "op":"createBlock",
              "pageId":"11111111-1111-1111-1111-111111111111",
              "clientRef":"created",
              "block":{
                "type":"paragraph",
                "content":{"html":"Created once"}
              }
            }]
            """,
            """
            [{
              "pageId":"11111111-1111-1111-1111-111111111111",
              "concurrencyToken":"token-1"
            }]
            """);

        var result = JsonNode.Parse(json)!.AsObject();
        result["success"]!.GetValue<bool>().Should().BeTrue();
        provider.IdempotentCallbackCount.Should().Be(1);
        provider.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_NewEngine_ReplaysProviderReceiptBeforeLoadingStaleTarget()
    {
        var page = Page("11111111-1111-1111-1111-111111111111", "token-1");
        var block = Block(page.Page.Id, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var provider = new DurableAggregateProvider(page);
        var request = Request(page);
        var firstCompiler = new RecordingCompiler(
            new NotionUpsertBlockOperation(0, "created", block));
        var firstEngine = Engine(provider, firstCompiler);

        var first = await firstEngine.ExecuteAsync(request);
        var loadCountAfterFirstExecution = provider.LoadCount;
        var restartedCompiler = new RecordingCompiler();
        var restartedEngine = Engine(provider, restartedCompiler);
        var replay = await restartedEngine.ExecuteAsync(request);

        first.Success.Should().BeTrue();
        replay.Should().BeEquivalentTo(first with { Replayed = true });
        provider.SaveCount.Should().Be(1);
        provider.LoadCount.Should().Be(loadCountAfterFirstExecution);
        provider.IdempotentCallbackCount.Should().Be(1);
        restartedCompiler.CallCount.Should().Be(0);
        provider.LastIdempotencyRequest.Should().BeEquivalentTo(
            new NotionIdempotentExecutionRequest
            {
                OperationScope = "notion_apply_block_operations",
                Key = request.IdempotencyKey,
                RequestHash = first.RequestHash,
                Retention = TimeSpan.FromHours(24)
            });
    }

    [Fact]
    public async Task ExecuteAsync_NewEngine_DifferentHashReturnsCollisionWithoutCallback()
    {
        var page = Page("11111111-1111-1111-1111-111111111111", "token-1");
        var provider = new DurableAggregateProvider(page);
        var firstEngine = Engine(provider, new RecordingCompiler());
        var firstRequest = Request(page, """[{"op":"noop","value":1}]""");
        var changedRequest = Request(page, """[{"op":"noop","value":2}]""");

        var first = await firstEngine.ExecuteAsync(firstRequest);
        var restartedCompiler = new RecordingCompiler();
        var collision = await Engine(provider, restartedCompiler).ExecuteAsync(changedRequest);

        first.Success.Should().BeTrue();
        collision.Success.Should().BeFalse();
        collision.Errors.Should().ContainSingle(issue =>
            issue.Code == "idempotency_key_reused" &&
            issue.Path == "$.idempotencyKey");
        provider.IdempotentCallbackCount.Should().Be(1);
        restartedCompiler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_UnexpectedFailureDoesNotCommitDurableReceipt()
    {
        var page = Page("11111111-1111-1111-1111-111111111111", "token-1");
        var provider = new DurableAggregateProvider(page);
        var failing = Engine(provider, new ThrowingCompiler());
        var request = Request(page);

        var first = () => failing.ExecuteAsync(request);
        await first.Should().ThrowAsync<ApplicationException>();

        var retryCompiler = new RecordingCompiler();
        var retry = await Engine(provider, retryCompiler).ExecuteAsync(request);

        retry.Success.Should().BeTrue();
        retry.Replayed.Should().BeFalse();
        provider.IdempotentCallbackCount.Should().Be(2);
        retryCompiler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentEngines_InvokeDurableCallbackExactlyOnce()
    {
        var page = Page("11111111-1111-1111-1111-111111111111", "token-1");
        var provider = new DurableAggregateProvider(page)
        {
            PauseBeforeReceiptCommit = true
        };
        var request = Request(page);
        var first = Engine(
            provider,
            new RecordingCompiler(
                new NotionUpsertBlockOperation(
                    0,
                    "created",
                    Block(page.Page.Id, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"))))
            .ExecuteAsync(request);
        await provider.ResponseProduced.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var concurrentCompiler = new RecordingCompiler();
        var concurrent = Engine(provider, concurrentCompiler).ExecuteAsync(request);
        provider.AllowReceiptCommit.TrySetResult();
        var results = await Task.WhenAll(first, concurrent);

        results.Should().OnlyContain(result => result.Success);
        results.Count(result => result.Replayed).Should().Be(1);
        provider.IdempotentCallbackCount.Should().Be(1);
        provider.SaveCount.Should().Be(1);
        concurrentCompiler.CallCount.Should().Be(0);
    }

    private static NotionAtomicAuthoringEngine Engine(
        INotionAggregateProvider provider,
        INotionAtomicOperationCompiler compiler)
        => new(provider, compiler, new InMemoryNotionIdempotencyReceiptStore());

    private static NotionAtomicAuthoringRequest Request(
        NotionPageSnapshot page,
        string operationsJson = """[{"op":"create","clientRef":"created"}]""")
        => new()
        {
            IdempotencyKey = "durable-across-restart",
            OperationsJson = operationsJson,
            Targets =
            [
                new NotionAggregateTarget(NotionAggregateTargetKind.Page, page.Page.Id)
            ],
            ExpectedPageVersions =
            [
                new NotionExpectedPageVersion(page.Page.Id, page.ConcurrencyToken)
            ]
        };

    private static NotionPageSnapshot Page(string id, string token)
    {
        var pageId = Guid.Parse(id);
        return new NotionPageSnapshot
        {
            Page = new NotionPageState { Id = pageId, Title = "Durable receipt" },
            ConcurrencyToken = token,
            Digest = $"sha256:{pageId:N}"
        };
    }

    private static NotionBlockSnapshot Block(Guid pageId, string id)
        => new()
        {
            Id = Guid.Parse(id),
            PageId = pageId,
            Type = BlockType.Paragraph,
            Content = JsonSerializer.SerializeToElement(
                new TextBlockContent { Html = "Created once" },
                NotionAggregateJson.Options)
        };

    private sealed class RecordingCompiler(params NotionCanonicalOperation[] operations)
        : INotionAtomicOperationCompiler
    {
        public int CallCount { get; private set; }

        public ValueTask<NotionOperationCompilationResult> CompileAsync(
            JsonArray source,
            NotionAggregateWorkingSet workingSet,
            NotionOperationCompileContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult(
                NotionOperationCompilationResult.Compiled(operations));
        }
    }

    private sealed class ThrowingCompiler : INotionAtomicOperationCompiler
    {
        public ValueTask<NotionOperationCompilationResult> CompileAsync(
            JsonArray source,
            NotionAggregateWorkingSet workingSet,
            NotionOperationCompileContext context,
            CancellationToken cancellationToken)
            => throw new ApplicationException("Synthetic provider-transaction failure.");
    }

    private sealed class DurableAggregateProvider(params NotionPageSnapshot[] pages)
        : INotionIdempotentAggregateProvider
    {
        private readonly FakeNotionAggregateProvider _inner = new(pages);
        private readonly SemaphoreSlim _transaction = new(1, 1);
        private readonly Dictionary<(string Scope, string Key), Receipt> _receipts = [];

        public int LoadCount => _inner.LoadCallCount;
        public int SaveCount => _inner.SaveCallCount;
        public int IdempotentCallbackCount { get; private set; }
        public NotionIdempotentExecutionRequest? LastIdempotencyRequest { get; private set; }
        public bool PauseBeforeReceiptCommit { get; init; }
        public TaskCompletionSource ResponseProduced { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowReceiptCommit { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<NotionAggregateLoadResult> LoadPageAsync(
            Guid pageId,
            CancellationToken cancellationToken = default)
            => _inner.LoadPageAsync(pageId, cancellationToken);

        public Task<NotionAggregateLoadResult> LoadBlockAsync(
            Guid blockId,
            CancellationToken cancellationToken = default)
            => _inner.LoadBlockAsync(blockId, cancellationToken);

        public Task<NotionAggregateSaveResult> SaveAsync(
            NotionAggregateSaveRequest request,
            CancellationToken cancellationToken = default)
            => _inner.SaveAsync(request, cancellationToken);

        public async Task<NotionIdempotentExecutionResult> ExecuteIdempotentAsync(
            NotionIdempotentExecutionRequest request,
            Func<INotionAggregateProvider, CancellationToken, Task<string>> operation,
            CancellationToken cancellationToken = default)
        {
            LastIdempotencyRequest = request;
            await _transaction.WaitAsync(cancellationToken);
            try
            {
                var receiptKey = (request.OperationScope, request.Key);
                if (_receipts.TryGetValue(receiptKey, out var receipt))
                {
                    return string.Equals(
                        receipt.RequestHash,
                        request.RequestHash,
                        StringComparison.Ordinal)
                        ? new NotionIdempotentExecutionResult
                        {
                            Status = NotionIdempotentExecutionStatus.Replayed,
                            ResponseJson = receipt.ResponseJson
                        }
                        : new NotionIdempotentExecutionResult
                        {
                            Status = NotionIdempotentExecutionStatus.Collision
                        };
                }

                IdempotentCallbackCount++;
                var responseJson = await operation(this, cancellationToken);
                ResponseProduced.TrySetResult();
                if (PauseBeforeReceiptCommit)
                {
                    await AllowReceiptCommit.Task.WaitAsync(cancellationToken);
                }
                _receipts.Add(
                    receiptKey,
                    new Receipt(request.RequestHash, responseJson));
                return new NotionIdempotentExecutionResult
                {
                    Status = NotionIdempotentExecutionStatus.Executed,
                    ResponseJson = responseJson
                };
            }
            finally
            {
                _transaction.Release();
            }
        }

        private sealed record Receipt(string RequestHash, string ResponseJson);
    }
}
