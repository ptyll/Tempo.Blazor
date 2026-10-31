using FluentAssertions;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.AI.Services;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Pins <see cref="AiMessageStreamApplier"/> — the pure, UI-free reducer that folds an
/// <see cref="AiStreamDelta"/> stream into an <see cref="AiMessage"/>. Every AI component in
/// <c>Tempo.Blazor.AI</c> renders through this model, so the contract is tested here rather than
/// inside a component: the stream must build parts in arrival order, be idempotent for repeated
/// control deltas (a re-delivered <c>Completed</c> or the same citation twice must not churn), and
/// must never let a late token reopen a finished message.
/// </summary>
public sealed class AiMessageTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    private static AiMessage NewMessage() => new()
    {
        Id = "assistant-1",
        Role = AiMessageRole.Assistant,
        CreatedAt = T0,
    };

    private static AiSource Source(string id) => new() { Id = id, Title = $"Source {id}", Url = $"https://example.test/{id}" };

    // ── Text ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TextDeltas_AppendToASingleTextPart()
    {
        var message = NewMessage();

        AiMessageStreamApplier.Apply(message, new AiTextDelta { Text = "Hello" }).Should().BeTrue();
        AiMessageStreamApplier.Apply(message, new AiTextDelta { Text = ", " }).Should().BeTrue();
        AiMessageStreamApplier.Apply(message, new AiTextDelta { Text = "world" }).Should().BeTrue();

        message.Parts.Should().HaveCount(1);
        message.Parts[0].Should().BeOfType<AiTextPart>().Which.Markdown.Should().Be("Hello, world");
    }

    [Fact]
    public void TextDelta_AfterANonTextPart_StartsANewTextPart()
    {
        var message = NewMessage();

        AiMessageStreamApplier.Apply(message, new AiTextDelta { Text = "first" });
        AiMessageStreamApplier.Apply(message, new AiCitationAddedDelta { Source = Source("s1") });
        AiMessageStreamApplier.Apply(message, new AiTextDelta { Text = "second" });

        message.Parts.OfType<AiTextPart>().Select(part => part.Markdown)
            .Should().Equal("first", "second");
        message.Parts.Select(part => part.GetType()).Should().Equal(
            typeof(AiTextPart), typeof(AiSourcesPart), typeof(AiTextPart));
    }

    [Fact]
    public void EmptyTextDelta_IsANoOp()
    {
        var message = NewMessage();

        AiMessageStreamApplier.Apply(message, new AiTextDelta { Text = string.Empty }).Should().BeFalse();

        message.Parts.Should().BeEmpty();
        message.Status.Should().Be(AiMessageStatus.Pending);
    }

    [Fact]
    public void FirstContentDelta_MovesPendingToStreaming()
    {
        var message = NewMessage();

        AiMessageStreamApplier.Apply(message, new AiTextDelta { Text = "x" });

        message.Status.Should().Be(AiMessageStatus.Streaming);
    }

    // ── Reasoning ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReasoningDeltas_AppendToTheLastStep_AndNewStepStartsAnother()
    {
        var message = NewMessage();

        AiMessageStreamApplier.Apply(message, new AiReasoningDelta { Text = "Look " });
        AiMessageStreamApplier.Apply(message, new AiReasoningDelta { Text = "here." });
        AiMessageStreamApplier.Apply(message, new AiReasoningDelta { Text = "Then answer.", NewStep = true });

        var reasoning = message.Parts.OfType<AiReasoningPart>().Should().ContainSingle().Subject;
        reasoning.Steps.Should().Equal("Look here.", "Then answer.");
    }

    [Fact]
    public void ReasoningDelta_ReusesTheTrailingReasoningPart_ButNotAnEarlierOne()
    {
        var message = NewMessage();

        AiMessageStreamApplier.Apply(message, new AiReasoningDelta { Text = "step" });
        AiMessageStreamApplier.Apply(message, new AiTextDelta { Text = "answer" });
        AiMessageStreamApplier.Apply(message, new AiReasoningDelta { Text = "more" });

        message.Parts.OfType<AiReasoningPart>().Should().HaveCount(2);
        message.Parts.OfType<AiReasoningPart>().Select(part => part.Steps[0]).Should().Equal("step", "more");
    }

    // ── Tool calls ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToolCallStarted_AddsAToolCallPart_AndDuplicateIdDoesNotAddAnother()
    {
        var message = NewMessage();

        AiMessageStreamApplier.Apply(message, new AiToolCallStartedDelta
        {
            ToolCall = new AiToolCall { Id = "call-1", Name = "search", DisplayName = "Search" },
        }, T0).Should().BeTrue();

        var part = message.Parts.OfType<AiToolCallPart>().Should().ContainSingle().Subject;
        part.ToolCall.Status.Should().Be(AiToolCallStatus.Running);
        part.ToolCall.StartedAt.Should().Be(T0);

        var again = AiMessageStreamApplier.Apply(message, new AiToolCallStartedDelta
        {
            ToolCall = new AiToolCall { Id = "call-1", Name = "search", DisplayName = "Search" },
        }, T0.AddSeconds(5));

        again.Should().BeFalse("a re-delivered start delta must not churn the part");
        message.Parts.OfType<AiToolCallPart>().Should().ContainSingle();
        message.Parts.OfType<AiToolCallPart>().Single().ToolCall.StartedAt.Should().Be(T0);
    }

    [Fact]
    public void ToolCallUpdated_UpdatesArgumentsById_AndCreatesWhenUnknown()
    {
        var message = NewMessage();
        AiMessageStreamApplier.Apply(message, new AiToolCallStartedDelta
        {
            ToolCall = new AiToolCall { Id = "call-1", Name = "search" },
        }, T0);

        AiMessageStreamApplier.Apply(message, new AiToolCallUpdatedDelta
        {
            ToolCallId = "call-1",
            ArgumentsJson = """{"q":"tempo"}""",
        }, T0).Should().BeTrue();

        var updated = message.Parts.OfType<AiToolCallPart>().Single().ToolCall;
        updated.ArgumentsJson.Should().Be("""{"q":"tempo"}""");
        updated.Name.Should().Be("search");

        AiMessageStreamApplier.Apply(message, new AiToolCallUpdatedDelta
        {
            ToolCallId = "call-2",
            Name = "fetch",
        }, T0).Should().BeTrue();
        message.Parts.OfType<AiToolCallPart>().Should().HaveCount(2);
        message.Parts.OfType<AiToolCallPart>().Last().ToolCall.Name.Should().Be("fetch");
    }

    [Fact]
    public void ToolCallUpdated_WithIdenticalValues_ReportsNoChange()
    {
        var message = NewMessage();
        AiMessageStreamApplier.Apply(message, new AiToolCallStartedDelta
        {
            ToolCall = new AiToolCall { Id = "call-1", Name = "search", ArgumentsJson = "{}" },
        }, T0);

        AiMessageStreamApplier.Apply(message, new AiToolCallUpdatedDelta
        {
            ToolCallId = "call-1",
            ArgumentsJson = "{}",
        }, T0).Should().BeFalse();
    }

    [Fact]
    public void ToolCallCompleted_SetsStatusResultAndTimestamp_AndIsIdempotent()
    {
        var message = NewMessage();
        AiMessageStreamApplier.Apply(message, new AiToolCallStartedDelta
        {
            ToolCall = new AiToolCall { Id = "call-1", Name = "search" },
        }, T0);

        AiMessageStreamApplier.Apply(message, new AiToolCallCompletedDelta
        {
            ToolCallId = "call-1",
            Status = AiToolCallStatus.Failed,
            Error = "timeout",
        }, T0.AddSeconds(2)).Should().BeTrue();

        var call = message.Parts.OfType<AiToolCallPart>().Single().ToolCall;
        call.Status.Should().Be(AiToolCallStatus.Failed);
        call.Error.Should().Be("timeout");
        call.CompletedAt.Should().Be(T0.AddSeconds(2));
        call.StartedAt.Should().Be(T0);

        AiMessageStreamApplier.Apply(message, new AiToolCallCompletedDelta
        {
            ToolCallId = "call-1",
            Status = AiToolCallStatus.Failed,
            Error = "timeout",
        }, T0.AddSeconds(9)).Should().BeFalse("a re-delivered completion keeps the first timestamp");
        call.CompletedAt.Should().Be(T0.AddSeconds(2));
    }

    [Fact]
    public void ToolCallCompleted_ForAnUnknownId_CreatesACompletedPart()
    {
        var message = NewMessage();

        AiMessageStreamApplier.Apply(message, new AiToolCallCompletedDelta
        {
            ToolCallId = "call-late",
            Status = AiToolCallStatus.Succeeded,
            ResultJson = """{"ok":true}""",
        }, T0).Should().BeTrue();

        var call = message.Parts.OfType<AiToolCallPart>().Single().ToolCall;
        call.Id.Should().Be("call-late");
        call.Status.Should().Be(AiToolCallStatus.Succeeded);
        call.CompletedAt.Should().Be(T0);
    }

    // ── Citations ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CitationAdded_AppendsToASourcesPart_AndDeduplicatesBySourceId()
    {
        var message = NewMessage();

        AiMessageStreamApplier.Apply(message, new AiCitationAddedDelta { Source = Source("a") });
        AiMessageStreamApplier.Apply(message, new AiCitationAddedDelta { Source = Source("b") });
        AiMessageStreamApplier.Apply(message, new AiCitationAddedDelta { Source = Source("a") }).Should().BeFalse();

        var sources = message.Parts.OfType<AiSourcesPart>().Should().ContainSingle().Subject;
        sources.Sources.Select(source => source.Id).Should().Equal("a", "b");
        message.Sources.Select(source => source.Id).Should().Equal("a", "b");
    }

    [Fact]
    public void CitationAdded_WithAnEmptyId_IsANoOpThatLeavesThePartsUntouched()
    {
        var message = NewMessage();

        AiMessageStreamApplier.Apply(message, new AiTextDelta { Text = "answer" });

        AiMessageStreamApplier.Apply(message, new AiCitationAddedDelta { Source = Source(" ") })
            .Should().BeFalse("an unaddressable citation is a malformed transport delta, not an exception");

        message.Parts.Select(part => part.GetType()).Should().Equal(typeof(AiTextPart));
        message.Sources.Should().BeEmpty();
        message.Status.Should().Be(AiMessageStatus.Streaming);
    }

    [Fact]
    public void CitationAdded_WithANullSource_IsANoOpThatLeavesThePartsUntouched()
    {
        var message = NewMessage();

        AiMessageStreamApplier.Apply(message, new AiCitationAddedDelta { Source = null })
            .Should().BeFalse("a stream consumer must survive a partial delta instead of losing the answer");

        message.Parts.Should().BeEmpty();
        message.Status.Should().Be(AiMessageStatus.Pending);
    }

    // ── Terminal transitions ────────────────────────────────────────────────────────────────

    [Fact]
    public void CompletedDelta_CompletesTheMessage_AndDuplicateIsANoOp()
    {
        var message = NewMessage();
        AiMessageStreamApplier.Apply(message, new AiTextDelta { Text = "done" });

        AiMessageStreamApplier.Apply(message, new AiCompletedDelta()).Should().BeTrue();
        message.Status.Should().Be(AiMessageStatus.Completed);

        AiMessageStreamApplier.Apply(message, new AiCompletedDelta()).Should().BeFalse();
    }

    [Fact]
    public void ErrorDelta_FailsTheMessage_AndKeepsTheReason()
    {
        var message = NewMessage();

        AiMessageStreamApplier.Apply(message, new AiErrorDelta { Message = "rate limited" }).Should().BeTrue();

        message.Status.Should().Be(AiMessageStatus.Failed);
        message.Error.Should().Be("rate limited");
        AiMessageStreamApplier.Apply(message, new AiErrorDelta { Message = "rate limited" }).Should().BeFalse();
    }

    [Fact]
    public void ContentDeltaAfterATerminalState_IsIgnored()
    {
        foreach (var status in new[] { AiMessageStatus.Completed, AiMessageStatus.Failed, AiMessageStatus.Stopped })
        {
            var message = NewMessage();
            AiMessageStreamApplier.Apply(message, new AiTextDelta { Text = "partial" });
            message.Status = status;

            AiMessageStreamApplier.Apply(message, new AiTextDelta { Text = " late token" }).Should().BeFalse();
            AiMessageStreamApplier.Apply(message, new AiCitationAddedDelta { Source = Source("late") }).Should().BeFalse();

            message.Parts.OfType<AiTextPart>().Single().Markdown.Should().Be("partial", status.ToString());
            message.Parts.OfType<AiSourcesPart>().Should().BeEmpty(status.ToString());
        }
    }

    [Theory]
    [InlineData(AiMessageStatus.Completed)]
    [InlineData(AiMessageStatus.Stopped)]
    public void ErrorDelta_AfterACompletedOrStoppedMessage_IsIgnored(AiMessageStatus terminal)
    {
        var message = NewMessage();
        AiMessageStreamApplier.Apply(message, new AiTextDelta { Text = "partial answer" });
        message.Status = terminal;

        AiMessageStreamApplier.Apply(message, new AiErrorDelta { Message = "transport closed" })
            .Should().BeFalse("a finished or user-stopped answer is never retroactively failed");

        message.Status.Should().Be(terminal);
        message.Error.Should().BeNull();
        message.Parts.OfType<AiTextPart>().Single().Markdown.Should().Be("partial answer");
    }

    [Fact]
    public void CompletedDelta_AfterAFailure_IsIgnored()
    {
        var message = NewMessage();
        AiMessageStreamApplier.Apply(message, new AiErrorDelta { Message = "boom" });

        AiMessageStreamApplier.Apply(message, new AiCompletedDelta()).Should().BeFalse();
        message.Status.Should().Be(AiMessageStatus.Failed);
    }

    [Fact]
    public void Apply_RejectsNullArguments()
    {
        var message = NewMessage();

        var apply = () => AiMessageStreamApplier.Apply(message, null!);
        var withNullMessage = () => AiMessageStreamApplier.Apply(null!, new AiCompletedDelta());

        apply.Should().Throw<ArgumentNullException>();
        withNullMessage.Should().Throw<ArgumentNullException>();
    }

    // ── A whole stream ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AWholeStream_ProducesTheExpectedPartOrderAndState()
    {
        var message = NewMessage();
        var deltas = new AiStreamDelta[]
        {
            new AiReasoningDelta { Text = "Need a lookup." },
            new AiToolCallStartedDelta { ToolCall = new AiToolCall { Id = "call-1", Name = "search" } },
            new AiToolCallUpdatedDelta { ToolCallId = "call-1", ArgumentsJson = """{"q":"x"}""" },
            new AiToolCallCompletedDelta
            {
                ToolCallId = "call-1",
                Status = AiToolCallStatus.Succeeded,
                ResultJson = """{"n":1}""",
            },
            new AiTextDelta { Text = "Answer [1](cite:a)." },
            new AiCitationAddedDelta { Source = Source("a") },
            new AiCompletedDelta(),
        };

        foreach (var delta in deltas)
        {
            AiMessageStreamApplier.Apply(message, delta, T0);
        }

        message.Status.Should().Be(AiMessageStatus.Completed);
        message.Parts.Select(part => part.GetType()).Should().Equal(
            typeof(AiReasoningPart), typeof(AiToolCallPart), typeof(AiTextPart), typeof(AiSourcesPart));
        message.Parts.OfType<AiTextPart>().Single().Markdown.Should().Be("Answer [1](cite:a).");
        message.Sources.Should().ContainSingle(source => source.Id == "a");
    }
}
