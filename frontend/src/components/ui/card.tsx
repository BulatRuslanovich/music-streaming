"use client";

import { Slot } from "@radix-ui/react-slot";
import { cva, type VariantProps } from "class-variance-authority";
import type { ComponentProps } from "react";
import { cn } from "@/lib/cn";

const surfaceVariants = cva("rounded-xl", {
  variants: {
    variant: {
      panel: "border border-border bg-card shadow-panel",
      tile: "bg-card",
      interactive:
        "border border-transparent bg-card transition-[background-color,border-color] duration-150 ease-brand hover:border-border hover:bg-raised",
    },
    padding: { none: "", sm: "p-3", md: "p-4", lg: "p-5" },
  },
  defaultVariants: { variant: "panel", padding: "md" },
});

export function Surface({
  className,
  variant,
  padding,
  asChild,
  ...props
}: ComponentProps<"div"> & VariantProps<typeof surfaceVariants> & { asChild?: boolean }) {
  const Component = asChild ? Slot : "div";

  return <Component className={cn(surfaceVariants({ variant, padding }), className)} {...props} />;
}
