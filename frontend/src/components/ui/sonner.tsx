// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { Toaster as Sonner } from "sonner";
import { isLight, useTheme } from "@/lib/theme";

export function Toaster() {
  const theme = isLight(useTheme()) ? "light" : "dark";

  return (
    <Sonner
      theme={theme}
      position="top-right"
      offset={18}
      mobileOffset={{ top: "max(0.75rem, env(safe-area-inset-top))", left: 12, right: 12 }}
      toastOptions={{
        unstyled: true,
        classNames: {
          toast:
            "flex w-full items-start gap-2.5 rounded-xl border border-border-strong bg-popover/95 p-3 pl-4 text-sm text-popover-foreground shadow-pop backdrop-blur-md",
          title: "min-w-0 flex-1 leading-snug",
          success: "border-success/50",
          error: "border-destructive/55 text-destructive",
          closeButton:
            "absolute -top-2 -left-2 grid size-5 place-items-center rounded-full border border-border-strong bg-popover text-muted-foreground hover:text-foreground",
        },
      }}
      closeButton
    />
  );
}
