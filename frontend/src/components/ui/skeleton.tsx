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

export type SkeletonVariant = keyof typeof shapes | "detail" | "spotlight";

function DetailSkeleton({ round = false }: { round?: boolean }) {
  return (
    <div
      className="flex flex-wrap items-end gap-8 p-5 max-md:items-start max-md:gap-3 max-md:p-3"
      aria-hidden="true"
    >
      <Skeleton className={cn("size-52 shrink-0 max-md:size-30", round && "rounded-full")} />
      <div className="flex min-w-[min(16rem,100%)] flex-1 flex-col gap-3">
        <Skeleton className="h-3 w-20" />
        <Skeleton className="h-9 w-2/3" />
        <Skeleton className="h-4 w-1/2" />
        <Skeleton className="mt-1 h-10 w-40 rounded-full" />
      </div>
    </div>
  );
}

function SpotlightSkeleton() {
  return <Skeleton className="h-[17.5rem] rounded-2xl max-lg:h-[22rem] max-md:h-[15rem]" />;
}

export function SkeletonGroup({
  variant = "card",
  count = 6,
  className,
}: {
  variant?: SkeletonVariant;
  count?: number;
  className?: string;
}) {
  if (variant === "spotlight") return <SpotlightSkeleton />;

  if (variant === "detail") {
    return (
      <div className={cn("flex flex-col gap-8", className)} aria-hidden="true">
        <DetailSkeleton />
        <div className={layouts.row}>
          {Array.from({ length: count }, (_, index) => (
            <Skeleton key={index} className={shapes.row} />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div className={cn(layouts[variant], className)} aria-hidden="true">
      {Array.from({ length: count }, (_, index) => (
        <Skeleton key={index} className={shapes[variant]} />
      ))}
    </div>
  );
}
