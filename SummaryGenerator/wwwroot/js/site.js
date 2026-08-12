// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(() => {
    const themeStorageKey = "theme";

    function getTheme() {
        const savedTheme = localStorage.getItem(themeStorageKey);
        return savedTheme === "light" ? "light" : "dark";
    }

    function applyTheme(theme) {
        document.documentElement.setAttribute("data-bs-theme", theme);
        localStorage.setItem(themeStorageKey, theme);

        const toggleButton = document.getElementById("theme-toggle");
        if (toggleButton) {
            toggleButton.textContent = theme === "dark" ? "Light mode" : "Dark mode";
            toggleButton.setAttribute("aria-pressed", theme === "dark" ? "true" : "false");
        }
    }

    document.addEventListener("DOMContentLoaded", () => {
        applyTheme(getTheme());

        const toggleButton = document.getElementById("theme-toggle");
        if (!toggleButton) {
            return;
        }

        toggleButton.addEventListener("click", () => {
            const nextTheme = getTheme() === "dark" ? "light" : "dark";
            applyTheme(nextTheme);
        });
    });
})();
