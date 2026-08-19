// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import * as ProgressPrimitive from "@radix-ui/react-progress";
import type { ComponentProps } from "react";
import { cn } from "@/lib/cn";

export function Progress({
  className,
  value,
  children,
  ...props
}: ComponentProps<typeof ProgressPrimitive.Root>) {
  return (
    <ProgressPrimitive.Root
      value={value}
      className={cn("relative h-9 w-full overflow-hidden rounded-full bg-raised", className)}
      {...props}
    >
      <ProgressPrimitive.Indicator
        className="h-full bg-primary transition-[width] duration-200 ease-linear"
        style={{ width: `${value ?? 0}%` }}
      />
      {children && (
        <span className="absolute inset-0 grid place-items-center text-sm font-semibold">
          {children}
        </span>
      )}
    </ProgressPrimitive.Root>
  );
}
