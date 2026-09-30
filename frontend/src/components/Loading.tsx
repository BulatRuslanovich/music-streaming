// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { cn } from "@/lib/cn";
import { useT } from "@/contexts/I18nContext";

const sizes = {
  s: { box: "h-4 gap-0.5", bar: "w-0.5", frame: "py-1" },
  m: { box: "h-8 gap-1", bar: "w-1", frame: "py-16" },
  l: { box: "h-10 gap-1.5", bar: "w-1.5", frame: "h-dvh" },
} as const;


export function Loading({ size = "m", label }: { size?: keyof typeof sizes; label?: string }) {
  const t = useT();
  const { box, bar, frame } = sizes[size];

  return (
    <span role="status" className={cn("flex flex-col items-center justify-center gap-4", frame)}>
      <span className={cn("flex items-end", box)} aria-hidden="true">
        {[0, 1, 2, 3].map((index) => (
          <span
            key={index}
            className={cn("h-1/2 animate-equalize rounded-full bg-primary", bar)}
            style={{ animationDelay: `${-0.9 + index * 0.25}s` }}
          />
        ))}
      </span>

      {label ? (
        <span className="text-muted-foreground">{label}</span>
      ) : (
        <span className="sr-only">{t("common.loading")}</span>
      )}
    </span>
  );
}
