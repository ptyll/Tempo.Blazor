using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Forms;

public class TmFormRowTests : LocalizationTestBase
{
    [Fact]
    public void FormRow_RendersChildrenInRow()
    {
        var cut = RenderComponent<TmFormRow>(p => p
            .AddChildContent("<div class='col-a'>A</div><div class='col-b'>B</div>"));

        cut.Find(".tm-form-row").Should().NotBeNull();
        cut.Find(".col-a").Should().NotBeNull();
        cut.Find(".col-b").Should().NotBeNull();
    }

    [Fact]
    public void FormRow_Columns_AppliesGridClass()
    {
        var cut = RenderComponent<TmFormRow>(p => p
            .Add(c => c.Columns, 3)
            .AddChildContent("<div>A</div>"));

        cut.Find(".tm-form-row").ClassList.Should().Contain("tm-form-row--cols-3");
    }
}
