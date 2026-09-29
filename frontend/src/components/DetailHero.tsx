// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ReactNode } from "react";
import { cn } from "@/lib/cn";
import { Overline } from "./ui/label";

/** Header of an opened entity: album, artist, playlist, mix, favorites. */
export function DetailHero({
  kind,
  title,
  art,
  facts,
  description,
  actions,
  round = false,
}: {
  kind: string;
  title: string;
  art: ReactNode;
  facts?: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  round?: boolean;
}) {
  return (
    <header className="relative pt-3 pb-4 max-md:pt-1">
      <div className="relative flex flex-wrap items-end gap-8 max-md:items-start max-md:gap-4">
        <div
          className={cn(
            "grid size-70 shrink-0 place-items-center overflow-hidden rounded-lg text-faint shadow-art",
            "max-md:size-32",
            round && "rounded-full",
          )}
        >
          {art}
        </div>

        <div className="flex min-w-[min(16rem,100%)] flex-1 flex-col gap-2">
          <Overline>{kind}</Overline>
          <h1 className="text-display font-bold">{title}</h1>
          {description && (
            <p className="max-w-[62ch] text-muted-foreground max-md:text-sm">{description}</p>
          )}
          {facts && <p className="text-sm text-muted-foreground">{facts}</p>}
          {actions && <div className="mt-3 flex flex-wrap items-center gap-3">{actions}</div>}
        </div>
      </div>
    </header>
  );
}
