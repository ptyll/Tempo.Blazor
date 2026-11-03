using FluentAssertions;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.Observability;

/// <summary>
/// Plan ff043b2e phase 6 review: <see cref="TraceSpan"/>'s two default maps are documented as
/// "empty, shared, read-only" and are shared by every span that does not set them. A shared instance
/// that a caller can cast back to a mutable dictionary is a trap — one
/// <c>((IDictionary)span.Metrics)["x"] = 1</c> would change the default of EVERY span in the process.
/// These facts pin the sharing AND the immutability.
/// </summary>
public class TraceSpanTests
{
    [Fact]
    public void TheDefaultMetricsMap_IsSharedAndReallyReadOnly()
    {
        var span = new TraceSpan { Id = "span" };
        var other = new TraceSpan { Id = "other" };

        span.Metrics.Should().BeEmpty();
        span.Metrics.Should().BeSameAs(other.Metrics, "the documented default is ONE shared map");

        var mutate = () => ((IDictionary<string, double>)span.Metrics)["tokens_in"] = 1;

        mutate.Should().Throw<NotSupportedException>(
            "a mutable shared default would let one caller change every default span");
        other.Metrics.Should().BeEmpty("the shared empty default never changes");
    }

    [Fact]
    public void TheDefaultAttributesMap_IsSharedAndReallyReadOnly()
    {
        var span = new TraceSpan { Id = "span" };
        var other = new TraceSpan { Id = "other" };

        span.Attributes.Should().BeEmpty();
        span.Attributes.Should().BeSameAs(other.Attributes, "the documented default is ONE shared map");

        var mutate = () => ((IDictionary<string, string>)span.Attributes)["model"] = "gpt-4o";

        mutate.Should().Throw<NotSupportedException>(
            "a mutable shared default would let one caller change every default span");
        other.Attributes.Should().BeEmpty("the shared empty default never changes");
    }
}
