using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Components.Notifications;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Overlay;

/// <summary>
/// N326: every <see cref="TmOverlayPanel"/> rendered with <c>Role="dialog"</c> must expose an
/// accessible name — <c>aria-label</c>, or <c>aria-labelledby</c> whose ids resolve to elements
/// present while the panel is open. The denominator is source-derived (every
/// <c>&lt;TmOverlayPanel … Role="dialog"&gt;</c> tag under <c>src/</c>), so a future unnamed
/// consumer fails here instead of silently shipping an anonymous dialog.
/// </summary>
public sealed class TmOverlayPanelDialogNamingTests : LocalizationTestBase
{
    private sealed record Product(int Id, string Name, string Category);

    public TmOverlayPanelDialogNamingTests()
    {
        Services.AddSingleton<ITmNotificationService>(new InMemoryNotificationStore());
        Services.AddSingleton<NavigationManager>(new FakeNavManager());
    }

    [Fact]
    public void EveryOverlayPanelWithDialogRole_HasAccessibleName()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var sites = OverlayDialogNaming.FindDialogRoleSites(root);
        var renderers = PanelRenderers();

        sites.Should().NotBeEmpty(
            "the sweep's denominator comes from <TmOverlayPanel … Role=\"dialog\"> tags in src/; " +
            "zero means the extractor broke, not that nothing needs checking");

        var failures = new List<string>();
        foreach (var site in sites)
        {
            if (!site.DeclaresName)
            {
                failures.Add($"{site.Component} ({site.File}): the dialog-role tag declares " +
                    "neither AriaLabel nor AriaLabelledBy");
                continue;
            }

            if (!renderers.TryGetValue(site.Component, out var open))
            {
                failures.Add($"unmeasurable:{site.Component} ({site.File}): declares a name but " +
                    "no bUnit renderer is registered — add one to PanelRenderers");
                continue;
            }

            var panel = open();
            var label = panel.GetAttribute("aria-label");
            var labelledBy = panel.GetAttribute("aria-labelledby");

            if (!string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(labelledBy))
            {
                failures.Add($"{site.Component} ({site.File}): rendered panel has no accessible name");
                continue;
            }

            var scope = RootScope(panel);
            foreach (var id in labelledBy.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (scope.QuerySelectorAll($"[id='{id}']").Length == 0)
                {
                    failures.Add($"{site.Component} ({site.File}): aria-labelledby references " +
                        $"missing id '{id}' while the panel is open");
                }
            }
        }

        failures.Should().BeEmpty("every dialog-role overlay panel must render an accessible " +
            "name (WCAG 4.1.2). Failures: " + string.Join("; ", failures));
    }

    [Fact]
    public void Detector_DialogRoleTag_IsExtracted()
    {
        const string razor = """
            <TmOverlayPanel IsOpen="_open" Role="dialog" AriaLabel="Pick">
                <p>x</p>
            </TmOverlayPanel>
            """;

        var sites = OverlayDialogNaming.DialogRoleSitesIn(razor, "src/Fake.razor");

        sites.Should().ContainSingle(site =>
            site.Component == "Fake" && site.DeclaresName);
    }

    [Fact]
    public void Detector_UnnamedDialogRoleTag_IsFlagged()
    {
        const string razor = """
            <TmOverlayPanel IsOpen="_open" Role="dialog" Class="tm-x__panel">
                <p>x</p>
            </TmOverlayPanel>
            """;

        var sites = OverlayDialogNaming.DialogRoleSitesIn(razor, "src/Fake.razor");

        sites.Should().ContainSingle(site => !site.DeclaresName);
    }

    [Fact]
    public void Detector_NonDialogRoles_AreNotCounted()
    {
        const string razor = """
            <TmOverlayPanel IsOpen="_open" Role="tooltip"><p>x</p></TmOverlayPanel>
            <TmOverlayPanel IsOpen="_open"><p>y</p></TmOverlayPanel>
            """;

        OverlayDialogNaming.DialogRoleSitesIn(razor, "src/Fake.razor").Should().BeEmpty();
    }

    // ── bUnit renderers, one per source site. Render the component, open its panel,
    //    and return the [role=dialog] element. ──

    private Dictionary<string, Func<IElement>> PanelRenderers() =>
        new(StringComparer.Ordinal)
        {
            ["TmNotificationBell"] = () =>
            {
                var cut = Render<TmNotificationBell>();
                cut.Find(".tm-notification-bell__button").Click();
                return cut.Find("[role='dialog']");
            },
            ["TmNotionNotificationCenter"] = () =>
            {
                var cut = Render<TmNotionNotificationCenter>(p => p
                    .Add(c => c.CurrentUserId, "alice")
                    .Add(c => c.PollInterval, TimeSpan.Zero));
                cut.Find("[data-testid='notion-notification-toggle']").Click();
                return cut.Find("[role='dialog']");
            },
            ["TmColorPicker"] = () =>
            {
                var cut = Render<TmColorPicker>();
                cut.Find(".tm-color-picker-trigger").Click();
                return cut.Find("[role='dialog']");
            },
            ["TmMultiColumnComboBox"] = () =>
            {
                var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
                    .Add(c => c.Data, new List<Product> { new(1, "Laptop", "Electronics") })
                    .Add(c => c.ValueField, x => x.Id)
                    .Add(c => c.TextField, x => x.Name)
                    .Add(c => c.Columns, new List<MultiColumnComboBoxColumn<Product>>
                    {
                        new() { Title = "Name", Field = x => x.Name },
                    }));
                cut.Find(".tm-multi-column-combo-box__trigger").Click();
                return cut.Find("[role='dialog']");
            },
            ["TmDatePicker"] = () =>
            {
                var cut = Render<TmDatePicker>(p => p.Add(c => c.Label, "Due date"));
                cut.Find(".tm-date-picker-trigger").Click();
                return cut.Find("[role='dialog']");
            },
            ["TmDateRangePicker"] = () =>
            {
                var cut = Render<TmDateRangePicker>(p => p.Add(c => c.Label, "Period"));
                cut.Find(".tm-date-range-trigger").Click();
                return cut.Find("[role='dialog']");
            },
            ["TmDateTimePicker"] = () =>
            {
                var cut = Render<TmDateTimePicker>(p => p.Add(c => c.Label, "Start"));
                cut.Find(".tm-date-picker-trigger").Click();
                return cut.Find("[role='dialog']");
            },
            ["TmNotionAiMenu"] = () =>
            {
                var cut = Render<TmNotionAiMenu>(p => p
                    .Add(c => c.Visible, true)
                    .Add(c => c.Provider, Substitute.For<INotionAIProvider>()));
                return cut.Find("[role='dialog']");
            },
            ["TmNotionMentionMenu"] = () =>
            {
                var host = Render<CascadingValue<NotionEditorContext>>(p => p
                    .Add(c => c.Value, new NotionEditorContext())
                    .AddChildContent<TmNotionMentionMenu>(m => m
                        .Add(x => x.Visible, true)
                        .Add(x => x.Top, 100)
                        .Add(x => x.Left, 200)));
                return host.Find("[role='dialog']");
            },
        };

    private static IParentNode RootScope(IElement panel)
    {
        INode node = panel;
        while (node.Parent is { } parent)
        {
            node = parent;
        }

        return (IParentNode)node;
    }

    private sealed class FakeNavManager : NavigationManager
    {
        public FakeNavManager()
        {
            Initialize("https://localhost/", "https://localhost/");
        }

        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
}

/// <summary>Source extractor for the N326 denominator: dialog-role TmOverlayPanel tags.</summary>
internal static class OverlayDialogNaming
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex OverlayPanelTag =
        new(@"<TmOverlayPanel\b[^>]*?>", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex DialogRole =
        new(@"\bRole\s*=\s*""dialog""", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex NameAttribute =
        new(@"\bAriaLabel(ledBy)?\s*=", RegexOptions.Compiled, RegexTimeout);

    /// <summary>Every file under src/ containing a dialog-role TmOverlayPanel tag.</summary>
    public static IReadOnlyList<OverlayDialogSite> FindDialogRoleSites(string repositoryRoot) =>
        Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.razor", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => DialogRoleSitesIn(File.ReadAllText(path), path))
            .ToList();

    public static IReadOnlyList<OverlayDialogSite> DialogRoleSitesIn(string razor, string path)
    {
        var sites = new List<OverlayDialogSite>();
        var component = Path.GetFileNameWithoutExtension(path);
        foreach (Match tag in OverlayPanelTag.Matches(razor))
        {
            if (!DialogRole.IsMatch(tag.Value))
            {
                continue;
            }

            sites.Add(new OverlayDialogSite(component, path, NameAttribute.IsMatch(tag.Value)));
        }

        return sites;
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}

internal readonly record struct OverlayDialogSite(string Component, string File, bool DeclaresName);
