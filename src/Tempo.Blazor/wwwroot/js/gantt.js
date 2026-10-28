window.tmGantt = {
    getScrollLeft: function (el) { return el ? el.scrollLeft : 0; },
    setScrollLeft: function (el, v) { if (el) el.scrollTo({ left: v }); },
    getScrollTop: function (el) { return el ? el.scrollTop : 0; },
    setScrollTop: function (el, v) { if (el) el.scrollTop = v; },
    // Width of the element the caller owns (an ElementReference) - replaces the former eval() +
    // global querySelector, which broke under a CSP without unsafe-eval and measured the wrong
    // Gantt when several were on the page (portfolio).
    getClientWidth: function (el) { return el ? el.clientWidth : 0; },

    // Docked workload panel: keeps its horizontal scroll and the timeline's in step. Both axes show the same
    // date range, so the position is mapped as a RATIO of the scrollable distance (the widths differ). Runs
    // entirely in the browser - a Blazor Server circuit must not pay a round trip per scroll event. The peer is
    // found from the workload element itself (closest .tm-gantt), so several Gantts on a page never cross.
    // Idempotent and safe to call on every render: returns true while the workload is linked to the CURRENT
    // timeline (an existing link to that same element is kept; a link to a replaced element is released and
    // remade), false when there is nothing to link to.
    linkScroll: function (workload) {
        if (!workload) return false;
        const root = workload.closest('.tm-gantt');
        const timeline = root ? root.querySelector('.tm-gantt__timeline') : null;
        const existing = workload.__tmGanttLink;
        if (existing && timeline && existing.timeline === timeline) return true;
        window.tmGantt.unlinkScroll(workload);
        if (!timeline) return false;

        // The value a programmatic write produced: the scroll event it causes must not drive the peer back.
        const written = new WeakMap();
        const ratio = function (el) {
            const max = el.scrollWidth - el.clientWidth;
            return max > 0 ? el.scrollLeft / max : 0;
        };
        const follow = function (source, target) {
            return function () {
                if (written.get(source) === source.scrollLeft) {
                    written.delete(source);
                    return;
                }
                const max = target.scrollWidth - target.clientWidth;
                target.scrollLeft = ratio(source) * Math.max(max, 0);
                written.set(target, target.scrollLeft);
            };
        };

        const toTimeline = follow(workload, timeline);
        const toWorkload = follow(timeline, workload);
        workload.addEventListener('scroll', toTimeline, { passive: true });
        timeline.addEventListener('scroll', toWorkload, { passive: true });
        workload.__tmGanttLink = { timeline: timeline, toTimeline: toTimeline, toWorkload: toWorkload };
        toWorkload();
        return true;
    },

    unlinkScroll: function (workload) {
        const link = workload && workload.__tmGanttLink;
        if (!link) return;
        workload.removeEventListener('scroll', link.toTimeline);
        link.timeline.removeEventListener('scroll', link.toWorkload);
        delete workload.__tmGanttLink;
    },

    // History drawer focus return: the drawer notes the element that had focus when it opened (the History button
    // of the toolbar) and hands focus back to it when it closes. The note lives on the drawer element, so several
    // Gantts never share one; a body / in-drawer / detached opener is ignored (nothing sensible to return to).
    noteOpener: function (drawer) {
        if (!drawer) return;
        const doc = drawer.ownerDocument;
        const active = doc ? doc.activeElement : null;
        if (!active || active === doc.body || drawer.contains(active)) {
            delete drawer.__tmGanttOpener;
            return;
        }
        drawer.__tmGanttOpener = active;
    },

    restoreOpener: function (drawer) {
        if (!drawer) return false;
        const opener = drawer.__tmGanttOpener;
        delete drawer.__tmGanttOpener;
        if (!opener || opener.isConnected === false || typeof opener.focus !== 'function') return false;
        opener.focus();
        return true;
    }
};