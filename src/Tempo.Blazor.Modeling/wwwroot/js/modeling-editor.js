/**
 * Tempo.Blazor.Modeling — CSP-safe JS interop.
 *
 * The Modeling package deliberately ships NO eval-based interop: hosts may enforce a
 * Content-Security-Policy without `unsafe-eval`, so every dynamic behavior goes through this
 * ES module (imported by the components via IJSObjectReference — no <script> tag needed in
 * the host) or through plain Blazor `ElementReference` calls.
 *
 * The DOM-scoped exports take the caller's own root ELEMENT (a Blazor ElementReference) as
 * their first argument so a query can never reach outside the component instance that owns it.
 *
 * Exports:
 *  - registerTreeDragBridge(): document-level dragstart/dragend listeners that expose the
 *    dragged tree element id through `dataTransfer` and `window.__tmModelingDraggedElementId`.
 *  - scrollTreeElementIntoView(treeRoot, elementId): reveals a tree node inside the tree
 *    scroll area, querying inside `treeRoot` only.
 *  - focusTreeElement(treeRoot, elementId): scroll + focus a tree node (keyboard navigation),
 *    querying inside `treeRoot` only.
 *  - focusTreeGroupHeader(treeRoot, groupKey): scroll + focus a tree GROUP treeitem (the APG
 *    `role="treeitem"` wrapper carrying `data-group-key`), same scoping as focusTreeElement.
 *  - focusWithin(root, selector): focus the first element matching the selector inside `root`,
 *    without scrolling. `selector` may be a comma-separated priority list — parts are tried in
 *    order and the first part with a match wins (used by the mobile focus return: the actual
 *    trigger first, the host-div proxy as the fallback).
 *  - isFocusWithin(root, selector): whether the document's active element is inside `root` and
 *    matches `selector` (the layout-flip focus probe of TmModelingEditor).
 */

let dragBridgeRegistered = false;

export function registerTreeDragBridge() {
  if (dragBridgeRegistered || typeof document === 'undefined') {
    return;
  }

  dragBridgeRegistered = true;

  document.addEventListener('dragstart', event => {
    const node = event.target?.closest?.('[data-modeling-drag-element-id]');
    if (!node || !event.dataTransfer) {
      return;
    }

    const elementId = node.getAttribute('data-modeling-drag-element-id') || '';
    window.__tmModelingDraggedElementId = elementId;
    event.dataTransfer.effectAllowed = 'copy';
    event.dataTransfer.setData('text/plain', elementId);
    event.dataTransfer.setData('application/x-tempo-modeling-element-id', elementId);
  }, true);

  document.addEventListener('dragend', () => {
    window.__tmModelingDraggedElementId = '';
  }, true);
}

export function scrollTreeElementIntoView(treeRoot, elementId) {
  findTreeNode(treeRoot, elementId)?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
}

export function focusTreeElement(treeRoot, elementId) {
  const node = findTreeNode(treeRoot, elementId);
  node?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  node?.focus({ preventScroll: true });
}

export function focusTreeGroupHeader(treeRoot, groupKey) {
  const root = queryRoot(treeRoot);
  if (!root) {
    return;
  }

  const escapedGroupKey = window.CSS?.escape
    ? CSS.escape(groupKey)
    : groupKey.replace(/["\\]/g, '\\$&');
  const header = root.querySelector(`[data-group-key="${escapedGroupKey}"]`);
  header?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  header?.focus({ preventScroll: true });
}

export function focusWithin(root, selector) {
  const scope = queryRoot(root);
  if (!scope) {
    return;
  }

  // The selector may be a comma-separated PRIORITY list (the mobile focus return asks for the
  // actual trigger first and the host-div proxy second). Parts are tried in order — the first
  // PART with a match wins, not the first match in document order. Single-part selectors
  // (every other caller) behave exactly as before.
  for (const part of selector.split(',')) {
    const trimmed = part.trim();
    if (!trimmed) {
      continue;
    }

    const target = scope.querySelector(trimmed);
    if (target) {
      target.focus({ preventScroll: true });
      return;
    }
  }
}

/**
 * Whether the DOCUMENT's active element sits inside `root` and matches `selector` — the
 * flip-time focus probe. A tablet→mobile flip re-presents the open "Open in editor" dialog
 * (the docked HeaderActions close unmounts, the mobile icon close takes over), so the editor
 * asks BEFORE the flip re-render whether the close owned the focus and re-arms its close-focus
 * flag for the next render. `selector` may be a comma-separated list; any part matching counts.
 */
export function isFocusWithin(root, selector) {
  const scope = queryRoot(root);
  const active = typeof document === 'undefined' ? null : document.activeElement;
  if (!scope || !active || typeof active.matches !== 'function' || !scope.contains(active)) {
    return false;
  }

  return String(selector || '').split(',').some(part => {
    const trimmed = part.trim();
    return trimmed && active.matches(trimmed);
  });
}

/**
 * A Blazor ElementReference arrives as the element itself; anything without querySelector
 * (default reference, prerender placeholder) simply cannot be scoped and is a no-op.
 */
function queryRoot(root) {
  return root && typeof root.querySelector === 'function' ? root : null;
}

function findTreeNode(treeRoot, elementId) {
  const root = queryRoot(treeRoot);
  if (!root) {
    return null;
  }

  const escapedElementId = window.CSS?.escape
    ? CSS.escape(elementId)
    : elementId.replace(/["\\]/g, '\\$&');
  return root.querySelector(`[data-element-id="${escapedElementId}"]`);
}
