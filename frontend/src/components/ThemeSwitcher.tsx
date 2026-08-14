"use client";

import { useT } from "@/contexts/I18nContext";
import { setTheme, useTheme } from "@/lib/theme";
import { MoonIcon, SunIcon } from "./Icons";

export function ThemeSwitcher() {
  const t = useT();
  const theme = useTheme();
  const label = t("action.switchTheme");

  return (
    <button
      type="button"
      className="icon-button"
      onClick={() => setTheme(theme === "dark" ? "light" : "dark")}
      aria-label={label}
      title={label}
    >
      {theme === "dark" ? <SunIcon size={18} /> : <MoonIcon size={18} />}
    </button>
  );
}
