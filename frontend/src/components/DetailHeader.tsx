// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ReactNode } from "react";
import { cn } from "@/lib/cn";
import { Caption } from "./ui/caption";

export function DetailHeader({
  kind,
  title,
  art,
  facts = [],
  description,
  actions,
  round = false,
  record = false,
}: {
  kind: string;
  title: string;
  art: ReactNode;
  facts?: ReactNode[];
  description?: ReactNode;
  actions?: ReactNode;
  round?: boolean;
  record?: boolean;
}) {
  const shown = facts.filter((fact) => fact !== null && fact !== undefined && fact !== false);

  return (
    <header className="grid grid-cols-[auto_minmax(0,1fr)] items-end gap-8 pt-2 [--art:clamp(15rem,20vw,18rem)] max-md:grid-cols-1 max-md:gap-5 max-md:[--art:14rem]">
      <div
        className={cn(
          "grid size-(--art) shrink-0 place-items-center overflow-hidden rounded-xs bg-accent text-faint shadow-art",
          round && "rounded-full shadow-none",
          record && "mr-[calc(var(--art)*0.3)] overflow-visible bg-transparent shadow-none",
        )}
      >
        {art}
      </div>

      <div className="flex min-w-0 flex-col gap-3">
        <Caption>{kind}</Caption>
        <h1 className="font-display text-display text-balance">{title}</h1>
        {description && (
          <p className="max-w-[62ch] text-muted-foreground max-md:text-sm">{description}</p>
        )}
        {shown.length > 0 && (
          <p className="flex flex-wrap gap-x-5 gap-y-1 text-sm text-muted-foreground">
            {shown.map((fact, index) => (
              <span key={index}>{fact}</span>
            ))}
          </p>
        )}
        {actions && <div className="mt-2 flex flex-wrap items-center gap-2.5">{actions}</div>}
      </div>
    </header>
  );
}
