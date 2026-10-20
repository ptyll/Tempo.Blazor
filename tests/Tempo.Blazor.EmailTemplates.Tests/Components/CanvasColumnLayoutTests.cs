using Tempo.Blazor.EmailTemplates.Abstractions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

public class CanvasColumnLayoutTests : BunitContext
{
    public CanvasColumnLayoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
    }

    [Fact]
    public void ManyColumns_RenderShrinkableProportionalFlex_NotFixedBasis()
    {
        // Regression: adding columns made the row overflow the editor because each column used a
        // non-shrinkable `flex:0 0 {width}`. Six 16.7% columns + gaps then exceeded 100%.
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        for (var i = 0; i < 6; i++)
        {
            section.Columns.Add(new EmailColumn { Width = "16.7%" });
        }
        doc.Sections.Add(section);

        var cut = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, doc));

        var columns = cut.FindAll("[data-tm-column]");
        columns.Should().HaveCount(6);
        foreach (var column in columns)
        {
            var style = column.GetAttribute("style") ?? string.Empty;
            style.Should().Contain("min-inline-size:0", "columns must be allowed to shrink");
            style.Should().NotContain("0 0 16.7%", "a fixed, non-shrinkable basis caused the overflow");
            style.Should().Contain("flex:16.7 1 0", "columns grow proportionally over a zero basis so they always fit");
        }
    }

    [Fact]
    public void EqualWidthColumns_WithoutExplicitWidth_ShareSpaceEqually()
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        section.Columns.Add(new EmailColumn());
        section.Columns.Add(new EmailColumn());
        doc.Sections.Add(section);

        var cut = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, doc));

        foreach (var column in cut.FindAll("[data-tm-column]"))
        {
            (column.GetAttribute("style") ?? string.Empty).Should().Contain("flex:1 1 0");
        }
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
