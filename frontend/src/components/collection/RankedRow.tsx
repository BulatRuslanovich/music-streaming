// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { Route } from "next";
import Link from "next/link";
import type { ReactNode } from "react";
import { cn } from "@/lib/cn";

/**
 * Строка пронумерованного списка: ранг, обложка, название, необязательная полоса доли и
 * цифры справа. Раньше эта геометрия существовала в двух несогласованных копиях — в
 * `RankedList` (треки, с обложками) и в топах статистики (текст без обложек), из-за чего
 * любимые исполнители на своей же странице выглядели строчками таблицы.
 *
 * Поведение остаётся за вызывающим: треки её проигрывают (`onClick`), топы статистики
 * ведут на страницу сущности (`href`).
 */
export function RankedRow<T extends string>({
  rank,
  art,
  title,
  subtitle,
  bar,
  trailing,
  current = false,
  featured = false,
  href,
  onClick,
  ariaLabel,
}: {
  rank: number;
  art: ReactNode;
  title: string;
  subtitle?: ReactNode;
  /** Доля от лидера, 0..100. Колонка появляется, только когда её передали. */
  bar?: number;
  trailing?: ReactNode;
  current?: boolean;
  /** Первое место крупнее остальных — иначе топ-1 неотличим от топ-10. */
  featured?: boolean;
  href?: Route<T>;
  onClick?: () => void;
  ariaLabel?: string;
}) {
  // Колонка обложки фиксированная, а не `auto`: каждая строка — своя сетка, и от плавающей
  // ширины названия в крупной первой строке и в обычных начинались с разных позиций.
  const artSize = featured ? "size-14 max-md:size-12" : "size-11";

  const shell = cn(
    "group grid w-full items-center gap-3 rounded-md px-2 text-left",
    featured ? "py-2.5" : "py-2",
    // Колонка полосы исчезает на узких экранах вместе со своей долей сетки.
    //
    // Правая колонка при этом фиксированная, а не `auto`. Каждая строка — своя сетка, и с
    // `auto` её ширину задавало собственное содержимое: «9 прослушиваний» и «10 прослушиваний»
    // отличаются на символ, эту разницу забирала колонка полосы, и полосы в соседних строках
    // начинались и кончались на разных местах — сравнивать их становилось нечем.
    // Обе ширины задаются в одной ветке: раньше мобильная стояла отдельной строкой ниже и
    // безусловно перекрывала ту, что выбрана здесь, — на телефоне правая колонка снова
    // становилась `auto`, и полосы разъезжались уже там.
    bar === undefined
      ? "grid-cols-[1.75rem_3.5rem_minmax(0,1fr)_auto] max-md:grid-cols-[1.75rem_3rem_minmax(0,1fr)_auto]"
      : "grid-cols-[1.75rem_3.5rem_minmax(0,1fr)_minmax(0,9rem)_7rem] max-md:grid-cols-[1.75rem_3rem_minmax(0,1fr)_7rem]",
    "transition-colors duration-150 ease-brand hover:bg-raised hover:no-underline",
  );

  const body = (
    <>
      <span
        className={cn(
          "font-semibold text-faint tabular-nums",
          featured ? "text-2xl" : "text-lg",
          current && "text-primary",
        )}
      >
        {rank}
      </span>

      <span className={cn("relative justify-self-center overflow-hidden rounded-md", artSize)}>
        {art}
      </span>

      <span className="min-w-0">
        <span
          className={cn(
            "block truncate font-semibold",
            featured && "text-xl max-md:text-base",
            current && "text-primary",
          )}
        >
          {title}
        </span>
        {subtitle && (
          <span className="block truncate text-sm text-muted-foreground">{subtitle}</span>
        )}
      </span>

      {bar !== undefined && (
        <span
          aria-hidden="true"
          className="h-2 rounded-full bg-raised max-md:hidden"
          style={{ ["--share" as string]: `${bar}%` }}
        >
          <span className="block h-full w-(--share) rounded-full bg-primary" />
        </span>
      )}

      {trailing}
    </>
  );

  if (href) {
    return (
      <Link href={href} className={shell} aria-label={ariaLabel}>
        {body}
      </Link>
    );
  }

  return (
    <button type="button" onClick={onClick} className={shell} aria-label={ariaLabel}>
      {body}
    </button>
  );
}
