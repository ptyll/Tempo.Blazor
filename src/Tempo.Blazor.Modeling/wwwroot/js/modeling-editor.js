/**
 * Tempo.Blazor.Modeling — CSP-safe JS interop.
 *
 * The Modeling package deliberately ships NO eval-based interop: hosts may enforce a
 * Content-Security-Policy without `unsafe-eval`, so every dynamic behavior goes through this
 * ES module (imported by the components via IJSObjectReference — no <script> tag needed in
 * the host) or through plain Blazor `ElementReference` calls.
 *
 * Exports:
 *  - registerTreeDragBridge(): document-level dragstart/dragend listeners that expose the
 *    dragged tree element id through `dataTransfer` and `window.__tmModelingDraggedElementId`.
 *  - scrollTreeElementIntoView(elementId): reveals a tree node inside the tree scroll area.
 *  - focusTreeElement(elementId): scroll + focus a tree node (keyboard navigation).
 *  - focusSelector(selector): focus the first element matching the selector, without scrolling.
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

export function scrollTreeElementIntoView(elementId) {
  findTreeNode(elementId)?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
}

export function focusTreeElement(elementId) {
  const node = findTreeNode(elementId);
  node?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  node?.focus({ preventScroll: true });
}

export function focusSelector(selector) {
  document.querySelector(selector)?.focus?.({ preventScroll: true });
}

function findTreeNode(elementId) {
  if (typeof document === 'undefined') {
    return null;
  }

  const escapedElementId = window.CSS?.escape
    ? CSS.escape(elementId)
    : elementId.replace(/["\\]/g, '\\$&');
  return document.querySelector(`[data-testid="modeling-model-tree"] [data-element-id="${escapedElementId}"]`);
}
