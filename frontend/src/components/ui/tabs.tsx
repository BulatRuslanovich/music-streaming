// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { cva, type VariantProps } from "class-variance-authority";
import type { ComponentProps } from "react";
import { cn } from "@/lib/cn";

const listVariants = cva("flex items-center", {
  variants: {
    variant: {
      pill: "flex-wrap gap-2",
      underline: "gap-1 overflow-x-auto border-b border-border [scrollbar-width:none]",
    },
  },
  defaultVariants: { variant: "pill" },
});

const triggerVariants = cva(
  "inline-flex shrink-0 items-center gap-2 font-semibold whitespace-nowrap transition-colors duration-150 ease-brand outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:pointer-events-none disabled:opacity-50",
  {
    variants: {
      variant: {
        pill: "rounded-full bg-raised px-4 py-2 text-sm text-muted-foreground hover:bg-accent hover:text-foreground data-[state=active]:bg-foreground data-[state=active]:text-canvas",
        underline:
          "-mb-px border-b-2 border-transparent px-4 py-2.5 text-sm text-muted-foreground hover:text-foreground data-[state=active]:border-primary data-[state=active]:text-primary",
      },
    },
    defaultVariants: { variant: "pill" },
  },
);

export function ToggleGroupButton({
  className,
  variant,
  active,
  ...props
}: ComponentProps<"button"> & VariantProps<typeof triggerVariants> & { active: boolean }) {
  return (
    <button
      type="button"
      aria-pressed={active}
      data-state={active ? "active" : "inactive"}
      className={cn(triggerVariants({ variant }), className)}
      {...props}
    />
  );
}

export function ToggleGroup({
  className,
  variant,
  ...props
}: ComponentProps<"div"> & VariantProps<typeof listVariants>) {
  return <div role="group" className={cn(listVariants({ variant }), className)} {...props} />;
}
