using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Collaboration;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;

namespace Tempo.Blazor.Tests.DocumentEditor;

/// <summary>
/// Larger-deployment load test: 20 concurrent editors spread over two server instances joined by
/// a backplane produce 1000 operations; after full fan-out every instance holds the complete
/// stream and every replica converges to an identical document through the conflict resolver.
/// </summary>
public class DocumentCollaborationLoadTests
{
    [Fact]
    public async Task TwentyEditors_ThousandOperations_AllReplicasConverge()
    {
        const int editorCount = 20;
        const int operationsPerEditor = 50; // 20 × 50 = 1000 operací

        var backplane = new InMemoryDocumentCollaborationBackplane();
        await using var instanceA = new BackplaneDocumentCollaborationProvider(backplane);
        await using var instanceB = new BackplaneDocumentCollaborationProvider(backplane);
        var instances = new[] { instanceA, instanceB };

        // 20 editors alternate between the two server instances.
        var sessions = new List<(BackplaneDocumentCollaborationProvider Instance, string SessionId, string ClientId)>();
        for (var editor = 0; editor < editorCount; editor++)
        {
            var instance = instances[editor % instances.Length];
            var session = await instance.JoinAsync(new DocumentCollaborationJoinRequest
            {
                DocumentId = "load-doc",
                ClientId = $"client-{editor}",
                Author = new DocumentEditorAuthor { Id = $"user-{editor}", DisplayName = $"Editor {editor}" },
            });
            sessions.Add((instance, session.Id, $"client-{editor}"));
        }

        // Concurrent broadcasting: every editor produces its operations on its own task.
        var broadcastTasks = sessions.Select((entry, editorIndex) => Task.Run(async () =>
        {
            var random = new Random(1000 + editorIndex);
            for (var i = 0; i < operationsPerEditor; i++)
            {
                var operation = CreateRandomOperation(random, editorIndex, i);
                await entry.Instance.BroadcastOperationBatchAsync(entry.SessionId, new DocumentOperationBatch
                {
                    Id = $"batch-{editorIndex}-{i}",
                    DocumentId = "load-doc",
                    ClientId = entry.ClientId,
                    Operations = [operation],
                });
            }
        })).ToArray();
        await Task.WhenAll(broadcastTasks);

        // Complete fan-out: both instances hold all 1000 batches.
        var batchesOnA = await instanceA.GetOperationBatchesAsync("load-doc", 0);
        var batchesOnB = await instanceB.GetOperationBatchesAsync("load-doc", 0);
        batchesOnA.Should().HaveCount(editorCount * operationsPerEditor, "instance A must hold every batch");
        batchesOnB.Should().HaveCount(editorCount * operationsPerEditor, "instance B must hold every batch");

        // Convergence: each replica resolves its own (differently ordered) stream and applies it
        // to the same base document — the results must be identical.
        var documentA = ApplyResolvedStream(batchesOnA);
        var documentB = ApplyResolvedStream(batchesOnB);
        documentB.Should().Be(documentA, "both replicas must converge to the same document content");
    }

    private static string ApplyResolvedStream(IReadOnlyList<DocumentCollaborationOperationBatch> batches)
    {
        var operations = batches.SelectMany(batch => batch.Batch.Operations).ToList();
        var resolver = new DocumentOperationConflictResolver();
        var applier = new DocumentOperationApplier();
        var document = CreateBaseDocument();
        foreach (var operation in resolver.Resolve(operations))
        {
            applier.Apply(document, operation);
        }

        document.Metadata.CreatedAt = new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero);
        document.Metadata.ModifiedAt = new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero);
        return Regex.Replace(
            JsonSerializer.Serialize(document, DocumentEditorJson.Options),
            "[0-9a-f]{32}",
            "id");
    }

    private static DocumentOperation CreateRandomOperation(Random random, int editorIndex, int sequence)
    {
        string[] blocks = ["b1", "b2", "b3", "b4"];
        var block = blocks[random.Next(blocks.Length)];
        var timestamp = editorIndex * 1000 + sequence;
        var operation = new DocumentOperation
        {
            OperationId = $"op-{editorIndex}-{sequence}",
            Target = new DocumentOperationTarget { BlockId = block, InlineIndex = 0 },
            Metadata = new DocumentOperationMetadata
            {
                LogicalTimestamp = timestamp,
                ClientId = $"client-{editorIndex}",
                AuthorId = $"user-{editorIndex}",
            },
        };

        switch (random.Next(4))
        {
            case 0:
                operation.Type = DocumentOperationType.InsertText;
                operation.Target.Offset = random.Next(0, 30);
                operation.Text = $"e{editorIndex}s{sequence} ";
                break;
            case 1:
                operation.Type = DocumentOperationType.DeleteText;
                operation.Target.Offset = random.Next(0, 30);
                operation.Target.Length = random.Next(1, 4);
                break;
            case 2:
                operation.Type = DocumentOperationType.AddInlineMark;
                operation.Target.Offset = random.Next(0, 25);
                operation.Target.Length = random.Next(1, 6);
                operation.Mark = new InlineMark { Type = InlineMarkType.Bold };
                break;
            default:
                operation.Type = DocumentOperationType.SetBlockAttribute;
                operation.AttributeName = "align";
                operation.AttributeValueJson = $"\"v{editorIndex}-{sequence}\"";
                break;
        }

        return operation;
    }

    private static DocumentEditorDocument CreateBaseDocument()
    {
        var document = DocumentEditorDocument.Empty();
        document.DocumentId = "load-doc";
        document.Blocks = new[] { "b1", "b2", "b3", "b4" }.Select((id, index) => new DocumentBlock
        {
            Id = id,
            Type = DocumentBlockType.Paragraph,
            Order = index + 1,
            Content = new ParagraphBlockContent
            {
                Inlines = [new TextRun { Id = $"{id}-run", Text = "Výchozí text odstavce pro zátěžový test konvergence dokumentu." }],
            },
        }).ToList();
        return document;
    }
}

/// <summary>
/// Redis backplane integration: runs only when a local Redis answers on localhost:6379
/// (dev boxes without Redis skip it silently, mirroring the RequiresSmtp4Dev pattern).
/// </summary>
[Trait("Category", "RequiresRedis")]
public class RedisDocumentCollaborationBackplaneTests
{
    [Fact]
    public async Task RedisBackplane_FansOperationBatchesOutAcrossInstances()
    {
        RedisDocumentCollaborationBackplane backplane;
        try
        {
            backplane = await RedisDocumentCollaborationBackplane.ConnectAsync("localhost:6379,connectTimeout=1000,abortConnect=true");
        }
        catch
        {
            return; // Redis not available on this machine — covered by the in-memory backplane tests.
        }

        await using (backplane)
        {
            await using var instanceA = new BackplaneDocumentCollaborationProvider(backplane);
            await using var instanceB = new BackplaneDocumentCollaborationProvider(backplane);

            var sessionA = await instanceA.JoinAsync(new DocumentCollaborationJoinRequest
            {
                DocumentId = "redis-doc",
                Author = new DocumentEditorAuthor { Id = "a", DisplayName = "A" },
            });
            await instanceB.JoinAsync(new DocumentCollaborationJoinRequest
            {
                DocumentId = "redis-doc",
                Author = new DocumentEditorAuthor { Id = "b", DisplayName = "B" },
            });

            await instanceA.BroadcastOperationBatchAsync(sessionA.Id, new DocumentOperationBatch
            {
                Id = "redis-batch-1",
                DocumentId = "redis-doc",
                ClientId = "client-a",
                Operations =
                [
                    new DocumentOperation
                    {
                        OperationId = "redis-op-1",
                        Type = DocumentOperationType.InsertText,
                        Target = new DocumentOperationTarget { BlockId = "b1", InlineIndex = 0, Offset = 0 },
                        Text = "Přes Redis",
                        Metadata = new DocumentOperationMetadata { LogicalTimestamp = 1, ClientId = "client-a", AuthorId = "a" },
                    },
                ],
            });

            // Redis pub/sub is asynchronous — the barrier is the fan-out landing in B's stream,
            // named by the helper rather than slept past.
            var onB = await WaitForBatchesAsync(instanceB, "redis-doc");
            onB.Should().ContainSingle("the batch must arrive through Redis pub/sub");
            onB[0].Batch.Operations.Single().Text.Should().Be("Přes Redis");
        }
    }

    /// <summary>
    /// Waits until the backplane's fan-out has landed in <paramref name="reader"/>'s operation
    /// stream. Pub/sub delivery produces no event the test can await directly, so the state — a
    /// non-empty stream — is polled, with the deadline named here instead of a bare sleep count.
    /// </summary>
    private static async Task<IReadOnlyList<DocumentCollaborationOperationBatch>> WaitForBatchesAsync(
        BackplaneDocumentCollaborationProvider reader,
        string documentId,
        int timeoutMs = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        IReadOnlyList<DocumentCollaborationOperationBatch> batches = [];
        while (batches.Count == 0 && Environment.TickCount64 < deadline)
        {
            await Task.Delay(50);
            batches = await reader.GetOperationBatchesAsync(documentId, 0);
        }

        return batches;
    }
}
