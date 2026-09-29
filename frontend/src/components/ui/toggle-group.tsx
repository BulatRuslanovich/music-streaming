// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { ComponentProps } from "react";
import { cn } from "@/lib/cn";

export function ToggleGroup({ className, ...props }: ComponentProps<"div">) {
  return (
    <div role="group" className={cn("flex flex-wrap items-center gap-2", className)} {...props} />
  );
}

/** Выбор нейтрален, как и в навигации: это «ты здесь», а не «это звучит». */
export function ToggleGroupButton({
  className,
  active,
  ...props
}: ComponentProps<"button"> & { active: boolean }) {
  return (
    <button
      type="button"
      aria-pressed={active}
      className={cn(
        "inline-flex shrink-0 items-center gap-2 rounded-full bg-raised px-4 py-2 text-sm font-medium whitespace-nowrap text-muted-foreground transition-colors duration-150 ease-brand outline-none hover:bg-accent hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:pointer-events-none disabled:opacity-50",
        active && "bg-foreground text-background hover:bg-foreground hover:text-background",
        className,
      )}
      {...props}
    />
  );
}
