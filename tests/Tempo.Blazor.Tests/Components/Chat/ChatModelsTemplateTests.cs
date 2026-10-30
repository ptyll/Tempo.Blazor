using System.Text.Json;
using FluentAssertions;
using Tempo.Blazor.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Chat;

/// <summary>
/// Template contract fields of <see cref="ChatMessage"/> (<c>Kind</c>, <c>Metadata</c>,
/// <c>Payload</c>) added by the custom-templates plan. TmChat never interprets them — it only
/// hands them to <c>MessageTemplate</c> through <c>ChatMessageContext</c>.
/// </summary>
public class ChatModelsTemplateTests
{
    [Fact]
    public void ChatMessage_TemplateFields_DefaultToEmpty()
    {
        var m = new ChatMessage { Id = "1", Text = "hi" };

        m.Kind.Should().BeNull("a plain message has no special kind");
        m.Metadata.Should().NotBeNull("Metadata defaults to an empty dictionary, never null")
            .And.BeEmpty();
        m.Payload.Should().BeNull();
    }

    [Fact]
    public void ChatMessage_WithExpression_KeepsMetadataAndPayload()
    {
        var payload = new HostMessageDto("d-1", "Draft reply", "policy-violation");

        var m = new ChatMessage("m1", "draft")
        {
            Kind = "ai-draft",
            Metadata = new Dictionary<string, object?>
            {
                ["reason"] = "policy-violation",
                ["ppvPrice"] = 9.5,
            },
            Payload = payload,
        };

        m.Kind.Should().Be("ai-draft");
        m.Metadata["reason"].Should().Be("policy-violation");
        m.Metadata["ppvPrice"].Should().Be(9.5);
        m.Payload.Should().BeSameAs(payload, "host DTO travels untouched");
    }

    [Fact]
    public void ChatMessage_NullMetadata_CoalescesToEmpty()
    {
        var m = new ChatMessage { Id = "1", Text = "hi", Metadata = null! };

        m.Metadata.Should().NotBeNull("a null Metadata assignment coalesces to the empty default")
            .And.BeEmpty();
    }

    [Fact]
    public void ChatMessage_JsonSerialization_ExcludesPayload()
    {
        var m = new ChatMessage("m1", "hello")
        {
            Payload = new HostMessageDto("d-1", "Draft reply", "policy-violation"),
        };

        var json = JsonSerializer.Serialize(m);

        json.Should().NotContain("d-1", "the payload DTO must never leak into serialized output");
        json.Should().NotContain("Draft reply");
        json.Should().NotContain("policy-violation");
    }

    private sealed record HostMessageDto(string Id, string Text, string PolicyReason);
}
