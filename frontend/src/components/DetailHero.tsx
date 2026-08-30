// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ReactNode } from "react";
import { cn } from "@/lib/cn";
import { Overline } from "./ui/label";

/**
 * Шапка открытой сущности: альбом, артист, плейлист, микс, избранное.
 *
 * Раньше это была скруглённая коробка с подкрашенным градиентом внутри — то есть виджет,
 * лежащий на тёмной странице, а не шапка самой страницы. Три вещи изменились:
 *
 * 1. Цвет обложки выходит из отступов контентной панели и доходит до её краёв, а снизу
 *    растворяется в трек-листе, вместо того чтобы обрываться на границе блока.
 * 2. Арт крупнее и получает `--elevation-hero`: на 208px прежняя тень
 *    (`0 4px 12px / 30%`) не была видна вовсе, и обложка выглядела наклеенной.
 * 3. `data-hero` гасит `TintScrim` — подложку, красящую верх страницы цветом *играющего*
 *    трека. Два цветовых поля от двух разных источников в одних пятистах пикселях давали
 *    муть; на странице с шапкой источник цвета остаётся один — то, что открыто.
 */
export function DetailHero({
  kind,
  title,
  art,
  facts,
  description,
  actions,
  tint,
  round = false,
}: {
  kind: string;
  title: string;
  art: ReactNode;
  facts?: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  tint?: string | null;
  round?: boolean;
}) {
  return (
    <header
      data-hero="true"
      style={{ ["--art-tint" as string]: tint ?? "" }}
      className={cn(
        // Отрицательные поля повторяют отступы `main`: шапка — единственный блок, которому
        // положено доходить до кромки панели.
        "relative -mx-8 -mt-7 px-8 pt-10 pb-4",
        "max-md:-mx-4 max-md:-mt-5 max-md:px-4 max-md:pt-6",
        "[transition:--art-tint_700ms_var(--ease)]",
      )}
    >
      {/*
        Заливка уходит на 8rem ниже шапки: маска гасит её уже внутри трек-листа, поэтому
        цвет обложки перетекает в содержимое, а не заканчивается ровной чертой. Зерно —
        тот же класс, что и у остальных подложек: тёмный градиент на почти чёрном холсте
        на 8-битной панели разваливается на ступеньки, и шум их разбивает.
      */}
      <span
        aria-hidden="true"
        className={cn(
          "grain pointer-events-none absolute inset-x-0 -bottom-32 top-0 overflow-hidden",
          "bg-[linear-gradient(180deg,color-mix(in_srgb,var(--tint-art)_var(--veil-hero),transparent),transparent_85%)]",
          "[mask-image:linear-gradient(to_bottom,#000_60%,transparent)]",
        )}
      />

      <div className="relative flex flex-wrap items-end gap-8 max-md:items-start max-md:gap-4">
        <div
          className={cn(
            "grid size-70 shrink-0 place-items-center overflow-hidden rounded-lg text-faint shadow-hero",
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
