// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ComponentProps } from "react";
import { cn } from "@/lib/cn";

export function Input({ className, type, ...props }: ComponentProps<"input">) {
  return (
    <input
      type={type}
      className={cn(
        "flex h-10 w-full rounded-lg border border-transparent bg-raised px-3 py-2 text-base transition-colors outline-none",
        "placeholder:text-faint",
        "focus-visible:border-ring focus-visible:ring-2 focus-visible:ring-ring/25",
        "aria-invalid:border-destructive aria-invalid:ring-destructive/25",
        "disabled:cursor-not-allowed disabled:opacity-50",
        "file:hidden [&::-webkit-search-cancel-button]:hidden",
        className,
      )}
      {...props}
    />
  );
}
