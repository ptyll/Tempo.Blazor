using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Interfaces;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

public class TmFileManagerTests : LocalizationTestBase
{
    private static IFileManagerDataProvider CreateMockProvider()
    {
        var items = new List<FileManagerItem>
        {
            new() { Id = "root", Name = "Root", Path = "/", IsDirectory = true },
            new() { Id = "docs", Name = "Documents", Path = "/Documents", IsDirectory = true },
            new() { Id = "pics", Name = "Pictures", Path = "/Pictures", IsDirectory = true },
            new() { Id = "file1", Name = "Report.pdf", Path = "/Documents/Report.pdf", IsDirectory = false, Size = 1024, Extension = ".pdf" },
            new() { Id = "file2", Name = "Data.xlsx", Path = "/Documents/Data.xlsx", IsDirectory = false, Size = 2048, Extension = ".xlsx" },
        };

        return new MockFileManagerDataProvider(items);
    }

    [Fact]
    public void TmFileManager_Renders_Container()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        cut.Find(".tm-file-manager").Should().NotBeNull();
    }

    [Fact]
    public void TmFileManager_Renders_Toolbar()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        cut.Find(".tm-file-manager__toolbar").Should().NotBeNull();
    }

    [Fact]
    public void TmFileManager_Renders_Breadcrumb()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        cut.Find(".tm-file-manager__breadcrumb").Should().NotBeNull();
    }

    [Fact]
    public void TmFileManager_Renders_Folder_Tree()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        cut.Find(".tm-file-manager__sidebar").Should().NotBeNull();
    }

    [Fact]
    public void TmFileManager_Default_ViewMode_Is_List()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        cut.Find(".tm-file-manager__content--list").Should().NotBeNull();
    }

    [Fact]
    public void TmFileManager_Grid_View_Shows_Grid_Class()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider())
            .Add(c => c.ViewMode, FileManagerViewMode.Grid));

        cut.Find(".tm-file-manager__content--grid").Should().NotBeNull();
    }

    [Fact]
    public void TmFileManager_Loads_Root_Contents()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        // Root contains Documents and Pictures folders
        cut.Markup.Should().Contain("Documents");
        cut.Markup.Should().Contain("Pictures");
    }

    [Fact]
    public void TmFileManager_Click_Folder_Navigates()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        // Double-click on Documents folder
        var folder = cut.FindAll(".tm-file-manager__item").First(e => e.TextContent.Contains("Documents"));
        folder.DoubleClick();

        // Should now show files inside Documents
        cut.Markup.Should().Contain("Report.pdf");
        cut.Markup.Should().Contain("Data.xlsx");
    }

    [Fact]
    public void TmFileManager_Breadcrumb_Updates_On_Navigate()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        var folder = cut.FindAll(".tm-file-manager__item").First(e => e.TextContent.Contains("Documents"));
        folder.DoubleClick();

        cut.Markup.Should().Contain("Documents");
    }

    [Fact]
    public void TmFileManager_Select_Item_Highlights()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        var item = cut.Find(".tm-file-manager__item");
        item.Click();

        // Re-query after render to get updated class list
        var selected = cut.FindAll(".tm-file-manager__item--selected");
        selected.Count.Should().Be(1);
        selected[0].TextContent.Should().Contain("Documents");
    }

    [Fact]
    public void TmFileManager_Disabled_Hides_Toolbar_Actions()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider())
            .Add(c => c.Disabled, true));

        cut.FindAll(".tm-file-manager__toolbar-button").Should().BeEmpty();
    }

    [Fact]
    public void TmFileManager_ShowUploadButton_False_Hides_Upload()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider())
            .Add(c => c.ShowUploadButton, false));

        cut.FindAll(".tm-file-manager__upload").Should().BeEmpty();
    }

    [Fact]
    public void TmFileManager_Custom_Class_Applied()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider())
            .Add(c => c.Class, "my-custom-class"));

        cut.Find(".tm-file-manager").ClassList.Should().Contain("my-custom-class");
    }

    [Fact]
    public void TmFileManager_Rename_Button_Shows_Input_When_Selected()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        // Select the first item (Documents folder)
        var item = cut.FindAll(".tm-file-manager__item").First(e => e.TextContent.Contains("Documents"));
        item.Click();

        // Click rename button
        var renameBtn = cut.FindAll(".tm-file-manager__toolbar-button")
            .First(b => b.TextContent.Contains("Rename"));
        renameBtn.Click();

        // Should show rename input for selected item
        cut.FindAll(".tm-file-manager__item-rename-input").Count.Should().Be(1);
    }

    [Fact]
    public void TmFileManager_Create_Folder_Starts_Inline_Rename()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        // Click New Folder button
        var newFolderBtn = cut.FindAll(".tm-file-manager__toolbar-button")
            .First(b => b.TextContent.Contains("New Folder"));
        newFolderBtn.Click();

        // Should show rename input for the newly created folder
        cut.FindAll(".tm-file-manager__item-rename-input").Count.Should().Be(1);
        cut.Markup.Should().Contain("New Folder");
    }

    [Fact]
    public void TmFileManager_Rename_Enter_Commits()
    {
        var provider = CreateMockProvider();
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, provider));

        // Select Documents folder
        var item = cut.FindAll(".tm-file-manager__item").First(e => e.TextContent.Contains("Documents"));
        item.Click();

        // Start rename
        var renameBtn = cut.FindAll(".tm-file-manager__toolbar-button")
            .First(b => b.TextContent.Contains("Rename"));
        renameBtn.Click();

        // Type new name and press Enter
        var input = cut.Find(".tm-file-manager__item-rename-input");
        input.Input("RenamedDocs");
        input.KeyDown("Enter");

        // Input should disappear and new name should be visible
        cut.FindAll(".tm-file-manager__item-rename-input").Should().BeEmpty();
        cut.Markup.Should().Contain("RenamedDocs");
    }

    [Fact]
    public void TmFileManager_Rename_Escape_Cancels()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        // Select Documents folder
        var item = cut.FindAll(".tm-file-manager__item").First(e => e.TextContent.Contains("Documents"));
        item.Click();

        // Start rename
        var renameBtn = cut.FindAll(".tm-file-manager__toolbar-button")
            .First(b => b.TextContent.Contains("Rename"));
        renameBtn.Click();

        // Type new name and press Escape
        var input = cut.Find(".tm-file-manager__item-rename-input");
        input.Input("AbortedName");
        input.KeyDown("Escape");

        // Input should disappear and original name should remain
        cut.FindAll(".tm-file-manager__item-rename-input").Should().BeEmpty();
        cut.Markup.Should().Contain("Documents");
        cut.Markup.Should().NotContain("AbortedName");
    }

    [Fact]
    public void TmFileManager_Rename_Blur_Commits()
    {
        var provider = CreateMockProvider();
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, provider));

        // Select Documents folder
        var item = cut.FindAll(".tm-file-manager__item").First(e => e.TextContent.Contains("Documents"));
        item.Click();

        // Start rename
        var renameBtn = cut.FindAll(".tm-file-manager__toolbar-button")
            .First(b => b.TextContent.Contains("Rename"));
        renameBtn.Click();

        // Type new name and blur
        var input = cut.Find(".tm-file-manager__item-rename-input");
        input.Input("BlurredDocs");
        input.Blur();

        // Input should disappear and new name should be visible
        cut.FindAll(".tm-file-manager__item-rename-input").Should().BeEmpty();
        cut.Markup.Should().Contain("BlurredDocs");
    }

    [Fact]
    public void TmFileManager_Rename_Enter_Does_Not_Open_Folder()
    {
        var provider = CreateMockProvider();
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, provider));

        // Select Documents folder and start rename
        var item = cut.FindAll(".tm-file-manager__item").First(e => e.TextContent.Contains("Documents"));
        item.Click();
        var renameBtn = cut.FindAll(".tm-file-manager__toolbar-button")
            .First(b => b.TextContent.Contains("Rename"));
        renameBtn.Click();

        // Press Enter to commit rename
        var input = cut.Find(".tm-file-manager__item-rename-input");
        input.Input("RenamedDocs");
        input.KeyDown("Enter");

        // Should still be in root (both root folders visible), not navigated into Documents
        cut.Markup.Should().Contain("Pictures");
        cut.Markup.Should().Contain("RenamedDocs");
    }

    [Fact]
    public void TmFileManager_Delete_Click_Shows_Confirm_Dialog()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        // Select Documents folder
        var item = cut.FindAll(".tm-file-manager__item").First(e => e.TextContent.Contains("Documents"));
        item.Click();

        // Click delete button
        var deleteBtn = cut.FindAll(".tm-file-manager__toolbar-button")
            .First(b => b.TextContent.Contains("Delete"));
        deleteBtn.Click();

        // Should show delete confirmation dialog
        cut.Find(".tm-dialog").Should().NotBeNull();
        cut.Markup.Should().Contain("Delete Items");
    }

    [Fact]
    public void TmFileManager_Delete_Confirm_Removes_Item()
    {
        var provider = CreateMockProvider();
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, provider));

        // Select Documents folder
        var item = cut.FindAll(".tm-file-manager__item").First(e => e.TextContent.Contains("Documents"));
        item.Click();

        // Click delete button
        var deleteBtn = cut.FindAll(".tm-file-manager__toolbar-button")
            .First(b => b.TextContent.Contains("Delete"));
        deleteBtn.Click();

        // Confirm delete
        var confirmBtn = cut.FindAll(".tm-dialog-footer button")
            .First(b => b.TextContent.Contains("Delete"));
        confirmBtn.Click();

        // Item should be removed
        cut.Markup.Should().NotContain("Documents");
    }

    [Fact]
    public void TmFileManager_Delete_Cancel_Keeps_Item()
    {
        var provider = CreateMockProvider();
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, provider));

        // Select Documents folder
        var item = cut.FindAll(".tm-file-manager__item").First(e => e.TextContent.Contains("Documents"));
        item.Click();

        // Click delete button
        var deleteBtn = cut.FindAll(".tm-file-manager__toolbar-button")
            .First(b => b.TextContent.Contains("Delete"));
        deleteBtn.Click();

        // Cancel delete
        var cancelBtn = cut.FindAll(".tm-dialog-footer button")
            .First(b => b.TextContent.Contains("Cancel"));
        cancelBtn.Click();

        // Dialog should close and item should still exist
        cut.FindAll(".tm-dialog").Should().BeEmpty();
        cut.Markup.Should().Contain("Documents");
    }

    [Fact]
    public async Task TmFileManager_Upload_Preserves_FileName()
    {
        var provider = CreateMockProvider();
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, provider));

        // Simulate file upload via InputFile
        var inputFile = cut.FindComponent<InputFile>();
        inputFile.UploadFiles(
            InputFileContent.CreateFromText("hello", "MyDocument.pdf", contentType: "application/pdf"));

        await cut.InvokeAsync(() => { });

        // Uploaded file should appear with its original name
        cut.Markup.Should().Contain("MyDocument.pdf");
    }

    [Fact]
    public void TmFileManager_Keyboard_ArrowDown_Selects_Next_Item()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        var wrapper = cut.Find(".tm-file-manager");
        wrapper.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        // focusedIndex starts at 0 (Documents), ArrowDown → 1 (Pictures)
        var selected = cut.FindAll(".tm-file-manager__item--selected");
        selected.Count.Should().Be(1);
        selected[0].TextContent.Should().Contain("Pictures");
    }

    [Fact]
    public void TmFileManager_Keyboard_ArrowDown_Twice_Clamps_To_Last()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        var wrapper = cut.Find(".tm-file-manager");
        wrapper.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        wrapper.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        // Only 2 items in root; second ArrowDown should clamp to last item (Pictures)
        var selected = cut.FindAll(".tm-file-manager__item--selected");
        selected.Count.Should().Be(1);
        selected[0].TextContent.Should().Contain("Pictures");
    }

    [Fact]
    public void TmFileManager_Keyboard_Enter_Navigates_Folder()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        var wrapper = cut.Find(".tm-file-manager");
        // focusedIndex starts at 0 = Documents
        wrapper.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        // Should navigate into Documents and show Report.pdf
        cut.Markup.Should().Contain("Report.pdf");
    }

    [Fact]
    public void TmFileManager_Enter_On_Toolbar_Button_Does_Not_Reach_Grid_Handler()
    {
        // Toolbar buttons are native <button> elements nested under the grid
        // container: the browser turns Enter into a click on keydown, and the
        // bubbled keydown must not ALSO reach the grid's Enter case (which
        // would open/navigate the focused item). The toolbar is isolated with
        // @onkeydown:stopPropagation, so bUnit finds no reachable onkeydown
        // handler for an event dispatched inside it and throws — proving the
        // keydown can never reach the grid handler.
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        var newFolderButton = cut.FindAll("button.tm-file-manager__toolbar-button").First();
        var act = () => newFolderButton.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        act.Should().Throw<MissingEventHandlerException>();
    }

    [Fact]
    public void TmFileManager_Keyboard_Delete_Shows_Confirm_Dialog()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        var wrapper = cut.Find(".tm-file-manager");
        wrapper.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        wrapper.KeyDown(new KeyboardEventArgs { Key = "Delete" });

        cut.Find(".tm-dialog").Should().NotBeNull();
    }

    [Fact]
    public void TmFileManager_Keyboard_F2_Starts_Rename()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        var wrapper = cut.Find(".tm-file-manager");
        wrapper.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        wrapper.KeyDown(new KeyboardEventArgs { Key = "F2" });

        cut.FindAll(".tm-file-manager__item-rename-input").Count.Should().Be(1);
    }

    [Fact]
    public void TmFileManager_Keyboard_CtrlA_Selects_All()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        var wrapper = cut.Find(".tm-file-manager");
        wrapper.KeyDown(new KeyboardEventArgs { Key = "a", CtrlKey = true });

        // Root contains Documents and Pictures folders
        var selected = cut.FindAll(".tm-file-manager__item--selected");
        selected.Count.Should().Be(2);
    }

    [Fact]
    public void TmFileManager_Keyboard_Backspace_Goes_Up()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        // Navigate to Documents first
        var folder = cut.FindAll(".tm-file-manager__item").First(e => e.TextContent.Contains("Documents"));
        folder.DoubleClick();

        // Now in Documents — press Backspace
        var wrapper = cut.Find(".tm-file-manager");
        wrapper.KeyDown(new KeyboardEventArgs { Key = "Backspace" });

        // Should be back at root
        cut.Markup.Should().Contain("Documents");
        cut.Markup.Should().Contain("Pictures");
    }

    [Fact]
    public void TmFileManager_CtrlClick_Toggles_Selection()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        var items = cut.FindAll(".tm-file-manager__item");
        items[0].Click(new MouseEventArgs { CtrlKey = true });

        // Re-query after render
        items = cut.FindAll(".tm-file-manager__item");
        items[1].Click(new MouseEventArgs { CtrlKey = true });

        var selected = cut.FindAll(".tm-file-manager__item--selected");
        selected.Count.Should().Be(2);
    }

    [Fact]
    public void TmFileManager_CtrlClick_Deselects()
    {
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, CreateMockProvider()));

        var items = cut.FindAll(".tm-file-manager__item");
        items[0].Click(new MouseEventArgs { CtrlKey = true });

        // Re-query after render
        items = cut.FindAll(".tm-file-manager__item");
        items[0].Click(new MouseEventArgs { CtrlKey = true });

        var selected = cut.FindAll(".tm-file-manager__item--selected");
        selected.Count.Should().Be(0);
    }

    [Fact]
    public void TmFileManager_ShiftClick_Selects_Range()
    {
        var provider = CreateMockProvider();
        var cut = Render<TmFileManager>(p => p
            .Add(c => c.DataProvider, provider));

        var items = cut.FindAll(".tm-file-manager__item");
        items[0].Click(); // normal click = anchor

        // Re-query after render
        items = cut.FindAll(".tm-file-manager__item");
        items[1].Click(new MouseEventArgs { ShiftKey = true });

        var selected = cut.FindAll(".tm-file-manager__item--selected");
        selected.Count.Should().Be(2);
    }

    // ── Mock Provider ────────────────────────────────────────────

    private sealed class MockFileManagerDataProvider : IFileManagerDataProvider
    {
        private readonly List<FileManagerItem> _allItems;

        public MockFileManagerDataProvider(List<FileManagerItem> items)
        {
            _allItems = items;
        }

        public Task<IReadOnlyList<FileManagerItem>> GetFolderContentsAsync(string? folderPath = null, CancellationToken cancellationToken = default)
        {
            var path = folderPath ?? "/";
            var normalizedPath = path.TrimEnd('/');
            if (string.IsNullOrEmpty(normalizedPath)) normalizedPath = "/";

            var children = _allItems
                .Where(i => i.Path != path && GetParentPath(i.Path) == normalizedPath)
                .ToList();
            return Task.FromResult<IReadOnlyList<FileManagerItem>>(children);
        }

        private static string GetParentPath(string itemPath)
        {
            itemPath = itemPath.TrimEnd('/');
            var lastSlash = itemPath.LastIndexOf('/');
            if (lastSlash <= 0) return "/";
            return itemPath.Substring(0, lastSlash);
        }

        public Task<IReadOnlyList<FileManagerItem>> GetFolderTreeAsync(CancellationToken cancellationToken = default)
        {
            var folders = _allItems.Where(i => i.IsDirectory).ToList();
            return Task.FromResult<IReadOnlyList<FileManagerItem>>(folders);
        }

        public Task<FileManagerItem> CreateFolderAsync(string parentPath, string folderName, CancellationToken cancellationToken = default)
        {
            var item = new FileManagerItem { Id = Guid.NewGuid().ToString(), Name = folderName, Path = $"{parentPath.TrimEnd('/')}/{folderName}", IsDirectory = true };
            _allItems.Add(item);
            return Task.FromResult(item);
        }

        public Task<FileManagerItem> RenameAsync(string itemPath, string newName, CancellationToken cancellationToken = default)
        {
            var item = _allItems.First(i => i.Path == itemPath);
            item.Name = newName;
            item.Path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(itemPath)!, newName).Replace("\\", "/");
            return Task.FromResult(item);
        }

        public Task DeleteAsync(IReadOnlyList<string> itemPaths, CancellationToken cancellationToken = default)
        {
            _allItems.RemoveAll(i => itemPaths.Contains(i.Path));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FileManagerItem>> UploadAsync(string folderPath, IReadOnlyList<FileUploadInfo> files, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            foreach (var file in files)
            {
                var path = $"{folderPath.TrimEnd('/')}/{file.FileName}";
                _allItems.Add(new FileManagerItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = file.FileName,
                    Path = path,
                    IsDirectory = false,
                    Size = file.Size,
                    Extension = System.IO.Path.GetExtension(file.FileName)
                });
            }
            return Task.FromResult<IReadOnlyList<FileManagerItem>>(files.Select(f => _allItems.Last(i => i.Name == f.FileName)).ToList());
        }

        public Task<Stream> DownloadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Stream>(new System.IO.MemoryStream());
        }
    }
}
