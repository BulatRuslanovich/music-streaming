// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ReactNode } from "react";
import { cn } from "@/lib/cn";

export function FileRow({
  name,
  muted = false,
  tone = "neutral",
  status,
  meta,
  action,
}: {
  name: string;
  muted?: boolean;
  tone?: "neutral" | "destructive";
  status?: ReactNode;
  meta?: ReactNode;
  action?: ReactNode;
}) {
  return (
    <li
      className={cn(
        "flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm",
        "max-[620px]:flex-wrap max-[620px]:gap-y-1.5",
        tone === "destructive"
          ? "border border-destructive/40 bg-destructive/10"
          : "bg-card shadow-panel",
      )}
    >
      <span
        className={cn(
          "min-w-28 flex-1 truncate font-medium max-[620px]:flex-[1_0_100%]",
          muted && "text-muted-foreground line-through",
        )}
      >
        {name}
      </span>

      {status}
      {meta && <span className="shrink-0 text-muted-foreground tabular-nums">{meta}</span>}
      {action}
    </li>
  );
}

export function FileList({ children }: { children: ReactNode }) {
  return <ul className="flex flex-col gap-1.5">{children}</ul>;
}
