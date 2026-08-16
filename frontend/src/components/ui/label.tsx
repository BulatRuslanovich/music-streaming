"use client";

import * as LabelPrimitive from "@radix-ui/react-label";
import type { ComponentProps } from "react";
import { cn } from "@/lib/cn";

export function Label({ className, ...props }: ComponentProps<typeof LabelPrimitive.Root>) {
  return (
    <LabelPrimitive.Root
      className={cn(
        "flex items-center gap-2 text-sm leading-none font-semibold text-muted-foreground select-none",
        "group-data-[disabled=true]:pointer-events-none group-data-[disabled=true]:opacity-50",
        className,
      )}
      {...props}
    />
  );
}

/**
 * Мелкая заглавная подпись — рецепт, который прежде был переписан шесть раз:
 * .detail-kind, .track-head, .nav-heading, .stat-tile-label, .admin-row-head, .menu-label.
 */
export function Overline({ className, ...props }: ComponentProps<"span">) {
  return (
    <span
      className={cn("text-2xs font-bold tracking-[0.08em] text-faint uppercase", className)}
      {...props}
    />
  );
}
