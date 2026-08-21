using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.Api.Data;

namespace Tempo.Blazor.Demo.Api.Tests;

/// <summary>
/// Reads back, out of a BOOTED host, which SQLite file this lane writes its diagrams to.
/// <para>
/// WHY THIS IS ASSERTED ON THE HOST AND NOT ON THE ENVIRONMENT VARIABLE. Asserting that
/// <see cref="DemoDatabaseRedirect"/> set its own variable would be a test of one line against
/// itself; it would stay green if the host stopped reading that key, if the key were misspelled on
/// either side, or if configuration from some other source outranked it. What the working tree
/// cares about is the DESTINATION, so the destination is what is measured — through
/// <c>DbConnection.DataSource</c>, which is the path the provider actually opened.
/// </para>
/// <para>
/// THE POSITIVE CONTROL IS PART OF THE TEST, not a remark next to it. "The host does not write the
/// committed file" is satisfied vacuously if the path this test compares against is wrong — a typo
/// in the expected location would make the inequality trivially true and the guard permanently
/// green. So the committed file's existence is asserted first: the needle is shown to reach a real
/// file before it is used to prove the host stays away from it.
/// </para>
/// <para>
/// WHAT A GREEN HERE DOES NOT PROVE. It measures ONE destination. It does not prove the committed
/// database is byte-identical after the whole suite — that is the acceptance criterion of the run
/// itself (<c>git status --porcelain</c> after suite and pack identical to before, with no hand-run
/// restore), and a unit test has no pack to observe. And it says nothing about the lanes the CI
/// filter excludes, <c>Tempo.Blazor.E2E</c> and <c>Tempo.ReportServer.Api.Tests.MsSql</c>, whose
/// tracked-file footprint nobody has counted.
/// </para>
/// </summary>
public sealed class DemoDatabaseRedirectTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DemoDatabaseRedirectTests(WebApplicationFactory<Program> factory) => _factory = factory;

    /// <summary>
    /// The booted host opens the redirected database, not the committed one, and it really creates
    /// the redirected file.
    /// </summary>
    [Fact]
    public void BootedHost_WritesItsDiagramsOutsideTheWorkingTree()
    {
        var committed = DemoDatabaseRedirect.CommittedDatabasePath;

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DemoDiagramDbContext>();
        var opened = Path.GetFullPath(context.Database.GetDbConnection().DataSource);

        using (new AssertionScope())
        {
            File.Exists(committed).Should().BeTrue(
                "the comparison below is only meaningful if it names a file that exists; a wrong "
                + "path here would make 'the host does not write the committed database' true for "
                + "the wrong reason and this guard permanently green. Looked for: " + committed);

            opened.Should().NotBe(
                committed,
                "a test run that writes the committed SQLite file leaves the working tree dirty on "
                + "every green run, and eng/pack-nuget-packages.sh refuses a dirty tree — so 'run "
                + "the suite, then pack' would need a hand-run git checkout in between. Measured "
                + "before the redirect existed: c5221512… became 73967287… over 208 passing tests");

            opened.Should().Be(
                Path.GetFullPath(DemoDatabaseRedirect.DatabasePath),
                "the redirect is the one place this lane's destination is chosen, so a host that "
                + "opened some third path would mean the key is being resolved by something else "
                + "and this guard would be watching the wrong mechanism");

            File.Exists(opened).Should().BeTrue(
                "the redirect does not merely NAME a path, it prepares one: DemoDatabaseRedirect "
                + "seeds the file from the committed database and creates the schema once, "
                + "single-threaded, before any host exists. A path announced but never materialised "
                + "would put every host back into the concurrent EnsureCreated() the committed "
                + "file's schema used to mask — measured as 7 of 209 failing with 'table "
                + "\"DiagramSnapshots\" already exists' the first time the redirect landed without "
                + "this preparation");
        }
    }
}
