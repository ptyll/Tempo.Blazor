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
 *  - focusWithin(root, selector): focus the first element matching the selector inside `root`,
 *    without scrolling.
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

export function focusWithin(root, selector) {
  const scope = queryRoot(root);
  scope?.querySelector(selector)?.focus?.({ preventScroll: true });
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
