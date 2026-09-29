// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { ComponentProps } from "react";
import { cn } from "@/lib/cn";

export function Badge({
  className,
  variant = "primary",
  ...props
}: ComponentProps<"span"> & { variant?: "primary" | "neutral" }) {
  return (
    <span
      className={cn(
        "inline-flex w-fit shrink-0 items-center gap-1.5 rounded-full px-2.5 py-0.5 text-2xs font-semibold whitespace-nowrap [&_svg]:size-3",
        variant === "primary" ? "bg-primary-soft text-primary" : "bg-raised text-muted-foreground",
        className,
      )}
      {...props}
    />
  );
}
