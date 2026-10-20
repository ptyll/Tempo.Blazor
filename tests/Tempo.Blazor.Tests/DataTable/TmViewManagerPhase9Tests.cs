using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Phase 9 view-manager opt-ins: persisting the default through the provider, naming the star
/// for assistive tech, and showing the active view name on the toggle.
/// </summary>
public class TmViewManagerPhase9Tests : LocalizationTestBase
{
    private const string Context = "phase9";
    private const string User = "ada";

    private static DataTableView[] Views =>
    [
        new() { Id = "v1", Name = "All items", Scope = ViewScope.Personal, IsDefault = true, CreatedBy = User },
        new() { Id = "v2", Name = "Quarterly pipeline review for the northern region", Scope = ViewScope.Personal, IsDefault = false, CreatedBy = User },
    ];

    private IDataTableViewProvider Provider(List<DataTableView> views)
    {
        var provider = Substitute.For<IDataTableViewProvider>();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => views.ToList());
        provider.GetDefaultViewAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => views.FirstOrDefault(v => v.IsDefault));
        provider.SetDefaultViewAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var id = call.ArgAt<string>(1);
                foreach (var view in views)
                    view.IsDefault = view.Id == id;
                return Task.CompletedTask;
            });
        return provider;
    }

    [Fact]
    public void Without_a_provider_the_set_default_action_is_absent()
    {
        var cut = Render<TmViewManager>(p => p
            .Add(c => c.Provider, Provider(Views.Select(Clone).ToList()))
            .Add(c => c.ViewContext, Context)
            .Add(c => c.CurrentUserId, User)
            .Add(c => c.AllowSetDefault, true));

        cut.Find(".tm-view-manager-toggle").Click();

        // The action is present only because a provider exists. A host that passes no provider
        // cannot render the manager at all (Provider is required), so the gate is AllowSetDefault.
        cut.FindAll(".tm-view-set-default-btn").Should().NotBeEmpty();

        var hidden = Render<TmViewManager>(p => p
            .Add(c => c.Provider, Provider(Views.Select(Clone).ToList()))
            .Add(c => c.ViewContext, Context)
            .Add(c => c.CurrentUserId, User)
            .Add(c => c.AllowSetDefault, false));
        hidden.Find(".tm-view-manager-toggle").Click();
        hidden.FindAll(".tm-view-set-default-btn").Should().BeEmpty();
    }

    [Fact]
    public async Task Set_default_persists_through_the_provider_then_reloads_and_notifies()
    {
        var views = Views.Select(Clone).ToList();
        var provider = Provider(views);
        DataTableView? notified = null;
        var cut = Render<TmViewManager>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.ViewContext, Context)
            .Add(c => c.CurrentUserId, User)
            .Add(c => c.CurrentTenantId, "tenant")
            .Add(c => c.AllowSetDefault, true)
            .Add(c => c.OnSetDefault, EventCallback.Factory.Create<DataTableView>(this, v => notified = v)));

        await cut.InvokeAsync(() => { });
        cut.Find(".tm-view-manager-toggle").Click();

        var action = cut.FindAll(".tm-view-set-default-btn").Single();
        action.GetAttribute("aria-label").Should().Be("Set Quarterly pipeline review for the northern region as default");
        action.Click();
        await cut.InvokeAsync(() => { });

        await provider.Received(1).SetDefaultViewAsync(Context, "v2", "tenant", User, Arg.Any<CancellationToken>());
        // Initial load plus the reload after the write, on top of the refresh that opens the menu.
        await provider.Received().GetViewsAsync(Context, "tenant", User, Arg.Any<CancellationToken>());
        notified.Should().NotBeNull();
        notified!.Id.Should().Be("v2");

        var star = cut.FindAll(".tm-view-item")
            .Single(item => item.TextContent.Contains("Quarterly pipeline review"));
        star.QuerySelector(".tm-view-item-badge").Should().NotBeNull();
        star.QuerySelector(".tm-sr-only")!.TextContent.Should().Be("Default view");
        star.QuerySelector(".tm-view-item-badge")!.GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public async Task Set_default_callback_throw_is_not_reported_as_a_failed_save()
    {
        var views = Views.Select(Clone).ToList();
        var provider = Provider(views);
        var cut = Render<TmViewManager>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.ViewContext, Context)
            .Add(c => c.CurrentUserId, User)
            .Add(c => c.AllowSetDefault, true)
            .Add(c => c.OnSetDefault, EventCallback.Factory.Create<DataTableView>(this, _ => throw new InvalidOperationException("host"))));

        await cut.InvokeAsync(() => { });
        cut.Find(".tm-view-manager-toggle").Click();

        var thrown = false;
        try
        {
            cut.Find(".tm-view-set-default-btn").Click();
        }
        catch (InvalidOperationException)
        {
            thrown = true;
        }

        thrown.Should().BeTrue("the consumer exception must not be swallowed");
        await provider.Received(1).SetDefaultViewAsync(Context, "v2", Arg.Any<string?>(), User, Arg.Any<CancellationToken>());
        cut.FindAll(".tm-view-error").Should().BeEmpty();
        cut.Markup.Should().NotContain("Could not set the default view.");
    }

    [Fact]
    public void Active_view_announces_aria_current()
    {
        var cut = Render<TmViewManager>(p => p
            .Add(c => c.Provider, Provider(Views.Select(Clone).ToList()))
            .Add(c => c.ViewContext, Context)
            .Add(c => c.CurrentUserId, User));

        cut.Find(".tm-view-manager-toggle").Click();
        cut.FindAll(".tm-view-item").First().Click();
        cut.Find(".tm-view-manager-toggle").Click();

        cut.Find(".tm-view-item--active").GetAttribute("aria-current").Should().Be("true");
        cut.FindAll(".tm-view-item")
            .Where(item => !item.ClassList.Contains("tm-view-item--active"))
            .Select(item => item.GetAttribute("aria-current"))
            .Should().AllBeEquivalentTo((string?)null);
    }

    [Fact]
    public void ShowActiveViewName_puts_the_full_name_on_the_toggle_and_its_title()
    {
        var cut = Render<TmViewManager>(p => p
            .Add(c => c.Provider, Provider(Views.Select(Clone).ToList()))
            .Add(c => c.ViewContext, Context)
            .Add(c => c.CurrentUserId, User)
            .Add(c => c.ShowActiveViewName, true));

        cut.Find(".tm-view-manager-toggle").Click();
        cut.FindAll(".tm-view-item")[1].Click();

        var toggle = cut.Find(".tm-view-manager-toggle");
        toggle.TextContent.Should().Contain("Quarterly pipeline review for the northern region");
        toggle.GetAttribute("title").Should().Be("Quarterly pipeline review for the northern region");
        toggle.QuerySelector(".tm-view-manager-toggle-name").Should().NotBeNull();
    }

    [Fact]
    public void Default_toggle_keeps_the_generic_label_and_no_title()
    {
        var cut = Render<TmViewManager>(p => p
            .Add(c => c.Provider, Provider(Views.Select(Clone).ToList()))
            .Add(c => c.ViewContext, Context)
            .Add(c => c.CurrentUserId, User));

        var toggle = cut.Find(".tm-view-manager-toggle");
        toggle.TextContent.Should().Contain("Views");
        toggle.GetAttribute("title").Should().BeNull();
    }

    private static DataTableView Clone(DataTableView view) => new()
    {
        Id = view.Id,
        Name = view.Name,
        Scope = view.Scope,
        IsDefault = view.IsDefault,
        CreatedBy = view.CreatedBy,
    };
}
