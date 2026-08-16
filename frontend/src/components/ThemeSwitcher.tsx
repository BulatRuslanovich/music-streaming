"use client";

import { useT } from "@/contexts/I18nContext";
import { setTheme, useTheme } from "@/lib/theme";
import { MoonIcon, SunIcon } from "./Icons";
import { Button } from "./ui/button";

export function ThemeSwitcher() {
  const t = useT();
  const theme = useTheme();
  const label = t("action.switchTheme");

  return (
    <Button
      variant="ghost"
      size="icon"
      onClick={() => setTheme(theme === "dark" ? "light" : "dark")}
      aria-label={label}
      title={label}
    >
      {theme === "dark" ? <SunIcon size={18} /> : <MoonIcon size={18} />}
    </Button>
  );
}
