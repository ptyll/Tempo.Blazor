using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

public sealed class UiRoleVocabularyTests
{
    [Fact]
    public void UiRoleDefinition_RequiresKebabCaseSlug()
    {
        var act = () => new UiRoleDefinition(
            "SearchInput",
            "Search input",
            "Allows users to search within a data set.",
            ["search box", "vyhledavaci pole"]);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("slug");
    }

    [Fact]
    public void UiRoleDefinition_PreservesDisplayNameDefinitionAndSynonyms()
    {
        var definition = new UiRoleDefinition(
            "search-input",
            "Search input",
            "Allows users to search within a data set.",
            ["search box", "vyhledavani", "TmSearchBox"]);

        definition.Slug.Should().Be("search-input");
        definition.DisplayName.Should().Be("Search input");
        definition.Definition.Should().Be("Allows users to search within a data set.");
        definition.Synonyms.Should().Equal("search box", "vyhledavani", "TmSearchBox");
    }

    [Fact]
    public void UiRoleVocabulary_MergesSourcesAndLooksUpSlugOrSynonymCaseInsensitively()
    {
        var vocabulary = new UiRoleVocabulary(
        [
            new TestVocabularySource(
                "base",
                0,
                new UiRoleDefinition("search-input", "Search input", "Searches records.", ["search box"])),
            new TestVocabularySource(
                "product",
                10,
                new UiRoleDefinition("otp-input", "OTP input", "Collects a one-time password.", ["TmOtpInput", "jednorazovy kod"]))
        ]);

        vocabulary.Find("SEARCH-INPUT")!.Slug.Should().Be("search-input");
        vocabulary.Find("Search Box")!.Slug.Should().Be("search-input");
        vocabulary.Find("tmotpinput")!.Slug.Should().Be("otp-input");
        vocabulary.Find("JEDNORAZOVY KOD")!.Slug.Should().Be("otp-input");
    }

    [Fact]
    public void UiRoleVocabulary_DeterministicallyOrdersRolesAndMergedSynonyms()
    {
        var vocabulary = new UiRoleVocabulary(
        [
            new TestVocabularySource(
                "z-source",
                0,
                new UiRoleDefinition("text-input", "Text input", "Accepts short text.", ["TmTextField", "input"])),
            new TestVocabularySource(
                "a-source",
                10,
                new UiRoleDefinition("badge", "Badge", "Highlights status or category.", ["stitek"])),
            new TestVocabularySource(
                "extension",
                5,
                new UiRoleDefinition("text-input", "Text input", "Accepts short text.", ["text field", "vstupni pole"]))
        ]);

        vocabulary.GetAll().Select(role => role.Slug).Should().Equal("badge", "text-input");
        vocabulary.Find("text-input")!.Synonyms.Should().Equal("input", "text field", "TmTextField", "vstupni pole");
    }

    [Fact]
    public void BuiltInVocabulary_ContainsBaselineRolesAndKnownHistoricalSynonyms()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.GetAll().Should().HaveCountGreaterThanOrEqualTo(60);
        // A sanity bound against runaway growth, not a contract: raised 120 -> 130 for `toolbar-button` (decision TOOLBAR-FILE-BUTTON),
        // then 130 -> 132 for the data-display pair `trend-list` and `breakdown-card` (plan ff043b2e phase 2),
        // then 133 for `facet-filter` (plan ff043b2e phase 4),
        // then 134 for `trace-waterfall` (plan ff043b2e phase 6),
        // then 135 for `usage-meter` (plan ff043b2e phase 7),
        // then 136 for `segmented-control` (plan ff043b2e phase 8),
        // then 137 for `agent-plan` (plan 3e96f6d8 phase 2),
        // then 139 for the agent-run pair `agent-run-tree` and `agent-monitor` (plan 3e96f6d8 phase 3),
        // then 140 for `model-picker` (plan 3e96f6d8 phase 4),
        // then 141 for `context-usage` (plan 3e96f6d8 phase 5),
        // then 142 for `prompt-library` (plan 3e96f6d8 phase 6),
        // then 143 for `agent-badge` (plan d67b33af phase 1).
        // then 147 for `fan-out-grid` and `parallel-attempts` (plan d67b33af phase 4).
        // then 159 for `app-shell` and `user-menu` (plan 7ba95ed6 phase 8 — the frame and its account menu).
        // then 160 for `pinned-on-top` (plan 4ce1b994 phase 2 — TmPinnedOnTop, decision TOP-LAYER-PIN).
        // then 159 − duplicate segmented-control row merged (plan 1c95309c phase 8a, decision UI-ROLE-SYNONYM-UNIQUE).
        vocabulary.GetAll().Should().HaveCountLessThanOrEqualTo(159); // + agent-handoff, fallback-chain (phase 5); autonomy-dial, memory-panel (phase 6); delegate-board, scheduled-runs (phase 7); conflict-radar, agent-presence-overlay (phase 8); modeling-viewer (plan eadbf967 phase 6)


        vocabulary.Find("search-input")!.Slug.Should().Be("search-input");
        vocabulary.Find("TmDecimalInput")!.Slug.Should().Be("decimal-input");
        vocabulary.Find("TmSearchBox")!.Slug.Should().Be("search-input");
        vocabulary.Find("TmDataGrid")!.Slug.Should().Be("data-table");
        vocabulary.Find("TmOtpInput")!.Slug.Should().Be("otp-input");
        vocabulary.Find("TmKycWizard")!.Slug.Should().Be("kyc-wizard");
        vocabulary.Find("TmScreeningResultPanel")!.Slug.Should().Be("screening-result-panel");
        vocabulary.Find("TmDataImport")!.Slug.Should().Be("data-import");
        vocabulary.Find("TmRedactionLayer")!.Slug.Should().Be("redaction-layer");
        vocabulary.Find("TmTextField")!.Slug.Should().Be("text-input");
        vocabulary.Find("TmNavbar")!.Slug.Should().Be("navigation-bar");
        vocabulary.Find("TmHeading")!.Slug.Should().Be("heading");
        vocabulary.Find("TmLink")!.Slug.Should().Be("link");
        vocabulary.Find("TmSegmentedControl")!.Slug.Should().Be("segmented-control");
        vocabulary.Find("TmFileUpload")!.Slug.Should().Be("file-drop");
        vocabulary.Find("TmSwitch")!.Slug.Should().Be("toggle");
        vocabulary.Find("TmToolbarFileButton")!.Slug.Should().Be("toolbar-button");
    }

    [Fact]
    public void BuiltInSource_SlugsAreUnique()
    {
        var duplicates = new BuiltInUiRoleVocabularySource()
            .GetRoles()
            .GroupBy(role => role.Slug, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        duplicates.Should().BeEmpty(
            "duplicate slugs: {0}",
            string.Join(", ", duplicates));
    }

    [Fact]
    public void BuiltInSource_SynonymsMapToExactlyOneRole()
    {
        var roles = new BuiltInUiRoleVocabularySource().GetRoles().ToList();
        var slugs = roles
            .Select(role => role.Slug.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var synonymOwners = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles)
        {
            foreach (var raw in role.Synonyms)
            {
                var synonym = raw.Trim();
                if (synonym.Length == 0)
                    continue;

                if (!synonymOwners.TryGetValue(synonym, out var owners))
                {
                    owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    synonymOwners[synonym] = owners;
                }

                owners.Add(role.Slug);
            }
        }

        var shared = synonymOwners
            .Where(pair => pair.Value.Count > 1)
            .Select(pair => $"{pair.Key} -> {string.Join(", ", pair.Value.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))}")
            .ToList();
        shared.Should().BeEmpty("synonyms owned by more than one slug: {0}", string.Join("; ", shared));

        var collidingWithOtherSlug = synonymOwners
            .Where(pair => slugs.Contains(pair.Key) && !pair.Value.Contains(pair.Key))
            .Select(pair => $"{pair.Key} (synonym of {string.Join(", ", pair.Value)}) equals another role slug")
            .ToList();
        collidingWithOtherSlug.Should().BeEmpty(
            "synonyms that equal another role slug: {0}",
            string.Join("; ", collidingWithOtherSlug));

        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        vocabulary.Find("agent status")!.Slug.Should().Be("agent-badge");
        vocabulary.Find("run status")!.Slug.Should().Be("agent-monitor");
        vocabulary.Find("bocni panel")!.Slug.Should().Be("sidebar");
        vocabulary.Find("inspektor")!.Slug.Should().Be("side-panel");
        vocabulary.Find("panel vlastnosti")!.Slug.Should().Be("side-panel");
    }

    private sealed class TestVocabularySource(
        string sourceId,
        int priority,
        params UiRoleDefinition[] roles)
        : IUiRoleVocabularySource
    {
        public string SourceId => sourceId;

        public int Priority => priority;

        public IEnumerable<UiRoleDefinition> GetRoles() => roles;
    }
}
