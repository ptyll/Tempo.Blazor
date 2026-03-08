using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// Base class for all bUnit component tests.
/// Registers a default English MockTmLocalizer so that @inject ITmLocalizer Loc
/// is always resolved in the test DI container.
/// Tests can override specific keys by calling Services.AddSingleton&lt;ITmLocalizer&gt;(...)
/// AFTER base registration (last registration wins in .NET DI).
/// </summary>
public abstract class LocalizationTestBase : TestContext
{
    protected LocalizationTestBase()
    {
        Services.AddSingleton<ITmLocalizer>(BuildEnglishLocalizer());
    }

    /// <summary>Registers a Czech localizer for this test context.</summary>
    protected void UseCzechLocalization()
    {
        Services.AddSingleton<ITmLocalizer>(BuildCzechLocalizer());
    }

    /// <summary>Registers a custom localizer with the provided key/value pairs.</summary>
    protected void UseCustomLocalization(Dictionary<string, string> keys)
    {
        Services.AddSingleton<ITmLocalizer>(new MockTmLocalizer(keys));
    }

    private static MockTmLocalizer BuildEnglishLocalizer() => new(new Dictionary<string, string>
    {
        // Shared
        ["Tm_Loading"]          = "Loading...",
        ["Tm_Close"]            = "Close",
        ["Tm_Cancel"]           = "Cancel",
        ["Tm_Save"]             = "Save",
        ["Tm_Delete"]           = "Delete",
        ["Tm_Confirm"]          = "Confirm",
        ["Tm_NoResults"]        = "No results found",
        ["Tm_Error"]            = "An error occurred",
        ["Tm_Retry"]            = "Retry",
        ["Tm_Search"]           = "Search",
        ["Tm_Clear"]            = "Clear",
        ["Tm_Select"]           = "Select",
        ["Tm_SelectAll"]        = "Select all",
        ["Tm_DeselectAll"]      = "Deselect all",
        ["Tm_Edit"]             = "Edit",
        ["Tm_Add"]              = "Add",
        ["Tm_Remove"]           = "Remove",
        ["Tm_Apply"]            = "Apply",
        ["Tm_Reset"]            = "Reset",
        ["Tm_Previous"]         = "Previous",
        ["Tm_Next"]             = "Next",
        ["Tm_Download"]         = "Download",
        ["Tm_Upload"]           = "Upload",
        ["Tm_Create"]           = "Create",
        ["Tm_Optional"]         = "Optional",
        ["Tm_Required"]         = "Required",
        ["Tm_Yes"]              = "Yes",
        ["Tm_No"]               = "No",

        // TmButton
        ["TmButton_Loading"] = "Loading",

        // TmSearchInput
        ["TmSearchInput_Placeholder"] = "Search...",
        ["TmSearchInput_Clear"]       = "Clear search",

        // TmEmptyState
        ["TmEmptyState_DefaultTitle"] = "No data available",

        // TmDataTable
        ["TmDataTable_NoResults"]          = "No results found",
        ["TmDataTable_Loading"]            = "Loading data...",
        ["TmDataTable_PageSize"]           = "Items per page",
        ["TmDataTable_Pagination"]         = "{0}–{1} of {2}",
        ["TmDataTable_SelectAll"]          = "Select all rows",
        ["TmDataTable_DeselectAll"]        = "Deselect all rows",
        ["TmDataTable_ColumnPicker"]       = "Choose columns",
        ["TmDataTable_ResetColumns"]       = "Reset to default",
        ["TmDataTable_SaveView"]           = "Save view",
        ["TmDataTable_ManageViews"]        = "Manage views",
        ["TmDataTable_DefaultView"]        = "Default view",
        ["TmDataTable_Filter"]             = "Filter",
        ["TmDataTable_ClearFilters"]       = "Clear filters",
        ["TmDataTable_SortAscending"]      = "Sort ascending",
        ["TmDataTable_SortDescending"]     = "Sort descending",
        ["TmDataTable_BulkActions"]        = "{0} selected",
        ["TmDataTable_DeleteSelected"]     = "Delete selected",
        ["TmDataTable_EditInline"]         = "Edit",
        ["TmDataTable_SaveInline"]         = "Save changes",
        ["TmDataTable_CancelInline"]       = "Cancel edit",
        ["TmDataTable_FirstPage"]          = "First page",
        ["TmDataTable_LastPage"]           = "Last page",
        ["TmDataTable_ViewName"]           = "View name",
        ["TmDataTable_ViewNamePlaceholder"]= "Enter view name...",
        ["TmDataTable_DeleteView"]         = "Delete view",

        // TmFilterableDropdown
        ["TmFilterableDropdown_Search"]        = "Search...",
        ["TmFilterableDropdown_NoResults"]     = "No results found",
        ["TmFilterableDropdown_Loading"]       = "Loading...",
        ["TmFilterableDropdown_Error"]         = "Failed to load options",
        ["TmFilterableDropdown_Retry"]         = "Retry",
        ["TmFilterableDropdown_CreateNew"]     = "Create \"{0}\"",
        ["TmFilterableDropdown_ClearSelection"]= "Clear selection",
        ["TmFilterableDropdown_Placeholder"]   = "Select an option...",

        // TmFileDropZone
        ["TmFileDropZone_DragDrop"]      = "Drag and drop files here",
        ["TmFileDropZone_Or"]            = "or",
        ["TmFileDropZone_Browse"]        = "Browse files",
        ["TmFileDropZone_Uploading"]     = "Uploading...",
        ["TmFileDropZone_Remove"]        = "Remove file",
        ["TmFileDropZone_MaxSize"]       = "Maximum file size: {0}",
        ["TmFileDropZone_AcceptedTypes"] = "Accepted types: {0}",

        // TmTimeline
        ["TmTimeline_NoEntries"]   = "No activity yet",
        ["TmTimeline_Internal"]    = "Internal",
        ["TmTimeline_JustNow"]     = "Just now",
        ["TmTimeline_MinutesAgo"]  = "{0} min ago",
        ["TmTimeline_HoursAgo"]    = "{0} h ago",
        ["TmTimeline_DaysAgo"]     = "{0} days ago",
        ["TmTimeline_DateFormat"]  = "MMM d, yyyy",

        // TmTreeView
        ["TmTreeView_Search"]    = "Search...",
        ["TmTreeView_NoResults"] = "No items found",
        ["TmTreeView_Loading"]   = "Loading...",
        ["TmTreeView_Expand"]    = "Expand",
        ["TmTreeView_Collapse"]  = "Collapse",

        // TmTagPicker
        ["TmTagPicker_Search"]    = "Search tags...",
        ["TmTagPicker_CreateNew"] = "Create \"{0}\"",
        ["TmTagPicker_NoTags"]    = "No tags found",
        ["TmTagPicker_AddTag"]    = "Add tag",

        // TmCommandPalette
        ["TmCommandPalette_Placeholder"] = "Type a command or search...",
        ["TmCommandPalette_NoResults"]   = "No commands found",

        // TmStepper
        ["TmStepper_Step"]      = "Step {0} of {1}",
        ["TmStepper_Completed"] = "Completed",

        // TmAvatar
        ["TmAvatar_ImageAlt"] = "User avatar",

        // TmNotificationBell
        ["TmNotificationBell_AriaLabel"]        = "Notifications",
        ["TmNotificationBell_NewCount"]         = "{0} new notifications",
        ["TmNotificationBell_NoNotifications"]  = "No notifications",
        ["TmNotificationBell_MarkAllRead"]      = "Mark all as read",

        // TmImageGallery
        ["TmImageGallery_PreviousImage"] = "Previous image",
        ["TmImageGallery_NextImage"]     = "Next image",
        ["TmImageGallery_Close"]         = "Close gallery",
        ["TmImageGallery_Counter"]       = "{0} of {1}",
        ["TmImageGallery_LoadingImage"]  = "Loading image...",
        ["TmImageGallery_ErrorLoading"]  = "Failed to load image",
        ["TmImageGallery_ZoomIn"]        = "Zoom in",
        ["TmImageGallery_ZoomOut"]       = "Zoom out",
        ["TmImageGallery_Download"]      = "Download image",
        ["TmImageGallery_Delete"]        = "Delete image",
        ["TmImageGallery_NoImages"]      = "No images available",
        ["TmImageGallery_UploadImages"]  = "Upload images",
        ["TmImageGallery_GridView"]      = "Grid view",
        ["TmImageGallery_GalleryView"]   = "Gallery view",

        // TmLayout
        ["TmSidebar_CollapseMenu"]       = "Collapse menu",
        ["TmSidebar_ExpandMenu"]         = "Expand menu",
        ["TmTopBar_OpenSearch"]          = "Search (Ctrl+K)",
        ["TmTopBar_OpenNotifications"]   = "Open notifications",
        ["TmTopBar_OpenUserMenu"]        = "Open user menu",

        // TmCheckbox
        ["TmCheckbox_Indeterminate"] = "Partially selected",

        // Validation
        ["TmValidation_Required"] = "This field is required",
        ["TmValidation_Invalid"]  = "This field contains an invalid value",

        // TmTimePicker
        ["TmTimePicker_Hours"]              = "Hours",
        ["TmTimePicker_Minutes"]            = "Minutes",
        ["TmTimePicker_Seconds"]            = "Seconds",
        ["TmTimePicker_Placeholder"]        = "HH:mm",
        ["TmTimePicker_PlaceholderSeconds"] = "HH:mm:ss",
        ["TmTimePicker_Clear"]              = "Clear time",

        // TmDatePicker
        ["TmDatePicker_Placeholder"]    = "dd.MM.yyyy",
        ["TmDatePicker_Clear"]          = "Clear date",
        ["TmDatePicker_Today"]          = "Today",
        ["TmDatePicker_PreviousMonth"]  = "Previous month",
        ["TmDatePicker_NextMonth"]      = "Next month",

        // TmDateTimePicker
        ["TmDateTimePicker_Placeholder"] = "dd.MM.yyyy HH:mm",
        ["TmDateTimePicker_Clear"]       = "Clear",

        // TmTimeRangePicker
        ["TmTimeRangePicker_From"]         = "From",
        ["TmTimeRangePicker_To"]           = "To",
        ["TmTimeRangePicker_Duration"]     = "Duration",
        ["TmTimeRangePicker_Swap"]         = "Swap",
        ["TmTimeRangePicker_InvalidRange"] = "End time must be after start time",

        // TmDateRangePicker
        ["TmDateRangePicker_From"]   = "From",
        ["TmDateRangePicker_To"]     = "To",
        ["TmDateRangePicker_Clear"]  = "Clear range",

        // TmDateTimeRangePicker
        ["TmDateTimeRangePicker_StartLabel"]      = "Start",
        ["TmDateTimeRangePicker_EndLabel"]        = "End",
        ["TmDateTimeRangePicker_Clear"]           = "Clear range",
        ["TmDateTimeRangePicker_ValidationError"] = "End must be after start",

        // TmFilterBuilder operators
        ["TmFilter_Contains"]      = "contains",
        ["TmFilter_NotContains"]   = "does not contain",
        ["TmFilter_Equals"]        = "equals",
        ["TmFilter_NotEquals"]     = "not equals",
        ["TmFilter_GreaterThan"]   = "greater than",
        ["TmFilter_LessThan"]      = "less than",
        ["TmFilter_GreaterOrEqual"]= "greater or equal",
        ["TmFilter_LessOrEqual"]   = "less or equal",
        ["TmFilter_Between"]       = "between",
        ["TmFilter_IsEmpty"]       = "is empty",
        ["TmFilter_IsNotEmpty"]    = "is not empty",
        ["TmFilter_In"]            = "in",
        ["TmFilter_NotIn"]         = "not in",

        // TmMultiViewList
        ["TmMvl_TableView"]   = "Table view",
        ["TmMvl_CardView"]    = "Card view",
        ["TmMvl_ListView"]    = "List view",
        ["TmMvl_ColTitle"]    = "Title",
        ["TmMvl_ColSubTitle"] = "Subtitle",
        ["TmMvl_ColStatus"]   = "Status",
        ["TmMvl_ColDate"]     = "Date",

        // TmValidationSummary
        ["TmValidationSummary_Title"] = "There were errors with your submission",

        // TmPasswordStrengthIndicator
        ["TmPasswordStrength_VeryWeak"]                = "Very weak",
        ["TmPasswordStrength_Weak"]                    = "Weak",
        ["TmPasswordStrength_Medium"]                  = "Medium",
        ["TmPasswordStrength_Strong"]                  = "Strong",
        ["TmPasswordStrength_VeryStrong"]              = "Very strong",
        ["TmPasswordStrength_HintUseAtLeast8Chars"]    = "Use at least 8 characters",
        ["TmPasswordStrength_HintAddLetters"]          = "Add letters",
        ["TmPasswordStrength_HintAddNumbers"]          = "Add numbers",
        ["TmPasswordStrength_HintAddSpecialChars"]     = "Add special characters",
        ["TmPasswordStrength_HintAddUppercase"]        = "Add uppercase letters",
        ["TmPasswordStrength_HintAddLowercase"]        = "Add lowercase letters",
        ["TmPasswordStrength_HintMakeItLonger"]        = "Make it longer",
        ["TmPasswordStrength_HintAvoidCommonPatterns"] = "Avoid common patterns",
        ["TmPasswordStrength_HintAvoidCommonWords"]    = "Avoid common words",
        ["TmPasswordStrength_HintExcellent"]           = "Excellent password!",

        // TmToast
        ["TmToast_Close"] = "Dismiss",

        // TmAlert
        ["TmAlert_Dismiss"] = "Dismiss",

        // TmDrawer
        ["TmDrawer_Close"] = "Close panel",

        // TmBulkActionBar
        ["TmBulkAction_Toolbar"] = "Bulk actions",
        ["TmBulkAction_Selected"] = "selected",
        ["TmBulkAction_ClearSelection"] = "Clear selection",

        // TmDataTable Grouping
        ["TmDataTable_GroupDropPlaceholder"] = "Drag column headers here to group",
        ["TmDataTable_ExpandAll"] = "Expand all",
        ["TmDataTable_CollapseAll"] = "Collapse all",
        ["TmDataTable_GroupCount"] = "{0} items",
        ["TmDataTable_SearchPlaceholder"] = "Search...",
        ["TmDataTable_ShowingItems"] = "{0}–{1} of {2}",
        ["TmDataTable_CurrentViewName"] = "Current",

        // TmMultiViewList Grouping
        ["TmMvl_GroupBy"] = "Group by",
        ["TmMvl_GroupNone"] = "No grouping",

        // TmViewManager Grouping
        ["TmViewManager_Grouping"] = "Grouping",
        ["TmViewManager_GroupByColumns"] = "Group by columns",

        // TmInlineEdit
        ["TmInlineEdit_Placeholder"] = "Click to edit",

        // TmRichEditor - Tokens
        ["TmRichEditor_Token"]          = "Insert variable",
        ["TmRichEditor_TokenEmpty"]     = "No variables found",
        ["TmRichEditor_TokenCreateNew"] = "Create new variable...",
        ["TmRichEditor_TokenSearch"]    = "Search variables...",
        ["TmRichEditor_MentionEmpty"]   = "No users found",
        ["TmRichEditor_Bold"]           = "Bold",
        ["TmRichEditor_Italic"]         = "Italic",
        ["TmRichEditor_Underline"]      = "Underline",
        ["TmRichEditor_Link"]           = "Link",
        ["TmRichEditor_TaskList"]       = "Task list",
        ["TmRichEditor_Emoji"]          = "Emoji",
        ["TmRichEditor_UnorderedList"]  = "Bullet list",
        ["TmRichEditor_OrderedList"]    = "Numbered list",
        ["TmRichEditor_Undo"]          = "Undo",
        ["TmRichEditor_Redo"]          = "Redo",
        ["TmRichEditor_Heading1"]      = "Heading 1",
        ["TmRichEditor_Heading2"]      = "Heading 2",
        ["TmRichEditor_Heading3"]      = "Heading 3",
        ["TmRichEditor_Heading4"]      = "Heading 4",
        ["TmRichEditor_Strikethrough"] = "Strikethrough",
        ["TmRichEditor_Subscript"]     = "Subscript",
        ["TmRichEditor_Superscript"]   = "Superscript",
        ["TmRichEditor_Indent"]        = "Indent",
        ["TmRichEditor_Outdent"]       = "Outdent",
        ["TmRichEditor_Code"]          = "Code block",
        ["TmRichEditor_Blockquote"]    = "Blockquote",
        ["TmRichEditor_HorizontalRule"]= "Horizontal line",
        ["TmRichEditor_TextColor"]     = "Text color",
        ["TmRichEditor_Highlight"]     = "Highlight",
        ["TmRichEditor_Image"]         = "Image",
        ["TmRichEditor_Table"]         = "Table",
        ["TmRichEditor_Video"]         = "Video",
        ["TmRichEditor_RemoveFormat"]  = "Remove formatting",

        // TmDashboard
        ["TmDashboard_Edit"]                  = "Edit",
        ["TmDashboard_EditMode"]              = "Edit Mode",
        ["TmDashboard_CancelEdit"]            = "Cancel",
        ["TmDashboard_SaveChanges"]           = "Save Changes",
        ["TmDashboard_AddWidget"]             = "Add Widget",
        ["TmDashboard_AddWidgets"]            = "Add Widgets",
        ["TmDashboard_Dashboards"]            = "Dashboards",
        ["TmDashboard_MyDashboards"]          = "My Dashboards",
        ["TmDashboard_CreateNewDashboard"]    = "Create New Dashboard",
        ["TmDashboard_DashboardName"]         = "Dashboard Name",
        ["TmDashboard_DefaultDashboard"]      = "Default",
        ["TmDashboard_SetAsDefault"]          = "Set as Default",
        ["TmDashboard_Delete"]                = "Delete",
        ["TmDashboard_DeleteDashboard"]       = "Delete Dashboard",
        ["TmDashboard_DeleteDashboardConfirm"]= "Are you sure you want to delete this dashboard?",
        ["TmDashboard_NoWidgets"]             = "No widgets added yet",
    });

    private static MockTmLocalizer BuildCzechLocalizer() => new(new Dictionary<string, string>
    {
        // Shared
        ["Tm_Loading"]     = "Načítání...",
        ["Tm_Close"]       = "Zavřít",
        ["Tm_Cancel"]      = "Zrušit",
        ["Tm_Save"]        = "Uložit",
        ["Tm_Delete"]      = "Smazat",
        ["Tm_Confirm"]     = "Potvrdit",
        ["Tm_NoResults"]   = "Žádné výsledky",
        ["Tm_Error"]       = "Došlo k chybě",
        ["Tm_Retry"]       = "Zkusit znovu",
        ["Tm_Search"]      = "Hledat",
        ["Tm_Clear"]       = "Vymazat",
        ["Tm_Select"]      = "Vybrat",
        ["Tm_SelectAll"]   = "Vybrat vše",
        ["Tm_DeselectAll"] = "Zrušit výběr",
        ["Tm_Edit"]        = "Upravit",
        ["Tm_Add"]         = "Přidat",
        ["Tm_Remove"]      = "Odebrat",
        ["Tm_Apply"]       = "Použít",
        ["Tm_Reset"]       = "Obnovit",
        ["Tm_Previous"]    = "Předchozí",
        ["Tm_Next"]        = "Další",
        ["Tm_Download"]    = "Stáhnout",
        ["Tm_Upload"]      = "Nahrát",
        ["Tm_Create"]      = "Vytvořit",
        ["Tm_Optional"]    = "Volitelné",
        ["Tm_Required"]    = "Povinné",
        ["Tm_Yes"]         = "Ano",
        ["Tm_No"]          = "Ne",

        // TmButton
        ["TmButton_Loading"] = "Načítání",

        // TmSearchInput
        ["TmSearchInput_Placeholder"] = "Hledat...",
        ["TmSearchInput_Clear"]       = "Vymazat hledání",

        // TmEmptyState
        ["TmEmptyState_DefaultTitle"] = "Žádná data",

        // TmDataTable
        ["TmDataTable_NoResults"]          = "Žádné výsledky",
        ["TmDataTable_Loading"]            = "Načítání dat...",
        ["TmDataTable_PageSize"]           = "Položek na stránku",
        ["TmDataTable_Pagination"]         = "{0}–{1} z {2}",
        ["TmDataTable_SelectAll"]          = "Vybrat všechny řádky",
        ["TmDataTable_DeselectAll"]        = "Zrušit výběr",
        ["TmDataTable_ColumnPicker"]       = "Vybrat sloupce",
        ["TmDataTable_ResetColumns"]       = "Obnovit výchozí",
        ["TmDataTable_SaveView"]           = "Uložit pohled",
        ["TmDataTable_ManageViews"]        = "Spravovat pohledy",
        ["TmDataTable_DefaultView"]        = "Výchozí pohled",
        ["TmDataTable_Filter"]             = "Filtr",
        ["TmDataTable_ClearFilters"]       = "Zrušit filtry",
        ["TmDataTable_SortAscending"]      = "Řadit vzestupně",
        ["TmDataTable_SortDescending"]     = "Řadit sestupně",
        ["TmDataTable_BulkActions"]        = "{0} vybráno",
        ["TmDataTable_DeleteSelected"]     = "Smazat vybrané",
        ["TmDataTable_EditInline"]         = "Upravit",
        ["TmDataTable_SaveInline"]         = "Uložit změny",
        ["TmDataTable_CancelInline"]       = "Zrušit úpravy",
        ["TmDataTable_FirstPage"]          = "První stránka",
        ["TmDataTable_LastPage"]           = "Poslední stránka",
        ["TmDataTable_ViewName"]           = "Název pohledu",
        ["TmDataTable_ViewNamePlaceholder"]= "Zadejte název pohledu...",
        ["TmDataTable_DeleteView"]         = "Smazat pohled",

        // TmFilterableDropdown
        ["TmFilterableDropdown_Search"]        = "Hledat...",
        ["TmFilterableDropdown_NoResults"]     = "Žádné výsledky",
        ["TmFilterableDropdown_Loading"]       = "Načítání...",
        ["TmFilterableDropdown_Error"]         = "Nepodařilo se načíst možnosti",
        ["TmFilterableDropdown_Retry"]         = "Zkusit znovu",
        ["TmFilterableDropdown_CreateNew"]     = "Vytvořit \"{0}\"",
        ["TmFilterableDropdown_ClearSelection"]= "Zrušit výběr",
        ["TmFilterableDropdown_Placeholder"]   = "Vyberte možnost...",

        // TmFileDropZone
        ["TmFileDropZone_DragDrop"]      = "Přetáhněte soubory sem",
        ["TmFileDropZone_Or"]            = "nebo",
        ["TmFileDropZone_Browse"]        = "Vybrat soubory",
        ["TmFileDropZone_Uploading"]     = "Nahrávání...",
        ["TmFileDropZone_Remove"]        = "Odebrat soubor",
        ["TmFileDropZone_MaxSize"]       = "Maximální velikost: {0}",
        ["TmFileDropZone_AcceptedTypes"] = "Povolené typy: {0}",

        // TmTimeline
        ["TmTimeline_NoEntries"]  = "Žádná aktivita",
        ["TmTimeline_Internal"]   = "Interní",
        ["TmTimeline_JustNow"]    = "Právě teď",
        ["TmTimeline_MinutesAgo"] = "před {0} min",
        ["TmTimeline_HoursAgo"]   = "před {0} h",
        ["TmTimeline_DaysAgo"]    = "před {0} dny",
        ["TmTimeline_DateFormat"] = "d. M. yyyy",

        // TmTreeView
        ["TmTreeView_Search"]    = "Hledat...",
        ["TmTreeView_NoResults"] = "Žádné položky",
        ["TmTreeView_Loading"]   = "Načítání...",
        ["TmTreeView_Expand"]    = "Rozbalit",
        ["TmTreeView_Collapse"]  = "Sbalit",

        // TmTagPicker
        ["TmTagPicker_Search"]    = "Hledat štítky...",
        ["TmTagPicker_CreateNew"] = "Vytvořit \"{0}\"",
        ["TmTagPicker_NoTags"]    = "Žádné štítky",
        ["TmTagPicker_AddTag"]    = "Přidat štítek",

        // TmCommandPalette
        ["TmCommandPalette_Placeholder"] = "Zadejte příkaz nebo hledejte...",
        ["TmCommandPalette_NoResults"]   = "Žádné příkazy nenalezeny",

        // TmStepper
        ["TmStepper_Step"]      = "Krok {0} z {1}",
        ["TmStepper_Completed"] = "Dokončeno",

        // TmAvatar
        ["TmAvatar_ImageAlt"] = "Avatar uživatele",

        // TmNotificationBell
        ["TmNotificationBell_AriaLabel"]       = "Oznámení",
        ["TmNotificationBell_NewCount"]        = "{0} nových oznámení",
        ["TmNotificationBell_NoNotifications"] = "Žádná oznámení",
        ["TmNotificationBell_MarkAllRead"]     = "Označit vše jako přečtené",

        // TmImageGallery
        ["TmImageGallery_PreviousImage"] = "Předchozí obrázek",
        ["TmImageGallery_NextImage"]     = "Další obrázek",
        ["TmImageGallery_Close"]         = "Zavřít galerii",
        ["TmImageGallery_Counter"]       = "{0} z {1}",
        ["TmImageGallery_LoadingImage"]  = "Načítání obrázku...",
        ["TmImageGallery_ErrorLoading"]  = "Obrázek se nepodařilo načíst",
        ["TmImageGallery_ZoomIn"]        = "Přiblížit",
        ["TmImageGallery_ZoomOut"]       = "Oddálit",
        ["TmImageGallery_Download"]      = "Stáhnout obrázek",
        ["TmImageGallery_Delete"]        = "Smazat obrázek",
        ["TmImageGallery_NoImages"]      = "Žádné obrázky",
        ["TmImageGallery_UploadImages"]  = "Nahrát obrázky",
        ["TmImageGallery_GridView"]      = "Mřížka",
        ["TmImageGallery_GalleryView"]   = "Galerie",

        // TmLayout
        ["TmSidebar_CollapseMenu"]     = "Sbalit menu",
        ["TmSidebar_ExpandMenu"]       = "Rozbalit menu",
        ["TmTopBar_OpenSearch"]        = "Hledat (Ctrl+K)",
        ["TmTopBar_OpenNotifications"] = "Otevřít oznámení",
        ["TmTopBar_OpenUserMenu"]      = "Otevřít nabídku uživatele",

        // TmCheckbox
        ["TmCheckbox_Indeterminate"] = "Částečně vybráno",

        // Validation
        ["TmValidation_Required"] = "Toto pole je povinné",
        ["TmValidation_Invalid"]  = "Toto pole obsahuje neplatnou hodnotu",

        // TmTimePicker
        ["TmTimePicker_Hours"]              = "Hodiny",
        ["TmTimePicker_Minutes"]            = "Minuty",
        ["TmTimePicker_Seconds"]            = "Sekundy",
        ["TmTimePicker_Placeholder"]        = "HH:mm",
        ["TmTimePicker_PlaceholderSeconds"] = "HH:mm:ss",
        ["TmTimePicker_Clear"]              = "Vymazat čas",

        // TmDatePicker
        ["TmDatePicker_Placeholder"]   = "dd.MM.yyyy",
        ["TmDatePicker_Clear"]         = "Vymazat datum",
        ["TmDatePicker_Today"]         = "Dnes",
        ["TmDatePicker_PreviousMonth"] = "Předchozí měsíc",
        ["TmDatePicker_NextMonth"]     = "Další měsíc",

        // TmDateTimePicker
        ["TmDateTimePicker_Placeholder"] = "dd.MM.yyyy HH:mm",
        ["TmDateTimePicker_Clear"]       = "Vymazat",

        // TmTimeRangePicker
        ["TmTimeRangePicker_From"]         = "Od",
        ["TmTimeRangePicker_To"]           = "Do",
        ["TmTimeRangePicker_Duration"]     = "Trvání",
        ["TmTimeRangePicker_Swap"]         = "Prohodit",
        ["TmTimeRangePicker_InvalidRange"] = "Konec musí být po začátku",

        // TmDateRangePicker
        ["TmDateRangePicker_From"]  = "Od",
        ["TmDateRangePicker_To"]    = "Do",
        ["TmDateRangePicker_Clear"] = "Vymazat rozsah",

        // TmDateTimeRangePicker
        ["TmDateTimeRangePicker_StartLabel"]      = "Začátek",
        ["TmDateTimeRangePicker_EndLabel"]        = "Konec",
        ["TmDateTimeRangePicker_Clear"]           = "Vymazat rozsah",
        ["TmDateTimeRangePicker_ValidationError"] = "Konec musí být po začátku",

        // TmFilterBuilder operators
        ["TmFilter_Contains"]      = "obsahuje",
        ["TmFilter_NotContains"]   = "neobsahuje",
        ["TmFilter_Equals"]        = "rovná se",
        ["TmFilter_NotEquals"]     = "nerovná se",
        ["TmFilter_GreaterThan"]   = "větší než",
        ["TmFilter_LessThan"]      = "menší než",
        ["TmFilter_GreaterOrEqual"]= "větší nebo rovno",
        ["TmFilter_LessOrEqual"]   = "menší nebo rovno",
        ["TmFilter_Between"]       = "mezi",
        ["TmFilter_IsEmpty"]       = "je prázdné",
        ["TmFilter_IsNotEmpty"]    = "není prázdné",
        ["TmFilter_In"]            = "v",
        ["TmFilter_NotIn"]         = "není v",

        // TmMultiViewList
        ["TmMvl_TableView"]   = "Tabulka",
        ["TmMvl_CardView"]    = "Karty",
        ["TmMvl_ListView"]    = "Seznam",
        ["TmMvl_ColTitle"]    = "Název",
        ["TmMvl_ColSubTitle"] = "Popis",
        ["TmMvl_ColStatus"]   = "Stav",
        ["TmMvl_ColDate"]     = "Datum",

        // TmValidationSummary
        ["TmValidationSummary_Title"] = "Při odesílání formuláře došlo k chybám",

        // TmPasswordStrengthIndicator
        ["TmPasswordStrength_VeryWeak"]                = "Velmi slabé",
        ["TmPasswordStrength_Weak"]                    = "Slabé",
        ["TmPasswordStrength_Medium"]                  = "Střední",
        ["TmPasswordStrength_Strong"]                  = "Silné",
        ["TmPasswordStrength_VeryStrong"]              = "Velmi silné",
        ["TmPasswordStrength_HintUseAtLeast8Chars"]    = "Použijte alespoň 8 znaků",
        ["TmPasswordStrength_HintAddLetters"]          = "Přidejte písmena",
        ["TmPasswordStrength_HintAddNumbers"]          = "Přidejte čísla",
        ["TmPasswordStrength_HintAddSpecialChars"]     = "Přidejte speciální znaky",
        ["TmPasswordStrength_HintAddUppercase"]        = "Přidejte velká písmena",
        ["TmPasswordStrength_HintAddLowercase"]        = "Přidejte malá písmena",
        ["TmPasswordStrength_HintMakeItLonger"]        = "Prodlužte heslo",
        ["TmPasswordStrength_HintAvoidCommonPatterns"] = "Vyhněte se běžným vzorům",
        ["TmPasswordStrength_HintAvoidCommonWords"]    = "Vyhněte se běžným slovům",
        ["TmPasswordStrength_HintExcellent"]           = "Vynikající heslo!",

        // TmToast
        ["TmToast_Close"] = "Zavřít",

        // TmAlert
        ["TmAlert_Dismiss"] = "Zavřít",

        // TmDrawer
        ["TmDrawer_Close"] = "Zavřít panel",

        // TmBulkActionBar
        ["TmBulkAction_Toolbar"] = "Hromadné akce",
        ["TmBulkAction_Selected"] = "vybráno",
        ["TmBulkAction_ClearSelection"] = "Zrušit výběr",

        // TmDataTable Grouping
        ["TmDataTable_GroupDropPlaceholder"] = "Přetáhněte záhlaví sloupce pro seskupení",
        ["TmDataTable_ExpandAll"] = "Rozbalit vše",
        ["TmDataTable_CollapseAll"] = "Sbalit vše",
        ["TmDataTable_GroupCount"] = "{0} položek",
        ["TmDataTable_SearchPlaceholder"] = "Hledat...",
        ["TmDataTable_ShowingItems"] = "{0}–{1} z {2}",
        ["TmDataTable_CurrentViewName"] = "Aktuální",

        // TmMultiViewList Grouping
        ["TmMvl_GroupBy"] = "Seskupit podle",
        ["TmMvl_GroupNone"] = "Bez seskupení",

        // TmViewManager Grouping
        ["TmViewManager_Grouping"] = "Seskupování",
        ["TmViewManager_GroupByColumns"] = "Seskupit podle sloupců",

        // TmInlineEdit
        ["TmInlineEdit_Placeholder"] = "Klikněte pro úpravu",

        // TmRichEditor - Tokens
        ["TmRichEditor_Token"]          = "Vložit proměnnou",
        ["TmRichEditor_TokenEmpty"]     = "Žádné proměnné nenalezeny",
        ["TmRichEditor_TokenCreateNew"] = "Vytvořit novou proměnnou...",
        ["TmRichEditor_TokenSearch"]    = "Hledat proměnné...",
        ["TmRichEditor_MentionEmpty"]   = "Uživatelé nenalezeni",
        ["TmRichEditor_Bold"]           = "Tučné",
        ["TmRichEditor_Italic"]         = "Kurzíva",
        ["TmRichEditor_Underline"]      = "Podtržené",
        ["TmRichEditor_Link"]           = "Odkaz",
        ["TmRichEditor_TaskList"]       = "Seznam úkolů",
        ["TmRichEditor_Emoji"]          = "Emoji",
        ["TmRichEditor_UnorderedList"]  = "Odrážkový seznam",
        ["TmRichEditor_OrderedList"]    = "Číslovaný seznam",
        ["TmRichEditor_Undo"]          = "Zpět",
        ["TmRichEditor_Redo"]          = "Znovu",
        ["TmRichEditor_Heading1"]      = "Nadpis 1",
        ["TmRichEditor_Heading2"]      = "Nadpis 2",
        ["TmRichEditor_Heading3"]      = "Nadpis 3",
        ["TmRichEditor_Heading4"]      = "Nadpis 4",
        ["TmRichEditor_Strikethrough"] = "Přeškrtnuté",
        ["TmRichEditor_Subscript"]     = "Dolní index",
        ["TmRichEditor_Superscript"]   = "Horní index",
        ["TmRichEditor_Indent"]        = "Odsadit",
        ["TmRichEditor_Outdent"]       = "Předsadit",
        ["TmRichEditor_Code"]          = "Blok kódu",
        ["TmRichEditor_Blockquote"]    = "Citace",
        ["TmRichEditor_HorizontalRule"]= "Vodorovná čára",
        ["TmRichEditor_TextColor"]     = "Barva textu",
        ["TmRichEditor_Highlight"]     = "Zvýraznění",
        ["TmRichEditor_Image"]         = "Obrázek",
        ["TmRichEditor_Table"]         = "Tabulka",
        ["TmRichEditor_Video"]         = "Video",
        ["TmRichEditor_RemoveFormat"]  = "Odstranit formátování",

        // TmDashboard
        ["TmDashboard_Edit"]                  = "Upravit",
        ["TmDashboard_EditMode"]              = "Režim úprav",
        ["TmDashboard_CancelEdit"]            = "Zrušit",
        ["TmDashboard_SaveChanges"]           = "Uložit změny",
        ["TmDashboard_AddWidget"]             = "Přidat widget",
        ["TmDashboard_AddWidgets"]            = "Přidat widgety",
        ["TmDashboard_Dashboards"]            = "Dashboardy",
        ["TmDashboard_MyDashboards"]          = "Moje dashboardy",
        ["TmDashboard_CreateNewDashboard"]    = "Vytvořit nový dashboard",
        ["TmDashboard_DashboardName"]         = "Název dashboardu",
        ["TmDashboard_DefaultDashboard"]      = "Výchozí",
        ["TmDashboard_SetAsDefault"]          = "Nastavit jako výchozí",
        ["TmDashboard_Delete"]                = "Smazat",
        ["TmDashboard_DeleteDashboard"]       = "Smazat dashboard",
        ["TmDashboard_DeleteDashboardConfirm"]= "Opravdu chcete smazat tento dashboard?",
        ["TmDashboard_NoWidgets"]             = "Zatím nebyly přidány žádné widgety",
    });
}

/// <summary>
/// Minimal in-memory ITmLocalizer for testing. Returns [key] for unknown keys.
/// </summary>
public sealed class MockTmLocalizer : ITmLocalizer
{
    private readonly Dictionary<string, string> _data;

    public MockTmLocalizer(Dictionary<string, string> data) => _data = data;

    public string this[string key] =>
        _data.TryGetValue(key, out var v) ? v : $"[{key}]";

    public string this[string key, params object[] arguments]
    {
        get
        {
            var template = _data.TryGetValue(key, out var v) ? v : $"[{key}]";
            return arguments.Length > 0 ? string.Format(template, arguments) : template;
        }
    }
}
