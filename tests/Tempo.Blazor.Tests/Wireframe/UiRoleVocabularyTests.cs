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
        vocabulary.GetAll().Should().HaveCountLessThanOrEqualTo(118);

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
