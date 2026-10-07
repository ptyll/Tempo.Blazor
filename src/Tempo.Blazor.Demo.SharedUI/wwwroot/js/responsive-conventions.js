// Measures the demo hosts so each heading can name the mode its own container resolves to.
// The page owns the text; this script only reads widths.
let observers = [];

window.tmResponsiveConventions = {
    measure() {
        const result = {};
        for (const host of document.querySelectorAll("[data-testid^='rc-host-']:not([data-testid$='-heading'])")) {
            const grid = host.querySelector(".tm-dashboard-grid-container");
            result[host.getAttribute("data-testid")] = grid ? Math.round(grid.clientWidth) : 0;
        }
        return result;
    },

    // A ResizeObserver per grid, so a forced mode or a resize updates the heading. The dashboards
    // render after the page, so the observers attach once every grid is in the document.
    watch(dotNet) {
        const attach = () => {
            const grids = document.querySelectorAll("[data-testid^='rc-host-']:not([data-testid$='-heading']) .tm-dashboard-grid-container");
            if (grids.length < 4) {
                setTimeout(attach, 200);
                return;
            }
            this.stop();
            const report = () => dotNet.invokeMethodAsync("OnMeasured", this.measure()).catch(() => {});
            for (const grid of grids) {
                const observer = new ResizeObserver(report);
                observer.observe(grid);
                observers.push(observer);
            }
            report();
        };
        attach();
    },

    stop() {
        for (const observer of observers) observer.disconnect();
        observers = [];
    },
};

