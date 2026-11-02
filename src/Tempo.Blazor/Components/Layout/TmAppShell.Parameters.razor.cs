using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Layout;

// The public parameter surface of the application shell. It lives in its own partial so the markup
// file stays under the 1200-line source ceiling; the class is one type either way.
public partial class TmAppShell
{
    // ── Frame ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The visual treatment of the frame. <see cref="AppShellVariant.Inset"/> (default) floats the
    /// content as a rounded card on the workspace background; <see cref="AppShellVariant.Flat"/> fills
    /// the frame edge to edge with a hairline divider.
    /// </summary>
    [Parameter] public AppShellVariant Variant { get; set; } = AppShellVariant.Inset;

    /// <summary>
    /// The layout the shell renders. <see cref="TmLayoutMode.Auto"/> (default) measures the shell's
    /// own root, so an application embedded at any width resolves a mode from that width and never
    /// from the viewport. A forced value never measures, so a test can render a mode without a DOM.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>The layout rendered before the first measurement. Desktop unless the host says otherwise.</summary>
    [Parameter] public TmLayoutMode InitialMode { get; set; } = TmLayoutMode.Desktop;

    /// <summary>
    /// Per-instance layout thresholds for this shell. Null (default) uses the shell's own pair —
    /// mobile below 768px, desktop from 1200px, the room a frame with a docked navigation column and
    /// a work area actually needs. The stylesheet never repeats these numbers: the markup branch is
    /// what moves.
    /// </summary>
    [Parameter] public TmLayoutBreakpoints? Breakpoints { get; set; }

    /// <summary>
    /// Raised when the resolved layout changes. The initial value is not a change. Never pair
    /// <see cref="LayoutMode"/> with this callback: it reports the resolved mode, and a two-way bind
    /// would pin that mode.
    /// </summary>
    [Parameter] public EventCallback<TmLayoutMode> ResolvedLayoutChanged { get; set; }

    // ── Navigation ──────────────────────────────────────────────────────────

    /// <summary>
    /// Navigation items for the built-in sidebar (rendered by <c>TmSidebar</c> as one ungrouped
    /// list). Ignored when <see cref="Sidebar"/> or <see cref="NavSections"/> is set.
    /// </summary>
    [Parameter] public IReadOnlyList<ISidebarNavItem>? NavItems { get; set; }

    /// <summary>
    /// Named groups of navigation items for the built-in sidebar, each under its own visible heading.
    /// Takes precedence over <see cref="NavItems"/>; ignored when <see cref="Sidebar"/> is set.
    /// </summary>
    [Parameter] public IReadOnlyList<SidebarNavSection>? NavSections { get; set; }

    /// <summary>
    /// A custom navigation surface, replacing the built-in sidebar entirely. It is rendered inside
    /// the shell's navigation column on desktop, inside the overlay drawer on a tablet layout and
    /// inside the phone drawer (<see cref="MobileNav"/> = <see cref="AppShellMobileNav.Drawer"/>), so
    /// the same content is mounted at every width — a custom rail that has to survive the 3.5rem
    /// column supplies its own collapsed rendering. In <see cref="AppShellMobileNav.BottomNavigation"/>
    /// mode a phone renders the bar instead: the slot is not mounted there.
    /// <para>
    /// On a phone the frame closes the drawer when the location changes (a link of the slot navigated),
    /// never on a bare click, so the slot's own controls (an expander, a submenu trigger) keep working.
    /// A slot that wants the drawer to close without navigating calls
    /// <see cref="CloseNavigationAsync"/> itself.
    /// </para>
    /// </summary>
    [Parameter] public RenderFragment? Sidebar { get; set; }

    /// <summary>Content above the navigation list (a logo, a workspace switcher) — rendered in the docked column and in both drawers.</summary>
    [Parameter] public RenderFragment? SidebarHeader { get; set; }

    /// <summary>Content below the navigation list (the signed-in account, a version stamp) — rendered in the docked column and in both drawers, so the account stays reachable on a phone.</summary>
    [Parameter] public RenderFragment? SidebarFooter { get; set; }

    /// <summary>
    /// Whether the docked navigation column is collapsed to an icon rail. Two-way with
    /// <see cref="SidebarCollapsedChanged"/>. A tablet always renders the rail and expands the
    /// navigation into an overlay drawer instead, and a phone renders no column at all (its
    /// navigation lives in the drawer or the bottom bar), so the value only applies on a desktop
    /// layout.
    /// </summary>
    [Parameter] public bool SidebarCollapsed { get; set; }

    /// <summary>Raised when the user collapses or expands the docked navigation column.</summary>
    [Parameter] public EventCallback<bool> SidebarCollapsedChanged { get; set; }

    /// <summary>
    /// Accessible name of the navigation landmark, and the title of the tablet overlay drawer. Null
    /// (the default) uses the localized "Main navigation" — a shell that carries more than one
    /// navigation landmark must name each one after its purpose.
    /// </summary>
    [Parameter] public string? SidebarNavLabel { get; set; }

    // ── Phone presentation ──────────────────────────────────────────────────

    /// <summary>
    /// How a PHONE layout presents the navigation (a container narrower than the mobile threshold —
    /// 768 px unless <see cref="Breakpoints"/> overrides it). <see cref="AppShellMobileNav.Drawer"/>
    /// (default) puts a hamburger in the top bar that opens the full navigation in a modal drawer;
    /// <see cref="AppShellMobileNav.BottomNavigation"/> renders a <c>TmBottomNavigation</c> bar and no
    /// drawer at all. Desktop and tablet are unaffected.
    /// </summary>
    [Parameter] public AppShellMobileNav MobileNav { get; set; } = AppShellMobileNav.Drawer;

    /// <summary>
    /// Entries of the phone bottom bar, used when <see cref="MobileNav"/> is
    /// <see cref="AppShellMobileNav.BottomNavigation"/>. Null (the default) derives one entry per
    /// top-level item of the navigation model (<see cref="NavItems"/>, or every section's items in
    /// order, ignoring sub-entries) — pass this when the bar needs its own labels, icons or a subset
    /// of the destinations (a bottom bar is comfortable up to about five).
    /// </summary>
    [Parameter] public IReadOnlyList<BottomNavItem>? BottomNavItems { get; set; }

    /// <summary>
    /// Raised when the user taps an entry of the phone bottom bar, before the shell follows the
    /// entry's <see cref="BottomNavItem.Href"/>. A bar of links that never navigates is dead, so the
    /// shell always follows an <c>Href</c>; an entry without one is a command and only raises this.
    /// </summary>
    [Parameter] public EventCallback<BottomNavItem> BottomNavItemClicked { get; set; }

    /// <summary>
    /// Whether the top bar renders the navigation disclosure button that opens the phone drawer. Null
    /// (the default) decides from the layout: the button renders on a phone layout in
    /// <see cref="AppShellMobileNav.Drawer"/> mode while a navigation model exists, and never
    /// otherwise — a tablet's opener is the rail toggle, and in
    /// <see cref="AppShellMobileNav.BottomNavigation"/> mode the bar IS the navigation. Pass
    /// <c>false</c> when a custom <see cref="TopBar"/> carries its own opener, so the bar never shows
    /// two.
    /// </summary>
    [Parameter] public bool? ShowTopBarMenuButton { get; set; }

    // ── Top bar ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Custom top bar content, replacing the built-in bar. Rendered inside the shell's own
    /// <c>&lt;header&gt;</c>, so the content must not render a second header landmark (a composed
    /// <c>TmTopBar</c> passes <c>IsLandmark="false"</c>).
    /// </summary>
    [Parameter] public RenderFragment? TopBar { get; set; }

    /// <summary>Brand title of the built-in top bar (usually the application name). Without it, and without any other bar content, the shell renders no header at all.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Actions of the built-in top bar, right-aligned (a theme switch, a command-palette trigger).</summary>
    [Parameter] public RenderFragment? TopBarActions { get; set; }

    /// <summary>The signed-in user, rendered by the built-in top bar's account menu. Without it no account surface renders.</summary>
    [Parameter] public IUserInfo? User { get; set; }

    /// <summary>Host entries of the built-in top bar's account menu; the built-in Profile / Settings / Sign out lines follow them.</summary>
    [Parameter] public IReadOnlyList<UserMenuItem>? UserMenuItems { get; set; }

    /// <summary>Raised when the user picks the sign-out entry of the built-in top bar's account menu. Without a handler no sign-out line renders.</summary>
    [Parameter] public EventCallback OnLogout { get; set; }

    // ── Content ─────────────────────────────────────────────────────────────

    /// <summary>The page content, rendered inside the shell's <c>main</c> landmark.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// The id the skip link targets and the <c>main</c> element carries. Null (the default) generates
    /// a unique id per shell instance, so two shells on one page never collide. Set it when a
    /// host-side skip link or in-page anchor already points at a known id.
    /// </summary>
    [Parameter] public string? SkipLinkTarget { get; set; }

    // ── Work area ───────────────────────────────────────────────────────────

    /// <summary>
    /// The resizable work area on the right of the content (a sources panel, a tool panel). Rendered
    /// as a docked, resizable column on a desktop or tablet layout while <see cref="WorkAreaOpen"/> is
    /// true; a phone layout presents the same content as a modal <c>TmSidePanel</c> bottom sheet, so a
    /// phone renders no resize separator. Nothing renders while it is closed.
    /// </summary>
    [Parameter] public RenderFragment? WorkArea { get; set; }

    /// <summary>
    /// Whether the work area renders. Two-way with <see cref="WorkAreaOpenChanged"/>: the region
    /// renders its own close button, so a keyboard user can dismiss it without the host wiring a
    /// toggle; the host opens it (`@bind-WorkAreaOpen`) and the shell reports the close back.
    /// <para>
    /// A host that does NOT bind <see cref="WorkAreaOpenChanged"/> keeps the last word but cannot
    /// re-open a dismissed surface by leaving this <c>true</c>: a modal surface whose close is dropped
    /// would trap the user, so the shell stops presenting it until the value actually transitions
    /// (set it to <c>false</c> and then <c>true</c> again). Binding the parameter removes that
    /// round-trip.
    /// </para>
    /// </summary>
    [Parameter] public bool WorkAreaOpen { get; set; }

    /// <summary>
    /// Raised when the user closes the work area from its own close button. Without a handler the
    /// docked region renders no close button at all — a control that cannot close anything is worse
    /// than no control — so a host that renders its own toggle can leave this unset. The phone sheet is
    /// the exception: it always closes (see <see cref="WorkAreaOpen"/>), and without this handler the
    /// host can bring it back only by toggling <see cref="WorkAreaOpen"/> off and on again.
    /// </summary>
    [Parameter] public EventCallback<bool> WorkAreaOpenChanged { get; set; }

    /// <summary>
    /// Width of the work area in CSS pixels, default 420. Two-way with
    /// <see cref="WorkAreaWidthChanged"/>: a user resize (pointer drag, keyboard on the separator)
    /// raises the callback with the new width, and a persisted width is restored through it too.
    /// </summary>
    [Parameter] public int WorkAreaWidth { get; set; } = 420;

    /// <summary>Raised when the user resizes the work area, and when a persisted width is restored on load.</summary>
    [Parameter] public EventCallback<int> WorkAreaWidthChanged { get; set; }

    /// <summary>Smallest width, in CSS pixels, the work area can be resized to. Default 280. Below 1 is rejected with <see cref="ArgumentOutOfRangeException"/>.</summary>
    [Parameter] public int WorkAreaMinWidth { get; set; } = 280;

    /// <summary>
    /// Largest width, in CSS pixels, the work area can be resized to. Default 640. A value below
    /// <see cref="WorkAreaMinWidth"/> is rejected with <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    [Parameter] public int WorkAreaMaxWidth { get; set; } = 640;

    /// <summary>
    /// The content width, in CSS pixels, a resize never takes away: growing the work area stops where
    /// the content would drop below it. Default 480. A negative value is rejected with
    /// <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    [Parameter] public int MinContentWidth { get; set; } = 480;

    /// <summary>
    /// Heading of the work area region, which also names it for assistive technology. Null (the
    /// default) uses the localized "Work area" — name it after its purpose ("Sources", "Activity")
    /// when a page has more than one complementary region.
    /// </summary>
    [Parameter] public string? WorkAreaLabel { get; set; }

    /// <summary>
    /// When set, the layout the user chose persists to (and restores from) localStorage under this
    /// key: the work-area width they resized (with the host width it was resized from, so a host that
    /// changes its own default invalidates it) and the navigation collapsed state. A restore is
    /// reported through <see cref="WorkAreaWidthChanged"/>/<see cref="SidebarCollapsedChanged"/>, since
    /// it counts as a user value. Null (default) disables persistence.
    /// </summary>
    [Parameter] public string? PersistKey { get; set; }

    // ── Hosting ─────────────────────────────────────────────────────────────

    /// <summary>Extra CSS classes for the shell root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the shell root. A capture, so the host forwards its own attributes as a dictionary.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The cascaded layout of an ancestor observer, when the shell is nested in one (the same input its own observer resolves with).</summary>
    [CascadingParameter] public TmLayoutContext? Parent { get; set; }
}
