using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// <see cref="TmFocusScope"/> is the public focus-trap primitive. The cycling itself lives in the JS
/// module (covered by its node tests and the E2E suite); these tests pin the markup contract a host
/// and a screen reader rely on: the dialog semantics, the inert background, and the restore target.
/// </summary>
public class TmFocusScopeTests : LocalizationTestBase
{
    [Fact]
    public void Active_MarksItselfAsAModalDialog()
    {
        var cut = Render<TmFocusScope>(p => p
            .Add(s => s.Active, true)
            .AddChildContent("<button>Save</button>"));

        var scope = cut.Find(".tm-focus-scope");
        scope.GetAttribute("role").Should().Be("dialog");
        scope.GetAttribute("aria-modal").Should().Be("true");
        scope.GetAttribute("tabindex").Should().Be("-1");
    }

    [Fact]
    public void Inactive_DropsTheDialogSemantics()
    {
        var cut = Render<TmFocusScope>(p => p
            .Add(s => s.Active, false)
            .AddChildContent("<button>Save</button>"));

        var scope = cut.Find(".tm-focus-scope");
        scope.GetAttribute("role").Should().BeNull();
        scope.GetAttribute("aria-modal").Should().BeNull();
    }

    [Fact]
    public void Nested_OnlyTheInnermostScopeOwnsTheDialogRole()
    {
        var cut = Render<TmFocusScope>(p => p
            .Add(s => s.Active, true)
            .AddChildContent(new RenderFragment(b =>
            {
                b.OpenComponent<TmFocusScope>(0);
                b.AddAttribute(1, "Active", true);
                b.AddAttribute(2, "ChildContent", (RenderFragment)(inner => inner.AddMarkupContent(0, "<button>Inner</button>")));
                b.CloseComponent();
            })));

        var scopes = cut.FindAll(".tm-focus-scope");
        scopes.Should().HaveCount(2);
        scopes[0].GetAttribute("aria-modal").Should().Be("false",
            "a trap with an active descendant yields the modal semantics to the innermost one");
        scopes[1].GetAttribute("aria-modal").Should().Be("true");
    }

    [Fact]
    public void RestoreFocusTarget_IsRecordedSoTheTrapCanHandFocusBack()
    {
        var cut = Render<ScopeHost>(p => p.Add(h => h.Active, true));

        cut.Find(".tm-focus-scope").GetAttribute("data-restore-target").Should().Be("canvas",
            "a canvas editor hands focus back to its canvas, not to the trigger that opened the scope");
    }

    [Fact]
    public void Nested_DeactivatingTheInnerScope_RestoresTheOuterDialogAndLetsItHandleEscape()
    {
        var outerEscapes = 0;
        var cut = Render<NestedScopeHost>(p => p
            .Add(h => h.InnerActive, true)
            .Add(h => h.OnOuterEscape, EventCallback.Factory.Create(this, () => outerEscapes++)));

        var scopes = cut.FindAll(".tm-focus-scope");
        scopes[0].GetAttribute("aria-modal").Should().Be("false");

        cut.Render(p => p.Add(h => h.InnerActive, false));

        scopes = cut.FindAll(".tm-focus-scope");
        scopes.Should().ContainSingle();
        scopes[0].GetAttribute("aria-modal").Should().Be("true",
            "closing the inner dialog hands the modal semantics back; a double-counted descendant would leave the outer one false");

        cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync().GetAwaiter().GetResult();
        outerEscapes.Should().Be(1, "the outer scope handles Escape once it is topmost again");
    }

    [Fact]
    public void Role_DefaultsToDialog_AndCanBeAnAlertDialog()
    {
        var dialog = Render<TmFocusScope>(p => p.Add(s => s.Active, true).AddChildContent("<button>Save</button>"));
        dialog.Find(".tm-focus-scope").GetAttribute("role").Should().Be("dialog");

        var alert = Render<TmFocusScope>(p => p
            .Add(s => s.Active, true)
            .Add(s => s.DialogRole, "alertdialog")
            .AddChildContent("<button>Delete</button>"));
        alert.Find(".tm-focus-scope").GetAttribute("role").Should().Be("alertdialog");
    }

    [Fact]
    public void InitialFocusTarget_IsForwardedSoTheModuleFocusesThatElement()
    {
        var cut = Render<TmFocusScope>(p => p
            .Add(s => s.Active, true)
            .Add(s => s.InitialFocusTargetId, "name")
            .AddChildContent("<input id=\"name\" />"));

        cut.Find(".tm-focus-scope").GetAttribute("data-initial-focus").Should().Be("name",
            "the module resolves the initial focus target from the id the scope records");
    }

    [Fact]
    public void LabelledBy_WiresTheAccessibleName()
    {
        var cut = Render<TmFocusScope>(p => p
            .Add(s => s.Active, true)
            .Add(s => s.AriaLabelledBy, "sheet-title")
            .AddChildContent("<h2 id=\"sheet-title\">Filters</h2>"));

        cut.Find(".tm-focus-scope").GetAttribute("aria-labelledby").Should().Be("sheet-title");
    }

    /// <summary>Renders a scope beside a sibling the trap must mark inert, and names a restore target.</summary>
    private sealed class ScopeHost : ComponentBase
    {
        [Parameter] public bool Active { get; set; }

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "tm-focus-scope-host");

            builder.OpenElement(2, "button");
            builder.AddAttribute(3, "id", "canvas");
            builder.AddContent(4, "Canvas");
            builder.CloseElement();

            builder.OpenComponent<TmFocusScope>(5);
            builder.AddAttribute(6, "Active", Active);
            builder.AddAttribute(7, "RestoreFocusTarget", (ElementReference?)null);
            builder.AddAttribute(8, "RestoreFocusTargetId", "canvas");
            builder.AddAttribute(9, "ChildContent", (RenderFragment)(b => b.AddMarkupContent(0, "<button>Save</button>")));
            builder.CloseComponent();

            builder.CloseElement();
        }
    }

    /// <summary>An outer scope with an inner one that can be deactivated, the nested-dialog case.</summary>
    private sealed class NestedScopeHost : ComponentBase
    {
        [Parameter] public bool InnerActive { get; set; }

        [Parameter] public EventCallback OnOuterEscape { get; set; }

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmFocusScope>(0);
            builder.AddAttribute(1, "Active", true);
            builder.AddAttribute(2, "CloseOnEscape", true);
            builder.AddAttribute(3, "OnEscape", OnOuterEscape);
            builder.AddAttribute(4, "ChildContent", (RenderFragment)(inner =>
            {
                inner.OpenElement(0, "button");
                inner.AddContent(1, "Outer");
                inner.CloseElement();

                if (!InnerActive) return;

                inner.OpenComponent<TmFocusScope>(2);
                inner.AddAttribute(3, "Active", true);
                inner.AddAttribute(4, "ChildContent", (RenderFragment)(b => b.AddMarkupContent(0, "<button>Inner</button>")));
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
