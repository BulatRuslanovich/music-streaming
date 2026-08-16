"use client";

import { Slot } from "@radix-ui/react-slot";
import { cva, type VariantProps } from "class-variance-authority";
import type { ComponentProps } from "react";
import { cn } from "@/lib/cn";

/** Одна пилюля вместо трёх прежних определений: .badge, .role-badge и .status-badge. */
const badgeVariants = cva(
  "inline-flex w-fit shrink-0 items-center gap-1.5 rounded-full px-2.5 py-0.5 text-2xs font-semibold whitespace-nowrap [&_svg]:size-3",
  {
    variants: {
      variant: {
        primary: "bg-primary-soft text-primary",
        neutral: "bg-raised text-muted-foreground",
        outline: "border border-border-strong text-muted-foreground",
        warning: "bg-warning/15 text-warning",
        destructive: "bg-destructive/15 text-destructive",
      },
    },
    defaultVariants: { variant: "primary" },
  },
);

export function Badge({
  className,
  variant,
  asChild,
  ...props
}: ComponentProps<"span"> & VariantProps<typeof badgeVariants> & { asChild?: boolean }) {
  const Component = asChild ? Slot : "span";

  return <Component className={cn(badgeVariants({ variant }), className)} {...props} />;
}
