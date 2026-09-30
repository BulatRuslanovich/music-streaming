// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { ThemeChoice } from "@/lib/theme";
import type { Palette } from "@/lib/themeScript";

export function ThemeSwatch({ choice }: { choice: ThemeChoice }) {
  return (
    <span
      aria-hidden="true"
      className="mb-2 flex h-16 w-full overflow-hidden rounded-sm border border-border"
    >
      {choice !== "light" && <Miniature palette="dark" />}
      {choice !== "dark" && <Miniature palette="light" />}
    </span>
  );
}

function Miniature({ palette }: { palette: Palette }) {
  return (
    <span data-theme-preview={palette} className="flex min-w-0 flex-1 gap-1.5 bg-background p-1.5">
      <span className="w-1/4 rounded-xs bg-card" />
      <span className="flex min-w-0 flex-1 flex-col gap-1">
        <span className="size-5 rounded-xs bg-(--sleeve-2)" />
        <span className="h-1 w-4/5 rounded-full bg-foreground" />
        <span className="h-1 w-3/5 rounded-full bg-muted-foreground" />
        <span className="mt-auto h-1 w-1/3 rounded-full bg-primary" />
      </span>
    </span>
  );
}
