using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Toolbar;

/// <summary>
/// C3: <see cref="TmToolbarGroup"/> — a semantic group of toolbar controls. On the bar it is the
/// layout wrapper tm-toolbar.js flattens (<c>data-tm-toolbar-group</c>, <c>role="group"</c> plus an
/// accessible name when labelled); in the "More" menu its collapsed members keep the group as a
/// labelled section.
/// </summary>
public class TmToolbarGroupTests : LocalizationTestBase
{
    [Fact]
    public void Labelled_IsRoleGroupWithAriaLabel_AndDataTmToolbarGroup()
    {
        var cut = Render<TmToolbarGroup>(p => p
            .Add(c => c.Label, "Edit")
            .Add(c => c.ChildContent, "<button type=\"button\">One</button>"));

        var group = cut.Find(".tm-toolbar-group");
        group.GetAttribute("role").Should().Be("group");
        group.GetAttribute("aria-label").Should().Be("Edit");
        group.GetAttribute("data-tm-toolbar-group").Should().NotBeNullOrEmpty("tm-toolbar.js flattens the wrapper by the marker");
    }

    [Fact]
    public void Unlabelled_HasNoRole()
    {
        // role=group without an accessible name is noise for assistive technology: an unlabelled
        // group is a layout wrapper only.
        var cut = Render<TmToolbarGroup>(p => p.Add(c => c.ChildContent, "<button type=\"button\">One</button>"));

        var group = cut.Find(".tm-toolbar-group");
        group.HasAttribute("role").Should().BeFalse();
        group.HasAttribute("aria-label").Should().BeFalse();
        group.HasAttribute("data-tm-toolbar-group").Should().BeTrue("measurement flattens it either way");
    }

    [Fact]
    public void ClassAndAdditionalAttributes_LandOnTheWrapper()
    {
        var cut = Render<TmToolbarGroup>(p => p
            .Add(c => c.Label, "Edit")
            .Add(c => c.Class, "my-group")
            .Add(c => c.AdditionalAttributes, new Dictionary<string, object> { ["data-tm-group"] = "edit" })
            .Add(c => c.ChildContent, "<button type=\"button\">One</button>"));

        var group = cut.Find(".tm-toolbar-group");
        group.ClassList.Should().Contain("my-group");
        group.GetAttribute("data-tm-group").Should().Be("edit", "a host's own marker (e.g. the DiagramEditor's data-tm-group) rides through");
    }

    [Fact]
    public void ChildContent_RendersInsideTheWrapper()
    {
        var cut = Render<TmToolbarGroup>(p => p
            .Add(c => c.Label, "Edit")
            .Add(c => c.ChildContent, "<button type=\"button\">One</button><button type=\"button\">Two</button>"));

        cut.FindAll(".tm-toolbar-group button").Should().HaveCount(2);
    }
}
