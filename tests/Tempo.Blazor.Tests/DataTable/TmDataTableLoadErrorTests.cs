using Bunit;
using FluentAssertions;
using FluentAssertions.Specialized;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Fáze 7 (opt-in per decision EMUSCZ-2.12-BREAKING-POINTS (b)): when the host registers
/// <see cref="TmDataTable{TItem}.OnLoadError"/> or supplies ErrorTemplate/ErrorText, a provider
/// exception is caught — previous rows stay, an inline error box with Retry is rendered and the
/// callback hears the exception. Without an opt-in the behavior is unchanged (the exception
/// propagates). OperationCanceledException is NEVER caught; a superseded response is dropped.
/// </summary>
public class TmDataTableLoadErrorTests : LocalizationTestBase
{
    public record LoadPerson(string Name);

    private static PagedResult<LoadPerson> Ok(int page = 1) => new()
    {
        Items = [new("Alice"), new("Bob")],
        TotalCount = 2,
        Page = page,
        PageSize = 25,
    };

    private IDataTableDataProvider<LoadPerson> Provider()
    {
        var provider = Substitute.For<IDataTableDataProvider<LoadPerson>>();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Ok()));
        return provider;
    }

    [Fact]
    public async Task NoOptIn_ProviderThrow_Propagates()
    {
        var provider = Provider();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        FluentActions.Invoking(() => Render<TmDataTable<LoadPerson>>(p => p.Add(c => c.DataProvider, provider)))
            .Should().Throw<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task OptIn_ProviderThrow_ShowsErrorBox_KeepsPreviousRows()
    {
        var provider = Provider();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Ok()), _ => throw new InvalidOperationException("boom"));

        var cut = Render<TmDataTable<LoadPerson>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.OnLoadError, EventCallback.Factory.Create<Exception>(this, _ => { }));
        });
        await cut.InvokeAsync(() => { });
        cut.FindAll("tbody tr").Count.Should().Be(2);

        // trigger a second load that throws (change the search text)
        cut.Find(".tm-data-table-search input, input[type='search']").Change("x");
        await cut.InvokeAsync(() => { });

        cut.Find(".tm-data-table-load-error").Should().NotBeNull();
        cut.Find(".tm-data-table-load-error").TextContent.Should().Contain("Loading the data failed");
        cut.FindAll("tbody tr").Count.Should().Be(2, "the previous rows stay while the load failed");
    }

    [Fact]
    public async Task OptIn_Retry_LoadsAgain_AndClearsTheError()
    {
        var provider = Provider();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Ok()), _ => throw new InvalidOperationException("boom"), _ => Task.FromResult(Ok()));

        var cut = Render<TmDataTable<LoadPerson>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.OnLoadError, EventCallback.Factory.Create<Exception>(this, _ => { }));
        });
        await cut.InvokeAsync(() => { });
        cut.Find("input[type='search']").Change("x");
        await cut.InvokeAsync(() => { });
        cut.Find(".tm-data-table-load-error").Should().NotBeNull();

        cut.Find(".tm-data-table-load-error__retry").Click();
        await cut.InvokeAsync(() => { });

        cut.FindAll(".tm-data-table-load-error").Should().BeEmpty();
        cut.FindAll("tbody tr").Count.Should().Be(2);
    }

    [Fact]
    public async Task OptIn_OnLoadError_ReceivesTheException()
    {
        Exception? received = null;
        var provider = Provider();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var cut = Render<TmDataTable<LoadPerson>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.OnLoadError, EventCallback.Factory.Create<Exception>(this, e => received = e));
        });
        await cut.InvokeAsync(() => { });

        received.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task ErrorText_OverridesTheLocalizedDefault()
    {
        var provider = Provider();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var cut = Render<TmDataTable<LoadPerson>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.ErrorText, "The jobs could not be loaded.");
        });
        await cut.InvokeAsync(() => { });

        cut.Find(".tm-data-table-load-error").TextContent.Should().Contain("The jobs could not be loaded.");
    }

    [Fact]
    public async Task ErrorTemplate_WinsOverErrorText()
    {
        var provider = Provider();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var cut = Render<TmDataTable<LoadPerson>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.ErrorText, "plain text");
            p.Add(c => c.ErrorTemplate, ex => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "my-error-template");
                builder.AddContent(2, $"custom {ex.Message}");
                builder.CloseElement();
            });
        });
        await cut.InvokeAsync(() => { });

        cut.Find(".my-error-template").TextContent.Should().Contain("custom boom");
        cut.Find(".tm-data-table-load-error").TextContent.Should().NotContain("plain text");
    }

    [Fact]
    public async Task OperationCanceledException_IsNeverCaught()
    {
        Exception? received = null;
        var provider = Provider();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        // The opt-in must NOT treat a cancellation as a load error: no error box, no callback.
        // bUnit swallows the escaped OperationCanceledException itself; the component contract we
        // can observe here is that the cancellation never becomes error state.
        Render<TmDataTable<LoadPerson>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.OnLoadError, EventCallback.Factory.Create<Exception>(this, e => received = e));
        });

        received.Should().BeNull("a cancellation is not a load error — OnLoadError must never hear it");
    }

    [Fact]
    public async Task SupersededError_IsDropped()
    {
        Exception? received = null;
        var gate = new TaskCompletionSource();
        var call = 0;
        var provider = Substitute.For<IDataTableDataProvider<LoadPerson>>();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                call++;
                return call switch
                {
                    1 => Task.FromResult(Ok()),
                    // hangs until the gate opens, then fails "stale"
                    2 => gate.Task.ContinueWith(_ => Task.FromException<PagedResult<LoadPerson>>(new InvalidOperationException("stale")), TaskScheduler.Default).Unwrap(),
                    _ => Task.FromResult(Ok()),
                };
            });

        var cut = Render<TmDataTable<LoadPerson>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.OnLoadError, EventCallback.Factory.Create<Exception>(this, e => received = e));
        });
        await cut.InvokeAsync(() => { });

        // Load 2 hangs until the gate opens; load 3 (a newer generation) completes first. The stale
        // failure of load 2 must be dropped: no error box, no callback, fresh rows.
        await cut.InvokeAsync(() => cut.Find("input[type='search']").Change("a"));
        await cut.InvokeAsync(() => cut.Find("input[type='search']").Change("ab"));
        gate.TrySetResult();
        await cut.InvokeAsync(() => { });

        cut.FindAll(".tm-data-table-load-error").Should().BeEmpty();
        received.Should().BeNull();
        cut.FindAll("tbody tr").Count.Should().Be(2);
    }
}
