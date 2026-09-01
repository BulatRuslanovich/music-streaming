// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ReactNode } from "react";
import { cn } from "@/lib/cn";

/**
 * Пустые экраны — это то, что новый слушатель видит первым: поиск без запроса, пустое
 * избранное, пустая очередь. Раньше все они были серым прямоугольником с одной строкой
 * по левому краю, и выглядели как заглушка, а не как часть продукта. Композиция теперь
 * та же, что у StatusPage: иконка в круге, заголовок, пояснение, действие — по центру.
 *
 * `bare` — для мест, где карточка уже есть снаружи (очередь, панель с текстом песни).
 */
export function EmptyState({
  icon,
  title,
  description,
  action,
  bare = false,
  className,
}: {
  icon?: ReactNode;
  title: string;
  description?: string;
  action?: ReactNode;
  bare?: boolean;
  className?: string;
}) {
  return (
    <div
      className={cn(
        "flex flex-col items-center gap-2 px-6 py-10 text-center",
        !bare && "rounded-xl bg-card",
        className,
      )}
    >
      {icon && (
        <span
          aria-hidden="true"
          className="mb-1 grid size-14 place-items-center rounded-full bg-raised text-faint"
        >
          {icon}
        </span>
      )}
      <h3 className="text-section font-semibold">{title}</h3>
      {description && <p className="max-w-md text-muted-foreground">{description}</p>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  );
}
