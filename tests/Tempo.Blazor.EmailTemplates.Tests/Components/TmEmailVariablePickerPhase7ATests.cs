using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.EmailTemplates.Localization;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7A (kontrakt §3.10, D8; screen <c>email-variable-picker</c>): the host's
/// <see cref="TemplateVariableInfo.Description"/>/<c>Group</c> show up in the picker and are searchable,
/// the picker opens from an explicit “Insert variable” button on every template-bearing field, and the
/// no-JS fallback inserts into the field the user last edited — never blindly at the end of the block
/// content.
/// </summary>
public class TmEmailVariablePickerPhase7ATests : BunitContext
{
    private static readonly TemplateVariableInfo[] HostVariables =
    [
        new("user.first_name", VariableKind.Scalar) { Description = "First name of the user", Group = "User" },
        new("user.email", VariableKind.Scalar) { Description = "E-mail of the user", Group = "User" },
        new("order.items", VariableKind.Collection) { Description = "Items of the order", Group = "Order" },
        new("legacy_token", VariableKind.Scalar),
    ];

    public TmEmailVariablePickerPhase7ATests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ITmEmailLocalizer>(new EchoEmailLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
    }

    private static EmailTemplateDocument DocWith(EmailBlockBase block)
    {
        var document = new EmailTemplateDocument { Subject = "Ahoj" };
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(block);
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }

    private IRenderedComponent<TmEmailPropertyPanel> Panel(EmailTemplateDocument document, Guid? selected)
        => Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.SelectedId, selected)
            .Add(c => c.Variables, HostVariables)
            .Add(c => c.OnChanged, () => { }));

    private static void OpenVariablePicker(IRenderedComponent<TmEmailPropertyPanel> cut, string property)
        => cut.Find($"[data-tm-variable-trigger=\"{property}\"] .tm-popover__trigger").Click();

    // ── Picker rows: token + description, grouped ────────────────────────────────────────

    [Fact]
    public void Every_row_shows_the_token_in_code_and_the_host_description()
    {
        var button = new EmailButtonBlock { Text = "Jdi" };
        var cut = Panel(DocWith(button), button.Id);
        OpenVariablePicker(cut, "Text");

        var row = cut.Find("[data-tm-variable=\"user.first_name\"]");
        row.QuerySelector("code")!.TextContent.Should().Be("{{ user.first_name }}");
        row.TextContent.Should().Contain("First name of the user");
    }

    [Fact]
    public void Rows_without_a_description_still_show_the_token()
    {
        var button = new EmailButtonBlock { Text = "Jdi" };
        var cut = Panel(DocWith(button), button.Id);
        OpenVariablePicker(cut, "Text");

        var row = cut.Find("[data-tm-variable=\"legacy_token\"]");
        row.QuerySelector("code")!.TextContent.Should().Be("{{ legacy_token }}");
    }

    [Fact]
    public void Host_groups_render_a_group_heading()
    {
        var button = new EmailButtonBlock { Text = "Jdi" };
        var cut = Panel(DocWith(button), button.Id);
        OpenVariablePicker(cut, "Text");

        cut.FindAll("[data-tm-variable-group]").Select(g => g.GetAttribute("data-tm-variable-group"))
            .Should().Contain(["User", "Order"]);
        cut.Find("[data-tm-variable-group=\"User\"] .tm-email-variables__group-heading").TextContent.Trim().Should().Be("User");
    }

    [Fact]
    public void Ungrouped_variables_land_under_the_localized_other_heading_when_groups_exist()
    {
        var button = new EmailButtonBlock { Text = "Jdi" };
        var cut = Panel(DocWith(button), button.Id);
        OpenVariablePicker(cut, "Text");

        cut.Find("[data-tm-variable-group=\"EmailEditor_Variables_Group_Other\"]").Should().NotBeNull();
    }

    [Fact]
    public void The_search_field_starts_empty_and_reflects_the_filter()
    {
        var button = new EmailButtonBlock { Text = "Jdi" };
        var cut = Panel(DocWith(button), button.Id);
        OpenVariablePicker(cut, "Text");

        var search = cut.Find("[data-tm-variable-picker] input[type=search]");
        search.GetAttribute("value").Should().BeNullOrEmpty("the field is not bound to the name of a field");

        search.Input("user");

        cut.Find("[data-tm-variable-picker] input[type=search]").GetAttribute("value").Should().Be("user");
    }

    [Fact]
    public void Search_matches_the_description()
    {
        var button = new EmailButtonBlock { Text = "Jdi" };
        var cut = Panel(DocWith(button), button.Id);
        OpenVariablePicker(cut, "Text");

        cut.Find("[data-tm-variable-picker] input[type=search]").Input("e-mail of");

        cut.FindAll("[data-tm-variable]").Select(e => e.GetAttribute("data-tm-variable"))
            .Should().Equal("user.email");
    }

    [Fact]
    public void Search_matches_the_path()
    {
        var button = new EmailButtonBlock { Text = "Jdi" };
        var cut = Panel(DocWith(button), button.Id);
        OpenVariablePicker(cut, "Text");

        cut.Find("[data-tm-variable-picker] input[type=search]").Input("order.");

        cut.FindAll("[data-tm-variable]").Select(e => e.GetAttribute("data-tm-variable"))
            .Should().Equal("order.items");
    }

    [Fact]
    public void Collection_variables_keep_the_collection_badge()
    {
        var button = new EmailButtonBlock { Text = "Jdi" };
        var cut = Panel(DocWith(button), button.Id);
        OpenVariablePicker(cut, "Text");

        cut.Find("[data-tm-variable=\"order.items\"] .tm-email-variables__badge").TextContent.Should().Be("[]");
    }

    // ── Insert button on template-bearing fields + caret/fallback contract ───────────────

    [Fact]
    public void Image_href_and_alt_offer_the_button_too()
    {
        var image = new EmailImageBlock { Src = "https://x/i.png", Alt = "Logo" };
        var cut = Panel(DocWith(image), image.Id);

        cut.Find("[data-tm-variable-trigger=\"Href\"]").Should().NotBeNull();
        cut.Find("[data-tm-variable-trigger=\"Alt\"]").Should().NotBeNull();
    }

    [Fact]
    public void The_text_block_variables_button_sits_next_to_the_rich_editor()
    {
        var text = new EmailTextBlock { Content = "Ahoj" };
        var cut = Panel(DocWith(text), text.Id);

        cut.Find("[data-tm-rich-content] [data-tm-variable-trigger=\"Content\"]").Should().NotBeNull();
    }

    [Fact]
    public void Inserting_without_js_appends_to_the_last_edited_field_not_to_the_block_content()
    {
        var button = new EmailButtonBlock { Text = "Buy", Href = "https://shop" };
        var cut = Panel(DocWith(button), button.Id);

        // The user typed into the link field last.
        cut.Find("[data-tm-prop=\"Href\"] input").Focus();

        OpenVariablePicker(cut, "Text");
        cut.Find("[data-tm-variable=\"user.first_name\"]").Click();

        button.Href.Should().Be("https://shop{{ user.first_name }}",
            "the token belongs behind the caret of the field the user last edited");
        button.Text.Should().Be("Buy", "the field that owns the button is not where the caret was");
    }

    [Fact]
    public void Inserting_without_js_and_without_a_focused_field_uses_the_owning_field()
    {
        var button = new EmailButtonBlock { Text = "Buy", Href = "https://shop" };
        var cut = Panel(DocWith(button), button.Id);

        OpenVariablePicker(cut, "Text");
        cut.Find("[data-tm-variable=\"user.first_name\"]").Click();

        button.Text.Should().Be("Buy{{ user.first_name }}");
        button.Href.Should().Be("https://shop");
    }

    [Fact]
    public void Inserting_without_js_never_writes_in_read_only()
    {
        var button = new EmailButtonBlock { Text = "Buy", Href = "https://shop" };
        var document = DocWith(button);
        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, document).Add(c => c.SelectedId, button.Id)
            .Add(c => c.Variables, HostVariables).Add(c => c.ReadOnly, true)
            .Add(c => c.OnChanged, () => { }));

        // Read-only renders no trigger at all; the picker rows are disabled when reached directly.
        cut.FindAll("[data-tm-variable-trigger]").Should().BeEmpty();
        button.Text.Should().Be("Buy");
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }

    private sealed class EchoEmailLocalizer : Tempo.Blazor.EmailTemplates.Localization.ITmEmailLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
