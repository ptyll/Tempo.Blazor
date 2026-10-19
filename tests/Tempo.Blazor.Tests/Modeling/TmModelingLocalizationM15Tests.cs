using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class TmModelingLocalizationM15Tests : LocalizationTestBase
{
    private static readonly string[] ModelingResourceKeys =
    [
        "TmModelingEditor_Title",
        "TmModelingEditor_Context",
        "TmModelingEditor_Provider",
        "TmModelingEditor_Notation",
        "TmModelingEditor_Viewpoint",
        "TmModelingEditor_NotSelected",
        "TmModelingEditor_Default",
        "TmModelingEditor_StateEmpty",
        "TmModelingEditor_EmptyTitle",
        "TmModelingEditor_EmptyText",
        "TmModelingEditor_ModelTree",
        "TmModelingEditor_Preview",
        "TmModelingEditor_Inspector",
        "TmModelingEditor_Panels",
        "TmModelingModelTree_Title",
        "TmModelingModelTree_Count",
        "TmModelingModelTree_SearchLabel",
        "TmModelingModelTree_SearchPlaceholder",
        "TmModelingModelTree_EmptyTitle",
        "TmModelingModelTree_EmptyText",
        "TmModelingViewSelector_Title",
        "TmModelingViewSelector_Notation",
        "TmModelingViewSelector_Viewpoint",
        "TmModelingViewSelector_SelectNotation",
        "TmModelingViewSelector_SelectNotationHint",
        "TmModelingViewSelector_NoViewpoints",
        "TmModelingViewSelector_Viewpoint_Application",
        "TmModelingViewSelector_Viewpoint_Process",
        "TmModelingDiagramPreview_Title",
        "TmModelingDiagramPreview_Generate",
        "TmModelingDiagramPreview_OpenInEditor",
        "TmModelingDiagramPreview_EmptyTitle",
        "TmModelingDiagramPreview_EmptyText",
        "TmModelingDiagramPreview_EmptySummary",
        "TmModelingDiagramPreview_Summary",
        "TmModelingIssuePanel_Title",
        "TmModelingIssuePanel_Count",
        "TmModelingIssuePanel_EmptyTitle",
        "TmModelingIssuePanel_EmptyText",
        "TmModelingIssuePanel_Info",
        "TmModelingIssuePanel_Warning",
        "TmModelingIssuePanel_Error",
        "TmModelingIssuePanel_UnspecifiedMessage",
        "TmModelingInspector_Title",
        "TmModelingInspector_EmptyTitle",
        "TmModelingInspector_EmptyText",
        "TmModelingInspector_Element",
        "TmModelingInspector_Name",
        "TmModelingInspector_SemanticType",
        "TmModelingInspector_Notation",
        "TmModelingInspector_SourceId",
        "TmModelingInspector_SourceType",
        "TmModelingInspector_SourcePath",
        "TmModelingInspector_Description",
        "TmModelingInspector_NoDescription",
        "TmModelingInspector_Governance",
        "TmModelingInspector_Trust",
        "TmModelingInspector_ReviewState",
        "TmModelingInspector_SyncState",
        "TmModelingInspector_DataSource",
        "TmModelingInspector_Properties",
        "TmModelingInspector_NoProperties",
        "TmModelingSourcePanel_Title",
        "TmModelingSourcePanel_LoadModel",
        "TmModelingSourcePanel_Loading",
        "TmModelingSourcePanel_EmptyTitle",
        "TmModelingSourcePanel_EmptyText",
        "TmModelingSourcePanel_SourceSystem",
        "TmModelingSourcePanel_SourceVersion",
        "TmModelingSourcePanel_LoadedAt",
        "TmModelingSourcePanel_Fresh",
        "TmModelingSourcePanel_Stale",
        "TmModelingSourcePanel_Unknown",
        "TmModelingSourcePanel_StaleWarning"
    ];

    private static readonly string[] ForbiddenUiTexts =
    [
        "Model tree",
        "Strom modelu",
        "Diagram preview",
        "Náhled diagramu",
        "Generate diagram",
        "Generovat diagram",
        "Issues",
        "Nálezy",
        "Inspector",
        "Inspektor",
        "Select notation",
        "Vyberte notaci",
        "No source loaded",
        "Není načten žádný zdroj",
        "Governance",
        "Řízení kvality",
        "Application usage",
        "Aplikační použití"
    ];

    public TmModelingLocalizationM15Tests()
    {
        Services.AddTempoBlazorModeling();
    }

    [Fact]
    public void Modeling_components_render_ui_text_from_localizer_resources()
    {
        var localization = BuildMarkerLocalization();
        UseCustomLocalization(localization);
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingNotationProfile>(new TestNotationProfile()));

        var rendered = new[]
        {
            Render<TmModelingEditor>().Markup,
            Render<TmModelingModelTree>().Markup,
            Render<TmModelingViewSelector>(parameters => parameters.Add(p => p.NotationKey, "bpmn")).Markup,
            Render<TmModelingDiagramPreview>().Markup,
            Render<TmModelingSourcePanel>().Markup,
            Render<TmModelingIssuePanel>(parameters => parameters.Add(p => p.Issues, CreateIssues())).Markup,
            Render<TmModelingInspector>(parameters => parameters.Add(p => p.Element, CreateElement())).Markup
        };

        var markup = string.Join(Environment.NewLine, rendered);

        markup.Should().Contain(Marker(localization, "TmModelingEditor_Title"));
        markup.Should().Contain(Marker(localization, "TmModelingModelTree_Title"));
        markup.Should().Contain(Marker(localization, "TmModelingViewSelector_Viewpoint_Process"));
        markup.Should().Contain(Marker(localization, "TmModelingDiagramPreview_Title"));
        markup.Should().Contain(Marker(localization, "TmModelingSourcePanel_Title"));
        markup.Should().Contain(Marker(localization, "TmModelingIssuePanel_Warning"));
        markup.Should().Contain(Marker(localization, "TmModelingInspector_Governance"));
        markup.Should().NotContainAny(ForbiddenUiTexts);
    }

    [Fact]
    public void Issue_panel_severity_labels_are_localized()
    {
        UseCustomLocalization(new()
        {
            ["TmModelingIssuePanel_Title"] = "Localized issues",
            ["TmModelingIssuePanel_Count"] = "Localized count {0}",
            ["TmModelingIssuePanel_Info"] = "Localized info",
            ["TmModelingIssuePanel_Warning"] = "Localized warning",
            ["TmModelingIssuePanel_Error"] = "Localized error"
        });

        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateIssues()));

        cut.Markup.Should().Contain("Localized info");
        cut.Markup.Should().Contain("Localized warning");
        cut.Markup.Should().Contain("Localized error");
        cut.Markup.Should().NotContain("Warning</strong>");
        cut.Markup.Should().NotContain("Error</strong>");
    }

    [Fact]
    public void Missing_resource_key_falls_back_to_key_without_exception()
    {
        Services.AddSingleton<ITmLocalizer>(new MockTmLocalizer(new Dictionary<string, string>()));

        using var cut = Render<TmModelingSourcePanel>();

        cut.Find("[data-testid='modeling-source-panel']").Should().NotBeNull();
        cut.Markup.Should().Contain("[TmModelingSourcePanel_Title]");
        cut.Markup.Should().Contain("[TmModelingSourcePanel_LoadModel]");
    }

    [Fact]
    public void Modeling_editor_renders_czech_ui_texts_in_czech_culture()
    {
        UseCzechLocalization();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new CzechTestProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, CzechTestProvider.Key));

        cut.WaitForAssertion(() =>
        {
            var markup = cut.Markup;
            markup.Should().Contain("Modelovací editor");
            markup.Should().Contain("Strom modelu");
            markup.Should().Contain("Náhled diagramu");
            markup.Should().Contain("Poskytovatel");
            markup.Should().Contain("Pohled");
            markup.Should().Contain("Inspektor");
            markup.Should().Contain("Nálezy");
            markup.Should().NotContain("Modeling Editor");
            markup.Should().NotContain("Model tree");
            markup.Should().NotContain("Diagram preview");
            markup.Should().NotContain("Generate diagram");
            markup.Should().NotContain("Viewpoint");
        });
    }

    [Fact]
    public void Modeling_editor_sends_the_request_culture_and_runs_the_loading_delay_on_the_clock()
    {
        // Review fix: the load request carries CultureInfo.CurrentUICulture — pin the ambient
        // culture with a CultureScope (a bare assignment would leak into every later test on the
        // thread), drive the minimum-loading delay with a FakeTimeProvider (no real 150 ms
        // sleep) and read the culture back through a recording provider.
        var time = new FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        var recorder = new RecordingModelProvider();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(recorder));

        using var culture = CultureScope.Use("cs");
        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, RecordingModelProvider.Key));

        // Before the delay elapses the provider must not have been asked yet.
        recorder.Request.Should().BeNull();
        time.Advance(TimeSpan.FromMilliseconds(200));

        cut.WaitForAssertion(() => recorder.Request.Should().NotBeNull(), TimeSpan.FromSeconds(5));
        recorder.Request!.Culture.Should().Be("cs");
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Model tree"), TimeSpan.FromSeconds(5));
    }

    private static Dictionary<string, string> BuildMarkerLocalization()
        => ModelingResourceKeys
            .Select((key, index) => (key, value: key switch
            {
                "TmModelingModelTree_Count" => $"loc-{index:D2} {{0}}",
                "TmModelingDiagramPreview_Summary" => $"loc-{index:D2} {{0}}/{{1}}",
                "TmModelingIssuePanel_Count" => $"loc-{index:D2} {{0}}",
                _ => $"loc-{index:D2}"
            }))
            .ToDictionary(item => item.key, item => item.value, StringComparer.Ordinal);

    private static string Marker(IReadOnlyDictionary<string, string> localization, string key)
        => localization[key].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

    private static ModelingElementDto CreateElement() => new()
    {
        Id = "element-a",
        SourceId = "source-a",
        SourceType = "type-a",
        SourcePath = "/source/a",
        Name = "Element A",
        SemanticType = "semantic-a",
        Notation = "notation-a",
        Governance = new ModelingGovernanceDto
        {
            TrustLevel = "medium",
            ReviewState = "reviewed",
            SyncState = "fresh",
            DataSource = "SYS"
        }
    };

    private static ModelingIssueDto[] CreateIssues() =>
    [
        new() { Id = "info", Severity = ModelingIssueSeverity.Info, SourceElementId = "element-a", Message = "message-a" },
        new() { Id = "warning", Severity = ModelingIssueSeverity.Warning, SourceElementId = "element-a", Message = "message-b" },
        new() { Id = "error", Severity = ModelingIssueSeverity.Error, SourceElementId = "element-a", Message = "message-c" }
    ];

    private sealed class TestNotationProfile : IModelingNotationProfile
    {
        public string NotationKey => "bpmn";

        public string DisplayName => "BPMN";

        public IReadOnlyCollection<string> SupportedElementTypes { get; } = [];

        public IReadOnlyCollection<string> SupportedRelationshipTypes { get; } = [];

        public IReadOnlyCollection<string> SupportedViewpointKeys { get; } = ["process"];
    }

    private sealed class CzechTestProvider : IModelingModelProvider
    {
        public const string Key = "tempo.tests.modeling.localization.cs";

        public string ProviderKey => Key;

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = new ModelingModelDto
            {
                Id = "cs-model",
                Title = "CS model",
                Notation = "bpmn",
                Metadata = new ModelingMetadataDto
                {
                    SourceSystem = "SYS",
                    SourceVersion = "1",
                    LoadedAt = DateTimeOffset.UtcNow
                },
                Elements =
                [
                    new()
                    {
                        Id = "task-a",
                        SourceId = "source-a",
                        SourceType = "type-a",
                        Name = "Task A",
                        SemanticType = "task",
                        Notation = "bpmn"
                    }
                ]
            };

            model.Views.Add(new ModelingViewDto
            {
                Id = "main",
                Name = "Main",
                Notation = "bpmn",
                Nodes = [new() { ElementId = "task-a", X = 80, Y = 80, Width = 140, Height = 80 }]
            });

            return Task.FromResult(model);
        }
    }

    /// <summary>Records the request so a test can assert the culture the editor sent.</summary>
    private sealed class RecordingModelProvider : IModelingModelProvider
    {
        public const string Key = "tempo.tests.modeling.localization.recording";

        public string ProviderKey => Key;

        public ModelingModelRequest? Request { get; private set; }

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new ModelingModelDto
            {
                Id = "recorded-model",
                Title = "Recorded",
                Notation = "bpmn",
                Metadata = new ModelingMetadataDto
                {
                    SourceSystem = "SYS",
                    SourceVersion = "1",
                    LoadedAt = DateTimeOffset.UtcNow
                },
                Elements =
                [
                    new()
                    {
                        Id = "task-a",
                        SourceId = "source-a",
                        SourceType = "type-a",
                        Name = "Task A",
                        SemanticType = "task",
                        Notation = "bpmn"
                    }
                ]
            });
        }
    }
}
