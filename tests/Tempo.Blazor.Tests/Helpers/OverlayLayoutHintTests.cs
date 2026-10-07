using FluentAssertions;
using Microsoft.Extensions.Logging;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Tests.Helpers;

public class OverlayLayoutHintTests
{
    private sealed class ListLogger : ILogger
    {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Levels.Add(logLevel);
    }

    [Fact]
    public void Hint_LogsOnceAtInformation_WhateverTheEnvironment()
    {
        OverlayLayout.ResetHint();
        var logger = new ListLogger();

        OverlayLayout.HintOnce(logger);
        OverlayLayout.HintOnce(logger);

        logger.Levels.Should().Equal(LogLevel.Information);
    }
}
