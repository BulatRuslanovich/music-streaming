// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ReactNode } from "react";
import { cn } from "@/lib/cn";
import { Caption } from "./ui/label";

/**
 * Header of an opened entity: album, artist, playlist, mix, favorites.
 *
 * `facts` — отдельные факты (исполнитель, год, число треков); разводит их отступ, а не
 * разделитель-точка между ними.
 */
export function DetailHeader({
  kind,
  title,
  art,
  facts = [],
  description,
  actions,
  round = false,
}: {
  kind: string;
  title: string;
  art: ReactNode;
  facts?: ReactNode[];
  description?: ReactNode;
  actions?: ReactNode;
  round?: boolean;
}) {
  const shown = facts.filter((fact) => fact !== null && fact !== undefined && fact !== false);

  return (
    <header className="grid grid-cols-[auto_minmax(0,1fr)] items-end gap-8 pt-2 max-md:grid-cols-1 max-md:gap-5">
      <div
        className={cn(
          "grid size-60 shrink-0 place-items-center overflow-hidden rounded-xs bg-accent text-faint shadow-art max-md:size-44",
          round && "rounded-full shadow-none",
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
