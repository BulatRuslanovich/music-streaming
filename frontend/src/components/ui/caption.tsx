// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { ComponentProps } from "react";
import { cn } from "@/lib/cn";

/** Мелкая подпись: заголовок колонки, вид сущности над названием. Без капса и разрядки. */
export function Caption({ className, ...props }: ComponentProps<"span">) {
  return <span className={cn("text-xs text-muted-foreground", className)} {...props} />;
}
