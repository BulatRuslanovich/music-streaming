// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ComponentProps } from "react";
import { cn } from "@/lib/cn";

export function Skeleton({ className, ...props }: ComponentProps<"div">) {
  return <div className={cn("animate-pulse rounded-lg bg-card", className)} {...props} />;
}

const shapes = {
  card: "aspect-[0.78] rounded-xl",
  row: "h-14 rounded-lg",
  tile: "h-24 rounded-xl",
} as const;

const layouts = {
  card: "grid grid-cols-[repeat(auto-fill,minmax(9.5rem,1fr))] gap-4",
  row: "flex flex-col gap-2",
  tile: "grid grid-cols-[repeat(auto-fill,minmax(10.5rem,1fr))] gap-4",
} as const;

export function SkeletonGroup({
  variant = "card",
  count = 6,
  className,
}: {
  variant?: keyof typeof shapes;
  count?: number;
  className?: string;
}) {
  return (
    <div className={cn(layouts[variant], className)} aria-hidden="true">
      {Array.from({ length: count }, (_, index) => (
        <Skeleton key={index} className={shapes[variant]} />
      ))}
    </div>
  );
}
