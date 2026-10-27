using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class TmModelingSourcePanelTests : LocalizationTestBase
{
    [Fact]
    public void Clicking_load_model_raises_on_load_requested()
    {
        var requested = false;
        using var cut = Render<TmModelingSourcePanel>(parameters => parameters
            .Add(p => p.Metadata, CreateMetadata(isFresh: true))
            .Add(p => p.OnLoadRequested, EventCallback.Factory.Create(this, () => requested = true)));

        cut.Find("[data-testid='modeling-source-load-button']").Click();

        requested.Should().BeTrue();
    }

    [Fact]
    public void Stale_metadata_shows_warning_with_icon()
    {
        using var cut = Render<TmModelingSourcePanel>(parameters => parameters
            .Add(p => p.Metadata, CreateMetadata(isFresh: false)));

        var warning = cut.Find("[data-testid='modeling-source-freshness-warning']");

        warning.Should().NotBeNull();
        warning.QuerySelector(".tm-modeling-source-panel__warning-icon").Should().NotBeNull();
        warning.QuerySelector(".tm-modeling-source-panel__warning-icon svg").Should().NotBeNull(
            "the stale warning carries an icon, not a text glyph");
        warning.TextContent.Should().NotContain("!");
    }

    [Fact]
    public void Stale_badge_renders_icon_and_single_freshness_label()
    {
        using var cut = Render<TmModelingSourcePanel>(parameters => parameters
            .Add(p => p.Metadata, CreateMetadata(isFresh: false)));

        var badge = cut.FindComponents<TmBadge>().Should().ContainSingle().Subject;

        badge.Instance.Variant.Should().Be(BadgeVariant.Warning);
        badge.Instance.Icon.Should().Be(IconNames.AlertTriangle);
        badge.Find("[data-testid='modeling-source-freshness-indicator']").TextContent.Should().Contain("Stale");

        // Phase 2: the duplicate header subtitle is gone — the freshness label renders ONCE.
        var header = cut.Find(".tm-modeling-source-panel__header");
        Regex.Matches(header.TextContent, "Stale").Should().HaveCount(1,
            "the freshness label lives only on the badge, not as a second subtitle line");
    }

    [Fact]
    public void Fresh_badge_uses_success_variant_with_check_icon()
    {
        using var cut = Render<TmModelingSourcePanel>(parameters => parameters
            .Add(p => p.Metadata, CreateMetadata(isFresh: true)));

        var badge = cut.FindComponents<TmBadge>().Should().ContainSingle().Subject;

        badge.Instance.Variant.Should().Be(BadgeVariant.Success);
        badge.Instance.Icon.Should().Be(IconNames.CheckCircle);
    }

    [Fact]
    public void Load_button_is_tm_button_with_refresh_icon_and_loading_state()
    {
        var requested = 0;
        using var cut = Render<TmModelingSourcePanel>(parameters => parameters
            .Add(p => p.Metadata, CreateMetadata(isFresh: true))
            .Add(p => p.IsLoading, false)
            .Add(p => p.OnLoadRequested, EventCallback.Factory.Create(this, () => requested++)));

        var loadButton = cut.FindComponent<TmButton>();
        loadButton.Instance.Variant.Should().Be(ButtonVariant.Primary);
        loadButton.Instance.Icon.Should().Be(IconNames.Refresh);
        loadButton.Instance.IsLoading.Should().BeFalse();

        cut.Find("[data-testid='modeling-source-load-button']").Click();
        requested.Should().Be(1);

        using var busy = Render<TmModelingSourcePanel>(parameters => parameters
            .Add(p => p.Metadata, CreateMetadata(isFresh: true))
            .Add(p => p.IsLoading, true)
            .Add(p => p.OnLoadRequested, EventCallback.Factory.Create(this, () => requested++)));

        var busyButton = busy.FindComponent<TmButton>();
        busyButton.Instance.IsLoading.Should().BeTrue("the host-reported load state drives the TmButton loading state");
        busyButton.Instance.LoadingText.Should().Be("Loading model");

        busy.Find("[data-testid='modeling-source-load-button']").Click();
        requested.Should().Be(1, "the double-click guard blocks a re-entry while a load is pending");
    }

    [Fact]
    public void Fresh_metadata_does_not_show_stale_warning()
    {
        using var cut = Render<TmModelingSourcePanel>(parameters => parameters
            .Add(p => p.Metadata, CreateMetadata(isFresh: true)));

        cut.FindAll("[data-testid='modeling-source-freshness-warning']").Should().BeEmpty();
    }

    [Fact]
    public void Null_metadata_renders_empty_state_without_exception()
    {
        using var cut = Render<TmModelingSourcePanel>(parameters => parameters
            .Add(p => p.Metadata, (ModelingMetadataDto?)null));

        cut.Find("[data-testid='modeling-source-panel']").Should().NotBeNull();
        cut.Find("[data-testid='modeling-source-empty']").Should().NotBeNull();
        cut.FindAll("[data-testid='modeling-source-freshness-indicator']").Should().BeEmpty(
            "no freshness badge without metadata");
    }

    [Fact]
    public void Metadata_rows_render_icons_in_two_column_list()
    {
        using var cut = Render<TmModelingSourcePanel>(parameters => parameters
            .Add(p => p.Metadata, CreateMetadata(isFresh: true)));

        var rows = cut.FindAll(".tm-modeling-source-panel__metadata-row");
        rows.Should().HaveCount(3);
        rows.Select(row => row.QuerySelector(".tm-modeling-source-panel__metadata-icon svg"))
            .Should().OnlyContain(icon => icon != null, "every metadata row carries its own icon");

        var icons = cut.FindComponents<TmIcon>().Select(icon => icon.Instance.Name).ToList();
        icons.Should().Contain(IconNames.Database);
        icons.Should().Contain(IconNames.Tag);
        icons.Should().Contain(IconNames.Calendar);
    }

    [Fact]
    public void Long_source_system_uses_wrapping_value_element()
    {
        var sourceSystem = new string('A', 200);
        var metadata = CreateMetadata(isFresh: true);
        metadata.SourceSystem = sourceSystem;

        using var cut = Render<TmModelingSourcePanel>(parameters => parameters
            .Add(p => p.Metadata, metadata));

        var sourceSystemElement = cut.Find("[data-testid='modeling-source-system']");

        sourceSystemElement.TextContent.Should().Be(sourceSystem);
        sourceSystemElement.GetAttribute("title").Should().Be(sourceSystem);
        sourceSystemElement.ClassList.Should().Contain("tm-modeling-source-panel__value");
    }

    private static ModelingMetadataDto CreateMetadata(bool isFresh) => new()
    {
        SourceSystem = "Tempo tests",
        SourceVersion = "1.0",
        LoadedAt = new DateTimeOffset(2026, 6, 6, 8, 0, 0, TimeSpan.Zero),
        IsFresh = isFresh
    };
}
