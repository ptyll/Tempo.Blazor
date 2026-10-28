window.tmGantt = {
    getScrollLeft: function (el) { return el ? el.scrollLeft : 0; },
    setScrollLeft: function (el, v) { if (el) el.scrollTo({ left: v }); },
    getScrollTop: function (el) { return el ? el.scrollTop : 0; },
    setScrollTop: function (el, v) { if (el) el.scrollTop = v; },
    // Width of the element the caller owns (an ElementReference) - replaces the former eval() +
    // global querySelector, which broke under a CSP without unsafe-eval and measured the wrong
    // Gantt when several were on the page (portfolio).
    getClientWidth: function (el) { return el ? el.clientWidth : 0; }
};
