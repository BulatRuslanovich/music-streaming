// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useState } from "react";
import { cn } from "@/lib/cn";

const SHELL = "pointer-events-none absolute inset-x-0 top-0 overflow-hidden grain";

/**
 * Фон из самой обложки: та же картинка, растянутая и заглушенная блюром. Цвет не
 * вычисляется — все переходы внутри арта сохраняются как есть, поэтому фон читается
 * как «этот альбом», а не как «зелёное пятно».
 *
 * Блюр на большом элементе дорогой, поэтому только там, где ничего не прокручивается.
 */
export function CoverBackdrop({
  source,
  className,
}: {
  source: string | null;
  className?: string;
}) {
  const [current, setCurrent] = useState(source);
  const [previous, setPrevious] = useState<string | null>(null);
  const [loaded, setLoaded] = useState(false);

  if (current !== source) {
    // Прошлый арт остаётся снизу, пока новый грузится, — иначе на смене трека
    // фон проваливается в пустоту и возвращается рывком.
    setPrevious(loaded ? current : previous);
    setCurrent(source);
    setLoaded(false);
  }

  const layer = "absolute inset-0 size-full scale-[1.4] object-cover blur-[64px] saturate-150";

  return (
    <span aria-hidden="true" className={cn(SHELL, "h-full opacity-60", className)}>
      {previous && <img src={previous} alt="" className={layer} />}

      {current && (
        <img
          key={current}
          src={current}
          alt=""
          onLoad={() => setLoaded(true)}
          className={cn(
            layer,
            "[transition:opacity_700ms_var(--ease)]",
            loaded ? "opacity-100" : "opacity-0",
          )}
        />
      )}
    </span>
  );
}

/**
 * Спокойный вариант для прокручиваемых страниц: диагональ из двух полюсов обложки,
 * без движения и без блюра. Оба цвета зарегистрированы через `@property`, поэтому
 * смена трека — переход, а не скачок; пока ничего не играет, они прозрачны.
 *
 * Это не то же самое, что `--art-tint` в DetailHeader: тот красит шапку открытой
 * страницы. Приложение окрашивается тем, что играет; страница — тем, что открыто.
 */
export function TintScrim({ className }: { className?: string }) {
  return (
    <span
      aria-hidden="true"
      className={cn(
        SHELL,
        "h-[55vh] opacity-30",
        "[transition:--cover-tint_700ms_var(--ease),--cover-tint-2_700ms_var(--ease)]",
        "bg-[linear-gradient(155deg,var(--cover-tint),var(--cover-tint-2)_45%,transparent_72%)]",
        "[mask-image:linear-gradient(to_bottom,#000,transparent)]",
        className,
      )}
    />
  );
}
