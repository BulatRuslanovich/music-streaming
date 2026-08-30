// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ComponentProps } from "react";
import { cn } from "@/lib/cn";
import { cardGrid } from "@/components/collection/layout";

export function Skeleton({ className, ...props }: ComponentProps<"div">) {
  // Поверхность и движение — в классе `.skeleton` (theme.css): блок стоит на видимом уровне
  // и по нему проходит направленный блик, а не пульсация прозрачностью.
  return <div className={cn("skeleton rounded-lg", className)} {...props} />;
}

// Размеры берём из тех же рецептов, что и настоящие сетки: скелет со своими значениями
// расходился с ними, и на загрузке раскладка ощутимо переставлялась на месте.
const shapes = {
  // Карточка — квадратная обложка плюс две строки подписи и p-3 вокруг.
  card: "aspect-[0.74] rounded-xl",
  row: "h-14 rounded-lg",
  tile: "h-24 rounded-xl",
} as const;

const layouts = {
  card: cardGrid,
  row: "flex flex-col gap-2",
  tile: "grid grid-cols-[repeat(auto-fill,minmax(10.25rem,1fr))] gap-4",
} as const;

export type SkeletonVariant = keyof typeof shapes | "detail" | "spotlight";

// Геометрия повторяет DetailHero вплоть до отрицательных полей: скелет с собственными
// размерами переставлял бы шапку на месте в момент загрузки.
function DetailSkeleton() {
  return (
    <div
      className={cn(
        "-mx-8 -mt-7 flex flex-wrap items-end gap-8 px-8 pt-10 pb-4",
        "max-md:-mx-4 max-md:-mt-5 max-md:items-start max-md:gap-4 max-md:px-4 max-md:pt-6",
      )}
      aria-hidden="true"
    >
      <Skeleton className="size-70 shrink-0 max-md:size-32" />
      <div className="flex min-w-[min(16rem,100%)] flex-1 flex-col gap-3">
        <Skeleton className="h-3 w-20" />
        <Skeleton className="h-11 w-2/3" />
        <Skeleton className="h-4 w-1/2" />
        <Skeleton className="mt-2 h-13 w-13 rounded-full" />
      </div>
    </div>
  );
}

function SpotlightSkeleton() {
  return <Skeleton className="h-[17.5rem] rounded-xl max-lg:h-[22rem] max-md:h-[15rem]" />;
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
