// Applies the demo's colour theme before Blazor starts, so the first paint is already indigo
// when that is the stored choice. The switch calls the same function afterwards.
window.tmColorTheme = {
    storageKey: 'tm-demo-color-theme',

    apply: function (theme) {
        var root = document.documentElement;
        if (theme === 'indigo') {
            root.setAttribute('data-tm-theme', 'indigo');
        } else {
            root.removeAttribute('data-tm-theme');
        }
        try {
            localStorage.setItem(this.storageKey, theme);
        } catch (e) {
            // A blocked localStorage must not stop the theme from applying.
        }
    },

    stored: function () {
        try {
            return localStorage.getItem(this.storageKey);
        } catch (e) {
            return null;
        }
    }
};
