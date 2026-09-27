using DotNet.Testcontainers.Containers;

namespace Tempo.ReportServer.TestSupport;

/// <summary>
/// Starts a throwaway SQL Server container, taking a FRESH container when the previous one exited
/// during start-up. Compiled into both <c>Tempo.ReportServer.Api.Tests</c> and (as a linked file)
/// <c>Tempo.ReportServer.Web.Tests</c>, so every Testcontainers SQL Server of the release gate starts
/// through the same rule.
/// <para>
/// THE FAILURE THIS IS FOR, observed rather than imagined: on ubuntu-latest the pinned
/// <c>mcr.microsoft.com/mssql/server</c> image occasionally dies about three seconds after
/// <c>docker start</c> with <c>ERROR: CoInitializeSecurity failure. (HRESULT 0x800706b5)</c> on stderr
/// and exit code 255 — twice on 2026-09-27 (build-and-test runs 36293856199 attempt 1, in
/// <c>SqlServerCacheFixture</c>, and 36293856211 attempt 2, in <c>MsSqlContainerFixture</c>, where it
/// took all 27 tests of the MsSql collection down with it). Nothing in the code under test is involved:
/// the engine never came up. Testcontainers reports that as <see cref="ContainerNotRunningException"/>.
/// </para>
/// <para>
/// ONLY THAT EXCEPTION IS RETRIED, and only <see cref="Attempts"/> times in all. Docker not being
/// reachable, an image that cannot be pulled, a readiness wait that times out on a container that is
/// still RUNNING — every other failure propagates on the first attempt exactly as before, so "a missing
/// service is a red, never a skip" still holds. Every candidate that failed is disposed before the next
/// is built, and the last failure propagates unchanged when all attempts crashed. The dispose is
/// best-effort: a container that crashed may also fail to be removed, and that secondary failure must
/// not replace the start-up exception, which is the finding.
/// </para>
/// <para>
/// A RETRY IS NEVER SILENT. Each replaced container writes one line to standard error (the test output
/// of the CI log) naming the attempt and Testcontainers' own first message line, which carries the exit
/// code — so a green run that needed a second container still says so.
/// </para>
/// </summary>
internal static class SqlServerContainerStart
{
    /// <summary>How many fresh containers one fixture may try before the start-up crash is the result.</summary>
    internal const int Attempts = 3;

    /// <summary>
    /// Builds a container with <paramref name="build"/> and starts it, retrying with a new container only
    /// when the previous one exited during start-up (<see cref="ContainerNotRunningException"/>).
    /// </summary>
    internal static Task<TContainer> StartAsync<TContainer>(Func<TContainer> build)
        where TContainer : IContainer
        => StartAsync(
            build,
            static container => container.StartAsync(),
            static container => container.DisposeAsync(),
            static line => Console.Error.WriteLine(line));

    /// <summary>
    /// The same rule with the start, dispose and log steps injectable, so the retry decision is testable
    /// without Docker.
    /// </summary>
    internal static async Task<TContainer> StartAsync<TContainer>(
        Func<TContainer> build,
        Func<TContainer, Task> start,
        Func<TContainer, ValueTask> dispose,
        Action<string> log)
    {
        for (var attempt = 1; ; attempt++)
        {
            var candidate = build();
            try
            {
                await start(candidate).ConfigureAwait(false);
                return candidate;
            }
            catch (Exception exception)
            {
                try
                {
                    await dispose(candidate).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort: the start-up exception below is the finding, not the cleanup.
                }

                if (exception is not ContainerNotRunningException || attempt >= Attempts)
                {
                    throw;
                }

                log(
                    "SqlServerContainerStart: attempt " + attempt + "/" + Attempts
                    + " exited during start-up, retrying with a fresh container: "
                    + FirstLine(exception.Message));
            }
        }
    }

    private static string FirstLine(string message)
    {
        var end = message.IndexOfAny(['\r', '\n']);
        return end < 0 ? message : message[..end];
    }
}
