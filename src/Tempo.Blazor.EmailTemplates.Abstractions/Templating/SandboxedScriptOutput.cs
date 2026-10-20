using System.Diagnostics;
using System.Text;
using Scriban.Runtime;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// A Scriban output that enforces the output-length and wall-clock limits cooperatively on every
/// write, so a runaway template that keeps producing output is aborted promptly. It is the last line of
/// defence behind the context's own <c>OutputLimit</c>: even if a future Scriban version stopped
/// honouring that limit, the render still cannot exceed
/// <see cref="TemplateSecurityOptions.MaxOutputLength"/> or <see cref="TemplateSecurityOptions.Timeout"/>.
/// </summary>
internal sealed class SandboxedScriptOutput : IScriptOutput
{
    private readonly StringBuilder _builder = new();
    private readonly int _maxLength;
    private readonly TimeSpan _timeout;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public SandboxedScriptOutput(int maxLength, TimeSpan timeout)
    {
        _maxLength = maxLength;
        _timeout = timeout;
    }

    public void Write(string text, int offset, int count)
        => Append(text, offset, count);

    public ValueTask WriteAsync(string text, int offset, int count, CancellationToken cancellationToken)
    {
        Append(text, offset, count);
        return default;
    }

    private void Append(string text, int offset, int count)
    {
        if (_stopwatch.Elapsed > _timeout)
        {
            throw new TemplateSecurityViolationException(
                TemplateSecurityEventKind.Timeout,
                "render time budget");
        }

        if (_builder.Length + count > _maxLength)
        {
            throw new TemplateSecurityViolationException(
                TemplateSecurityEventKind.LimitExceeded,
                "MaxOutputLength");
        }

        _builder.Append(text, offset, count);
    }

    public override string ToString() => _builder.ToString();
}
