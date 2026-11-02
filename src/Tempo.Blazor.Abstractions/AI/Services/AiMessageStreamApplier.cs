using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.AI.Services;

/// <summary>
/// Folds an <see cref="AiStreamDelta"/> stream into an <see cref="AiMessage"/> — the one place that knows
/// how a message grows. Pure logic (no UI, no clock of its own): the caller passes the timestamp, so a
/// Server circuit, a WebAssembly host and a unit test all produce the same message. Every call reports
/// whether it changed the message, which is what lets a component skip a render for a no-op delta.
/// </summary>
public static class AiMessageStreamApplier
{
    /// <summary>
    /// Applies one delta to <paramref name="message"/>.
    /// </summary>
    /// <param name="message">The message being streamed.</param>
    /// <param name="delta">The delta to fold in.</param>
    /// <param name="timestamp">
    /// UTC time recorded on tool-call transitions; defaults to <see cref="DateTimeOffset.UtcNow"/>. Tests
    /// pass a fixed value instead of freezing a clock.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the message changed, <see langword="false"/> for a no-op (an empty text
    /// chunk, a re-delivered control delta, a citation without a source id, or content that arrived after
    /// the message reached a terminal state).
    /// </returns>
    /// <remarks>
    /// <para>The state machine this method owns is deliberately small and total:</para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>Pending</c> is the initial state. The first content delta (text, reasoning, tool call, citation)
    /// moves the message to <c>Streaming</c>.
    /// </description></item>
    /// <item><description>
    /// <c>Streaming</c> stays <c>Streaming</c> while content arrives.
    /// </description></item>
    /// <item><description>
    /// <c>Completed</c> is entered from <c>Pending</c>/<c>Streaming</c> by <see cref="AiCompletedDelta"/>.
    /// A duplicate completion, or a completion after <c>Failed</c> or <c>Stopped</c>, is a no-op.
    /// </description></item>
    /// <item><description>
    /// <c>Failed</c> is entered by <see cref="AiErrorDelta"/> from <c>Pending</c>/<c>Streaming</c>. A
    /// duplicate error with the same reason is a no-op; an error that arrives after <c>Completed</c> or
    /// <c>Stopped</c> is ignored, because a finished answer is never retroactively failed.
    /// </description></item>
    /// <item><description>
    /// <c>Stopped</c> is set by the host (the Stop button), never by a delta. It is terminal for content,
    /// completion AND errors, so a cancellation cannot turn the partial answer into a failure.
    /// </description></item>
    /// </list>
    /// <para>
    /// Once a message is terminal (<c>Completed</c>, <c>Stopped</c> or <c>Failed</c>) a late content delta
    /// is dropped instead of reopening the answer. Only the two terminal deltas are let through, and each
    /// one still refuses an illegal transition itself — a late token must never resurrect a finished turn.
    /// </para>
    /// </remarks>
    public static bool Apply(AiMessage message, AiStreamDelta delta, DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(delta);

        var at = timestamp ?? DateTimeOffset.UtcNow;

        if (IsTerminal(message.Status) && delta is not AiCompletedDelta and not AiErrorDelta)
        {
            // A late token must never reopen a finished (or stopped, or failed) answer.
            return false;
        }

        return delta switch
        {
            AiTextDelta text => ApplyText(message, text),
            AiReasoningDelta reasoning => ApplyReasoning(message, reasoning),
            AiToolCallStartedDelta started => ApplyToolCallStarted(message, started, at),
            AiToolCallUpdatedDelta updated => ApplyToolCallUpdated(message, updated),
            AiToolCallCompletedDelta completed => ApplyToolCallCompleted(message, completed, at),
            AiCitationAddedDelta citation => ApplyCitation(message, citation),
            AiCompletedDelta => ApplyCompleted(message),
            AiErrorDelta error => ApplyError(message, error),
            _ => false,
        };
    }

    private static bool IsTerminal(AiMessageStatus status)
        => status is AiMessageStatus.Completed or AiMessageStatus.Stopped or AiMessageStatus.Failed;

    private static bool ApplyText(AiMessage message, AiTextDelta delta)
    {
        if (string.IsNullOrEmpty(delta.Text))
        {
            return false;
        }

        if (message.Parts.Count > 0 && message.Parts[^1] is AiTextPart trailing)
        {
            trailing.Append(delta.Text);
        }
        else
        {
            message.Parts.Add(new AiTextPart(delta.Text));
        }

        MarkStreaming(message);
        return true;
    }

    private static bool ApplyReasoning(AiMessage message, AiReasoningDelta delta)
    {
        var text = delta.Text ?? string.Empty;
        if (text.Length == 0 && !delta.NewStep)
        {
            return false;
        }

        var part = message.Parts.Count > 0 && message.Parts[^1] is AiReasoningPart trailing
            ? trailing
            : null;

        if (part is null)
        {
            part = new AiReasoningPart();
            message.Parts.Add(part);
        }

        if (part.Steps.Count == 0 || delta.NewStep)
        {
            part.Steps.Add(text);
        }
        else
        {
            part.Steps[^1] += text;
        }

        MarkStreaming(message);
        return true;
    }

    private static bool ApplyToolCallStarted(AiMessage message, AiToolCallStartedDelta delta, DateTimeOffset at)
    {
        var call = delta.ToolCall ?? new AiToolCall();
        var existing = FindToolCall(message, call.Id);

        if (existing is not null)
        {
            // Idempotent: a re-delivered start keeps the first start timestamp and the part position.
            var changed = false;
            if (existing.Status is AiToolCallStatus.Queued or AiToolCallStatus.Running)
            {
                changed |= SetIfDifferent(existing.Name, call.Name, value => existing.Name = value);
                if (!string.IsNullOrEmpty(call.DisplayName))
                {
                    changed |= SetIfDifferent(existing.DisplayName, call.DisplayName, value => existing.DisplayName = value);
                }
            }

            return changed;
        }

        call.Status = AiToolCallStatus.Running;
        call.StartedAt ??= at;
        call.CompletedAt = null;
        message.Parts.Add(new AiToolCallPart(call));
        MarkStreaming(message);
        return true;
    }

    private static bool ApplyToolCallUpdated(AiMessage message, AiToolCallUpdatedDelta delta)
    {
        var call = FindToolCall(message, delta.ToolCallId);
        if (call is null)
        {
            call = new AiToolCall
            {
                Id = delta.ToolCallId ?? string.Empty,
                Status = AiToolCallStatus.Running,
            };
            message.Parts.Add(new AiToolCallPart(call));
        }

        var changed = false;
        if (!string.IsNullOrEmpty(delta.Name))
        {
            changed |= SetIfDifferent(call.Name, delta.Name, value => call.Name = value);
        }

        if (!string.IsNullOrEmpty(delta.DisplayName))
        {
            changed |= SetIfDifferent(call.DisplayName, delta.DisplayName, value => call.DisplayName = value);
        }

        if (delta.ArgumentsJson is not null)
        {
            changed |= SetIfDifferent(call.ArgumentsJson, delta.ArgumentsJson, value => call.ArgumentsJson = value);
        }

        if (delta.ResultJson is not null)
        {
            changed |= SetIfDifferent(call.ResultJson, delta.ResultJson, value => call.ResultJson = value);
        }

        if (delta.Status is { } status && call.Status != status)
        {
            call.Status = status;
            changed = true;
        }

        if (changed)
        {
            MarkStreaming(message);
        }

        return changed;
    }

    private static bool ApplyToolCallCompleted(AiMessage message, AiToolCallCompletedDelta delta, DateTimeOffset at)
    {
        var call = FindToolCall(message, delta.ToolCallId);
        var changed = false;

        if (call is null)
        {
            call = new AiToolCall { Id = delta.ToolCallId ?? string.Empty };
            message.Parts.Add(new AiToolCallPart(call));
            changed = true;
        }

        if (call.StartedAt is null)
        {
            call.StartedAt = at;
            changed = true;
        }

        if (call.Status != delta.Status)
        {
            call.Status = delta.Status;
            changed = true;
        }

        if (call.CompletedAt is null)
        {
            call.CompletedAt = at;
            changed = true;
        }

        if (delta.ResultJson is not null)
        {
            changed |= SetIfDifferent(call.ResultJson, delta.ResultJson, value => call.ResultJson = value);
        }

        if (delta.Error is not null)
        {
            changed |= SetIfDifferent(call.Error, delta.Error, value => call.Error = value);
        }

        if (call.IsRedacted != delta.IsRedacted)
        {
            call.IsRedacted = delta.IsRedacted;
            changed = true;
        }

        if (changed)
        {
            MarkStreaming(message);
        }

        return changed;
    }

    private static bool ApplyCitation(AiMessage message, AiCitationAddedDelta delta)
    {
        // A citation delta that cannot be addressed is a NO-OP, not an exception. The stream is fed by a
        // network transport, so a malformed or partial delta is an input the applier has to survive: throwing
        // here would tear down the consumer's read loop and lose the rest of the answer. Nothing is added to
        // the message, and the return value tells the caller no render is needed.
        var source = delta.Source;
        if (source is null || string.IsNullOrWhiteSpace(source.Id))
        {
            return false;
        }

        var part = message.Parts.OfType<AiSourcesPart>().LastOrDefault();
        if (part is null)
        {
            part = new AiSourcesPart();
            message.Parts.Add(part);
        }

        if (!part.Add(source))
        {
            return false;
        }

        MarkStreaming(message);
        return true;
    }

    private static bool ApplyCompleted(AiMessage message)
    {
        if (message.Status is AiMessageStatus.Completed)
        {
            return false;
        }

        if (message.Status is AiMessageStatus.Failed or AiMessageStatus.Stopped)
        {
            // A failure stays a failure; a stopped answer stays stopped.
            return false;
        }

        message.Status = AiMessageStatus.Completed;
        return true;
    }

    private static bool ApplyError(AiMessage message, AiErrorDelta delta)
    {
        // A finished or user-stopped answer stays that way: an error arriving after the fact (a transport
        // aborting a cancelled request, a late timeout) must not rewrite a Completed/Stopped turn into a
        // failure. This is the one transition Apply lets through the terminal gate, so it re-checks here.
        if (message.Status is AiMessageStatus.Completed or AiMessageStatus.Stopped)
        {
            return false;
        }

        var error = delta.Message ?? string.Empty;
        if (message.Status is AiMessageStatus.Failed
            && string.Equals(message.Error, error, StringComparison.Ordinal))
        {
            return false;
        }

        message.Status = AiMessageStatus.Failed;
        message.Error = error;
        return true;
    }

    private static void MarkStreaming(AiMessage message)
    {
        if (message.Status is AiMessageStatus.Pending)
        {
            message.Status = AiMessageStatus.Streaming;
        }
    }

    private static AiToolCall? FindToolCall(AiMessage message, string? id)
        => message.Parts.OfType<AiToolCallPart>()
            .Select(part => part.ToolCall)
            .LastOrDefault(call => string.Equals(call.Id, id ?? string.Empty, StringComparison.Ordinal));

    private static bool SetIfDifferent(string? current, string? next, Action<string?> assign)
    {
        if (string.Equals(current, next, StringComparison.Ordinal))
        {
            return false;
        }

        assign(next);
        return true;
    }
}
