// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import * as RadioGroupPrimitive from "@radix-ui/react-radio-group";
import type { ComponentProps, ReactNode } from "react";
import { cn } from "@/lib/cn";

export function RadioGroup({
  className,
  ...props
}: ComponentProps<typeof RadioGroupPrimitive.Root>) {
  return (
    <RadioGroupPrimitive.Root
      className={cn("grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-2", className)}
      {...props}
    />
  );
}

export function RadioCard({
  className,
  label,
  hint,
  ...props
}: ComponentProps<typeof RadioGroupPrimitive.Item> & { label: ReactNode; hint?: ReactNode }) {
  return (
    <RadioGroupPrimitive.Item
      className={cn(
        "flex cursor-pointer flex-col items-start gap-0.5 rounded-lg border border-border p-3 text-left transition-colors outline-none",
        "hover:bg-raised focus-visible:ring-2 focus-visible:ring-ring/40",
        "data-[state=checked]:border-primary data-[state=checked]:bg-primary-surface",
        "disabled:cursor-not-allowed disabled:opacity-50",
        className,
      )}
      {...props}
    >
      <span className="font-medium">{label}</span>
      {hint && <span className="text-xs text-muted-foreground">{hint}</span>}
    </RadioGroupPrimitive.Item>
  );
}
