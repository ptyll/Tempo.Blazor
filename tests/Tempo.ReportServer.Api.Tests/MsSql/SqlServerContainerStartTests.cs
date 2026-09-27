using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Tempo.ReportServer.TestSupport;

namespace Tempo.ReportServer.Api.Tests.MsSql;

/// <summary>
/// The retry rule of <see cref="SqlServerContainerStart"/>, measured without Docker: which failures earn a
/// fresh container, how many, and that every failed candidate is disposed.
/// </summary>
public sealed class SqlServerContainerStartTests
{
    private sealed class Candidate
    {
        public required int Number { get; init; }

        public bool Disposed { get; set; }
    }

    private static ContainerNotRunningException StartUpCrash(int number) =>
        new(
            "candidate-" + number,
            "SQL Server 2022 will run as non-root by default.",
            "ERROR: CoInitializeSecurity failure. (HRESULT 0x800706b5)",
            255,
            new InvalidOperationException("container is not running"));

    [Fact]
    public async Task AContainerThatExitsDuringStartUp_IsReplacedByAFreshOne()
    {
        var built = new List<Candidate>();

        var started = await SqlServerContainerStart.StartAsync(
            () =>
            {
                var candidate = new Candidate { Number = built.Count + 1 };
                built.Add(candidate);
                return candidate;
            },
            candidate => candidate.Number == 1 ? Task.FromException(StartUpCrash(1)) : Task.CompletedTask,
            candidate =>
            {
                candidate.Disposed = true;
                return ValueTask.CompletedTask;
            });

        started.Number.Should().Be(2, "the second, fresh container is the one the fixture gets");
        built.Should().HaveCount(2);
        built[0].Disposed.Should().BeTrue("the crashed candidate must not be left behind");
        built[1].Disposed.Should().BeFalse("the running candidate belongs to the fixture now");
    }

    [Fact]
    public async Task AStartUpCrashOnEveryAttempt_IsTheResult_AfterTheBoundedNumberOfAttempts()
    {
        var built = new List<Candidate>();

        var act = () => SqlServerContainerStart.StartAsync(
            () =>
            {
                var candidate = new Candidate { Number = built.Count + 1 };
                built.Add(candidate);
                return candidate;
            },
            candidate => Task.FromException(StartUpCrash(candidate.Number)),
            candidate =>
            {
                candidate.Disposed = true;
                return ValueTask.CompletedTask;
            });

        (await act.Should().ThrowAsync<ContainerNotRunningException>())
            .WithMessage("*candidate-" + SqlServerContainerStart.Attempts + "*",
                "the LAST crash propagates unchanged — the fixture then reports it as before");
        built.Should().HaveCount(SqlServerContainerStart.Attempts, "the retry is bounded");
        built.Should().OnlyContain(candidate => candidate.Disposed, "every crashed candidate is disposed");
    }

    [Fact]
    public async Task AnyOtherStartFailure_IsNotRetried()
    {
        var built = new List<Candidate>();

        var act = () => SqlServerContainerStart.StartAsync(
            () =>
            {
                var candidate = new Candidate { Number = built.Count + 1 };
                built.Add(candidate);
                return candidate;
            },
            _ => Task.FromException(new InvalidOperationException("Docker is not reachable")),
            candidate =>
            {
                candidate.Disposed = true;
                return ValueTask.CompletedTask;
            });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Docker is not reachable");
        built.Should().ContainSingle(
            "only a container that exited during start-up earns a fresh one — an unreachable Docker, a "
            + "failed pull or a readiness timeout is still a red on the first attempt");
        built[0].Disposed.Should().BeTrue();
    }
}
