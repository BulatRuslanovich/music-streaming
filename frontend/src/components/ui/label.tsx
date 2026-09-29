// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import * as LabelPrimitive from "@radix-ui/react-label";
import type { ComponentProps } from "react";
import { cn } from "@/lib/cn";

export function Label({ className, ...props }: ComponentProps<typeof LabelPrimitive.Root>) {
  return (
    <LabelPrimitive.Root
      className={cn(
        "flex items-center gap-2 text-sm leading-none font-medium text-muted-foreground select-none",
        "group-data-[disabled=true]:pointer-events-none group-data-[disabled=true]:opacity-50",
        className,
      )}
      {...props}
    />
  );
}

/** Мелкая подпись: заголовок колонки, вид сущности над названием. Без капса и разрядки. */
export function Caption({ className, ...props }: ComponentProps<"span">) {
  return <span className={cn("text-xs text-muted-foreground", className)} {...props} />;
}
