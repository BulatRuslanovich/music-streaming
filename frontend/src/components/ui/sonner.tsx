// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { Toaster as Sonner } from "sonner";
import { useTheme } from "@/lib/theme";

export function Toaster() {
  const theme = useTheme();

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
            "flex w-full items-start gap-2.5 rounded-lg border border-transparent bg-popover p-3 pl-4 text-sm text-popover-foreground shadow-pop",
          title: "min-w-0 flex-1 leading-snug",
          success: "border-success/50",
          error: "border-destructive/55 text-destructive",
          closeButton:
            "order-last -my-0.5 -mr-1 grid size-6 shrink-0 place-items-center rounded-md border-0 transition-colors [--normal-bg:transparent] [--normal-text:var(--muted-foreground)] hover:[--normal-bg:var(--surface-raised)] hover:[--normal-text:var(--foreground)] [&_svg]:size-3.5",
        },
      }}
      closeButton
    />
  );
}
