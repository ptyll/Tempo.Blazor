using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Tempo.Blazor.Demo.Api.Tests;

/// <summary>
/// The /api/wireframe/preview/render-document endpoint caps its request body at 1 MB — including
/// chunked uploads that carry no Content-Length header (the header-only check let chunked bodies
/// bypass the cap).
/// </summary>
public class WireframePreviewBodyLimitTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public WireframePreviewBodyLimitTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task RenderDocument_WithAChunkedBodyOver1Mb_IsRejected()
    {
        var oversized = new string('x', 1_048_576 + 16);
        // A StreamContent over an unseekable stream sends chunked transfer encoding (no
        // Content-Length), so the endpoint cannot lean on the header check alone.
        using var content = new StreamContent(new UnseekableStringStream(oversized));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await _client.PostAsync("/api/wireframe/preview/render-document", content);

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge,
            $"expected 400/413, got {(int)response.StatusCode} ({response.StatusCode})");
    }

    private sealed class UnseekableStringStream : Stream
    {
        private readonly byte[] _payload;
        private int _position;

        public UnseekableStringStream(string payload) => _payload = Encoding.UTF8.GetBytes(payload);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _payload.Length) return 0;
            var n = Math.Min(count, _payload.Length - _position);
            Array.Copy(_payload, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
