namespace Tempo.Blazor.Tests.Theme;

public class ThemeServiceTests
{
    [Fact]
    public void IsDark_DefaultsFalse()
    {
        var svc = new Tempo.Blazor.Services.ThemeService();
        svc.IsDark.Should().BeFalse();
    }

    [Fact]
    public void Toggle_SwitchesToDark()
    {
        var svc = new Tempo.Blazor.Services.ThemeService();
        svc.Toggle();
        svc.IsDark.Should().BeTrue();
    }

    [Fact]
    public void Toggle_Twice_SwitchesBack()
    {
        var svc = new Tempo.Blazor.Services.ThemeService();
        svc.Toggle();
        svc.Toggle();
        svc.IsDark.Should().BeFalse();
    }

    [Fact]
    public void Toggle_RaisesOnChanged()
    {
        var svc = new Tempo.Blazor.Services.ThemeService();
        var raised = false;
        svc.OnChanged += () => raised = true;

        svc.Toggle();

        raised.Should().BeTrue();
    }

    [Fact]
    public void ThemeName_ReturnsCorrectString()
    {
        var svc = new Tempo.Blazor.Services.ThemeService();
        svc.ThemeName.Should().Be("light");

        svc.Toggle();
        svc.ThemeName.Should().Be("dark");
    }
}
