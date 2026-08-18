export const THEME_STORAGE_KEY = "music-streaming.theme";

export const THEME_COLORS = { dark: "#121212", light: "#faf9f6" } as const;

export const NO_FLASH_THEME_SCRIPT = `try {
  var t = localStorage.getItem("${THEME_STORAGE_KEY}");
  if (t === "light") {
    document.documentElement.dataset.theme = "light";
    var m = document.querySelector('meta[name="theme-color"]');
    if (m) m.setAttribute("content", "${THEME_COLORS.light}");
  }
} catch (e) {}`;
