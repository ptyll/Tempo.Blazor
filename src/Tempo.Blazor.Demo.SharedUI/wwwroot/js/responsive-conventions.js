// Measures the demo hosts so each heading can name the mode its own container resolves to.
// The page owns the text; this script only reads widths.
window.tmResponsiveConventions = {
    measure() {
        const result = {};
        for (const host of document.querySelectorAll("[data-testid^='rc-host-']:not([data-testid$='-heading'])")) {
            const grid = host.querySelector(".tm-dashboard-grid-container");
            result[host.getAttribute("data-testid")] = grid ? Math.round(grid.clientWidth) : 0;
        }
        return result;
    },

    // Reports once every host has a width. The dashboards render after the page, so the
    // page cannot measure them on its first frame.
    watch(dotNet) {
        const tick = () => {
            const measured = this.measure();
            const widths = Object.values(measured);
            if (widths.length > 0 && widths.every(width => width > 0)) {
                dotNet.invokeMethodAsync("OnMeasured", measured).catch(() => {});
                return;
            }
            setTimeout(tick, 200);
        };
        tick();
    },
};

