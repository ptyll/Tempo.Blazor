using System.Collections.Concurrent;
using Tempo.Blazor.Demo.Shared;

namespace Tempo.Blazor.Demo.Api.Data;

public class MockAttachmentStore
{
    public ConcurrentDictionary<string, List<AttachmentDto>> Attachments { get; } = new();

    private readonly ConcurrentDictionary<string, ChunkUploadState> _uploads = new();

    public MockAttachmentStore()
    {
        foreach (var entity in new[] { "task-1", "task-2", "task-3" })
        {
            Attachments[entity] = GenerateAttachments(entity);
        }
    }

    private static List<AttachmentDto> GenerateAttachments(string entityId)
    {
        var hash = Math.Abs(entityId.GetHashCode());
        return
        [
            new AttachmentDto(
                Id: $"{entityId}-a-1",
                FileName: "report.pdf",
                ContentType: "application/pdf",
                FileSizeBytes: 245_000,
                UploadedAt: DateTimeOffset.Now.AddDays(-3),
                UploadedByName: "Alice",
                CanDelete: true,
                IsImage: false),
            new AttachmentDto(
                Id: $"{entityId}-a-2",
                FileName: "screenshot.png",
                ContentType: "image/png",
                FileSizeBytes: 128_000,
                UploadedAt: DateTimeOffset.Now.AddDays(-2),
                UploadedByName: "Bob",
                CanDelete: true,
                IsImage: true),
            new AttachmentDto(
                Id: $"{entityId}-a-3",
                FileName: "data.xlsx",
                ContentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                FileSizeBytes: 87_500,
                UploadedAt: DateTimeOffset.Now.AddDays(-1),
                UploadedByName: "Carol",
                CanDelete: hash % 2 == 0,
                IsImage: false),
        ];
    }

    public string? ProcessChunk(string fileName, string contentType, long totalSize,
        int chunkIndex, int totalChunks, byte[] data, string? entityId)
    {
        var key = $"{entityId}:{fileName}";

        var state = _uploads.GetOrAdd(key, _ => new ChunkUploadState(totalChunks));
        state.ReceivedChunks[chunkIndex] = true;
        state.AccumulatedSize += data.Length;

        if (state.ReceivedChunks.All(c => c))
        {
            _uploads.TryRemove(key, out _);

            var id = Guid.NewGuid().ToString("N")[..8];
            var attachment = new AttachmentDto(
                Id: id,
                FileName: fileName,
                ContentType: contentType,
                FileSizeBytes: totalSize,
                UploadedAt: DateTimeOffset.Now,
                UploadedByName: "Demo User",
                CanDelete: true,
                IsImage: contentType.StartsWith("image/"));

            var list = Attachments.GetOrAdd(entityId ?? "unknown", _ => []);
            list.Add(attachment);

            return id;
        }

        return null;
    }

    private sealed class ChunkUploadState(int totalChunks)
    {
        public bool[] ReceivedChunks { get; } = new bool[totalChunks];
        public long AccumulatedSize { get; set; }
    }
}
