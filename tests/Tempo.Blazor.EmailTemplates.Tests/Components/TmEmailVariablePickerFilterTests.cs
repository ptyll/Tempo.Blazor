using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Decision EMAIL-VARIABLE-PICKER-FILTER (phase 7): the variable picker's filter is bindable
/// (<c>@bind-Filter</c>), an unbound picker behaves as before, and the inspector keeps the picker's open
/// state and filter on a level that survives a layout flip (desktop ↔ tablet remounts the inspector
/// content) — the filter is reset when the picker CLOSES, never by a flip. The editor itself gets no new
/// parameter (contract §3 unchanged).
/// </summary>
public class TmEmailVariablePickerFilterTests : EmailEditorTestBase
{
    private static readonly TemplateVariableInfo[] Variables =
    [
        new("user.first_name", VariableKind.Scalar) { Description = "First name", Group = "User" },
        new("user.email", VariableKind.Scalar) { Description = "E-mail", Group = "User" },
        new("order.items", VariableKind.Collection) { Description = "Items", Group = "Order" },
    ];

    private static IEnumerable<string?> Rows(IRenderedComponent<TmEmailVariablePicker> cut)
        => cut.FindAll("[data-tm-variable]").Select(e => e.GetAttribute("data-tm-variable"));

    // ── The picker's own binding ────────────────────────────────────────────────────────────

    [Fact]
    public void A_bound_Filter_is_applied_and_shown_in_the_search_field()
    {
        var cut = Render<TmEmailVariablePicker>(p => p
            .Add(c => c.Variables, Variables)
            .Add(c => c.Filter, "order"));

        Rows(cut).Should().Equal("order.items");
        cut.Find("[data-tm-variable-picker] input[type=search]").GetAttribute("value").Should().Be("order");
    }

    [Fact]
    public void Typing_raises_FilterChanged_with_the_new_text()
    {
        var raised = new List<string>();
        var cut = Render<TmEmailVariablePicker>(p => p
            .Add(c => c.Variables, Variables)
            .Add(c => c.FilterChanged, EventCallback.Factory.Create<string>(this, value => raised.Add(value))));

        cut.Find("[data-tm-variable-picker] input[type=search]").Input("user");

        raised.Should().Equal("user");
        Rows(cut).Should().Equal("user.email", "user.first_name");
    }

    [Fact]
    public void A_host_that_changes_Filter_later_moves_the_picker()
    {
        var cut = Render<TmEmailVariablePicker>(p => p
            .Add(c => c.Variables, Variables)
            .Add(c => c.Filter, "user"));

        cut.Render(p => p.Add(c => c.Variables, Variables).Add(c => c.Filter, "items"));

        Rows(cut).Should().Equal("order.items");
    }

    [Fact]
    public void Without_a_binding_the_picker_filters_on_its_own_state_as_before()
    {
        var cut = Render<TmEmailVariablePicker>(p => p.Add(c => c.Variables, Variables));

        cut.Find("[data-tm-variable-picker] input[type=search]").Input("first");

        Rows(cut).Should().Equal("user.first_name");
        cut.Render(p => p.Add(c => c.Variables, Variables));
        Rows(cut).Should().Equal(new[] { "user.first_name" }, "a re-render without a Filter parameter keeps the typed text");
    }

    [Fact]
    public void A_null_Filter_is_the_empty_filter()
    {
        var cut = Render<TmEmailVariablePicker>(p => p
            .Add(c => c.Variables, Variables)
            .Add(c => c.Filter, (string?)null));

        Rows(cut).Should().HaveCount(3);
    }

    // ── The inspector keeps it across a remount (the layout flip) ───────────────────────────

    private static EmailTemplateDocument DocWithButton(out EmailButtonBlock button)
    {
        button = new EmailButtonBlock { Text = "Go" };
        var document = new EmailTemplateDocument { Subject = "Hi" };
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(button);
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }

    private IRenderedComponent<TmEmailPropertyPanel> Panel(EmailTemplateDocument document, Guid selected, EmailInspectorState state)
        => Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.SelectedId, selected)
            .Add(c => c.Variables, Variables)
            .Add(c => c.State, state)
            .Add(c => c.OnChanged, () => { }));

    private static void OpenPicker(IRenderedComponent<TmEmailPropertyPanel> cut)
        => cut.Find("[data-tm-variable-trigger=\"Text\"] .tm-popover__trigger").Click();

    [Fact]
    public void A_remounted_inspector_with_the_same_state_reopens_the_picker_with_its_filter()
    {
        var document = DocWithButton(out var button);
        var state = new EmailInspectorState();

        var first = Panel(document, button.Id, state);
        OpenPicker(first);
        first.Find("[data-tm-variable-picker] input[type=search]").Input("order");
        first.FindAll("[data-tm-variable]").Should().ContainSingle();
        first.Dispose();   // the layout flip tears the inspector content down …

        var second = Panel(document, button.Id, state);   // … and mounts it again

        second.Find("[data-tm-variable-picker] input[type=search]").GetAttribute("value").Should().Be("order",
            "the filter belongs to the editor's inspector state, not to the component that was unmounted");
        second.FindAll("[data-tm-variable]").Select(e => e.GetAttribute("data-tm-variable"))
            .Should().Equal("order.items");
    }

    [Fact]
    public void Closing_the_picker_resets_the_filter()
    {
        var document = DocWithButton(out var button);
        var state = new EmailInspectorState();
        var cut = Panel(document, button.Id, state);
        OpenPicker(cut);
        cut.Find("[data-tm-variable-picker] input[type=search]").Input("user");

        cut.Find("[data-tm-variable-trigger=\"Text\"] .tm-popover__trigger").Click();   // toggle = close
        cut.FindAll("[data-tm-variable-picker]").Should().BeEmpty();
        state.VariableFilter.Should().BeEmpty("a closed picker forgets what was typed");

        OpenPicker(cut);
        cut.Find("[data-tm-variable-picker] input[type=search]").GetAttribute("value").Should().BeNullOrEmpty();
        cut.FindAll("[data-tm-variable]").Should().HaveCount(3);
    }

    [Fact]
    public void A_panel_without_a_host_state_still_works_and_keeps_the_filter_while_it_lives()
    {
        var document = DocWithButton(out var button);
        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.SelectedId, button.Id)
            .Add(c => c.Variables, Variables)
            .Add(c => c.OnChanged, () => { }));

        cut.Find("[data-tm-variable-trigger=\"Text\"] .tm-popover__trigger").Click();
        cut.Find("[data-tm-variable-picker] input[type=search]").Input("first");
        cut.Render(p => p
            .Add(c => c.Document, document)
            .Add(c => c.SelectedId, button.Id)
            .Add(c => c.Variables, Variables)
            .Add(c => c.OnChanged, () => { }));

        cut.FindAll("[data-tm-variable]").Select(e => e.GetAttribute("data-tm-variable"))
            .Should().Equal("user.first_name");
    }

    [Fact]
    public void The_editor_survives_a_forced_layout_flip_with_an_open_picker_and_a_filter()
    {
        var document = DocWithButton(out var button);
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.Variables, Variables)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop));

        cut.Find("[data-tm-block=\"button\"]").Click();
        cut.Find("[data-tm-variable-trigger=\"Text\"] .tm-popover__trigger").Click();
        cut.Find("[data-tm-variable-picker] input[type=search]").Input("order");

        cut.Render(p => p
            .Add(c => c.Document, document)
            .Add(c => c.Variables, Variables)
            .Add(c => c.LayoutMode, TmLayoutMode.Tablet));
        // The tablet layout shows ONE panel; the inspector is the one a selection opens.
        if (cut.FindAll("[data-tm-variable-picker]").Count == 0 && cut.FindAll("[data-tm-expand-properties]").Count > 0)
        {
            cut.Find("[data-tm-expand-properties]").Click();
        }

        cut.Find("[data-tm-variable-picker] input[type=search]").GetAttribute("value").Should().Be("order");
        cut.FindAll("[data-tm-variable]").Select(e => e.GetAttribute("data-tm-variable"))
            .Should().Equal("order.items");
    }
}
