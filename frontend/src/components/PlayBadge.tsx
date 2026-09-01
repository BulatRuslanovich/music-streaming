// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { cn } from "@/lib/cn";
import { PauseIcon, PlayIcon } from "./Icons";

export function PlayBadge({
  playing,
  visible = false,
  standalone = false,
  size = 9,
  iconSize,
  className,
}: {
  playing: boolean;
  visible?: boolean;
  /**
   * Значок — самостоятельная кнопка, а не украшение поверх нажимаемой карточки. Такие
   * остаются видимыми там, где нет наведения: спрятать их — значит убрать единственный
   * способ запустить альбом прямо с полки.
   */
  standalone?: boolean;
  size?: 8 | 9;
  iconSize?: number;
  className?: string;
}) {
  const icon = iconSize ?? (size === 8 ? 16 : 18);

  return (
    <span
      aria-hidden="true"
      className={cn(
        // Значок — действие («играй это»), поэтому `--action`, а не цвет состояния.
        "grid shrink-0 place-items-center rounded-full bg-action text-action-foreground shadow-art",
        size === 8 ? "size-8" : "size-9",
        "translate-y-1 opacity-0 transition-[opacity,transform] duration-150 ease-brand",
        "group-hover:translate-y-0 group-hover:opacity-100",
        "group-focus-visible:translate-y-0 group-focus-visible:opacity-100",
        // Условие по наличию наведения, а не по ширине: планшет в альбомной шире 900px, но
        // мыши на нём нет — и до этого кнопка запуска там просто не появлялась никогда.
        //
        // Значок при этом остаётся только у самостоятельных кнопок. Украшение поверх
        // нажимаемой карточки молчит: иначе получалась страница, где каждая обложка кричит
        // «играй меня», и играющий трек в этом хоре ничем не выделялся.
        standalone && "[@media(hover:none)]:translate-y-0 [@media(hover:none)]:opacity-100",
        "group-active:scale-95",
        visible && "translate-y-0 opacity-100",
        className,
      )}
    >
      {playing ? <PauseIcon size={icon} /> : <PlayIcon size={icon} />}
    </span>
  );
}
