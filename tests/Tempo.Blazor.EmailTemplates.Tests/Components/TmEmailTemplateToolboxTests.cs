using Tempo.Blazor.EmailTemplates.Abstractions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions.Layout;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Registry;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

public class TmEmailTemplateToolboxTests : BunitContext
{
    public TmEmailTemplateToolboxTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
    }

    [Fact]
    public void Renders_AllFourteenBlocks()
    {
        var cut = Render<TmEmailTemplateToolbox>();
        cut.FindAll("[data-tm-block]").Should().HaveCount(14);
    }

    [Fact]
    public void Renders_AllSixLayoutPresets()
    {
        var cut = Render<TmEmailTemplateToolbox>();
        cut.FindAll("[data-tm-preset]").Should().HaveCount(LayoutPresets.All.Count);
    }

    [Fact]
    public void ClickingBlock_RaisesOnAddBlockWithDescriptor()
    {
        BlockDescriptor? added = null;
        var cut = Render<TmEmailTemplateToolbox>(p => p
            .Add(c => c.OnAddBlock, d => added = d));

        cut.Find("[data-tm-block=\"button\"]").Click();

        added.Should().NotBeNull();
        added!.Type.Should().Be(BlockType.Button);
    }

    [Fact]
    public void ClickingPreset_RaisesOnAddSection()
    {
        LayoutPreset? preset = null;
        var cut = Render<TmEmailTemplateToolbox>(p => p
            .Add(c => c.OnAddSection, x => preset = x));

        cut.Find($"[data-tm-preset=\"{LayoutPreset.ThreeEqual}\"]").Click();

        preset.Should().Be(LayoutPreset.ThreeEqual);
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
