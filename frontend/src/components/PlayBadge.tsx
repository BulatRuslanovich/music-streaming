// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { cn } from "@/lib/cn";
import { PauseIcon, PlayIcon } from "./Icons";

export function PlayBadge({
  playing,
  visible = false,
  size = 9,
  iconSize,
  className,
}: {
  playing: boolean;
  visible?: boolean;
  size?: 8 | 9;
  iconSize?: number;
  className?: string;
}) {
  const icon = iconSize ?? (size === 8 ? 16 : 18);

  return (
    <span
      aria-hidden="true"
      className={cn(
        "grid shrink-0 place-items-center rounded-full bg-primary text-primary-foreground shadow-art",
        size === 8 ? "size-8" : "size-9",
        "translate-y-1 opacity-0 transition-[opacity,transform] duration-150 ease-brand",
        "group-hover:translate-y-0 group-hover:opacity-100",
        "group-focus-visible:translate-y-0 group-focus-visible:opacity-100",
        "max-md:translate-y-0 max-md:opacity-100",
        "group-active:scale-95",
        visible && "translate-y-0 opacity-100",
        className,
      )}
    >
      {playing ? <PauseIcon size={icon} /> : <PlayIcon size={icon} />}
    </span>
  );
}
