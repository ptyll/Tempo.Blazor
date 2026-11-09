using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;
using Tempo.Blazor.Components.NotionEditor.Blocks.Database;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// The template menu's per-item actions must be reachable without a hover, and Escape must close
/// the menu so focus can return to the trigger that opened it.
/// </summary>
public class TmNotionDbTemplateMenuKeyboardTests : LocalizationTestBase
{
    [Fact]
    public void TemplateMenu_ItemActions_ReachableByKeyboard()
    {
        var provider = Substitute.For<INotionDatabaseProvider>();
        provider.GetTemplatesAsync(Arg.Any<string>()).Returns(new IDatabaseRecordTemplate[]
        {
            new DatabaseRecordTemplate { Id = Guid.NewGuid(), Name = "Weekly" }
        });
        var closed = false;
        var cut = Render<TmNotionDbTemplateMenu>(p => p
            .Add(x => x.DatabaseId, Guid.NewGuid())
            .Add(x => x.Fields, Array.Empty<IDatabaseField>())
            .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => closed = true))
            .AddCascadingValue(new NotionEditorContext { DatabaseProvider = provider }));

        cut.WaitForAssertion(() => cut.FindAll(".tm-dbtm__action-btn").Should().NotBeEmpty());
        cut.Find(".tm-dbtm__action-btn").Should().NotBeNull();

        cut.Find(".tm-dbtm").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        closed.Should().BeTrue();
    }
}
