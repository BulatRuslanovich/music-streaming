// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useT } from "@/contexts/I18nContext";
import { isLight, setTheme, useTheme } from "@/lib/theme";
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
      onClick={() => setTheme(isLight(theme) ? "dark" : "light")}
      aria-label={label}
      title={label}
    >
      {isLight(theme) ? <MoonIcon size={18} /> : <SunIcon size={18} />}
    </Button>
  );
}
