// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { cn } from "@/lib/cn";
import { cardGrid } from "@/components/collection/layout";

export function PosterGrid({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cn(cardGrid, className)}>{children}</div>;
}

export function Poster({
  href,
  onClick,
  cover,
  title,
  subtitle,
  footnote,
  badge,
  overlay,
  wide = false,
}: {
  href?: string;
  onClick?: () => void;
  cover: ReactNode;
  title: string;
  subtitle?: ReactNode;
  footnote?: ReactNode;
  badge?: ReactNode;
  overlay?: ReactNode;
  wide?: boolean;
}) {
  const shell = cn(
    "group relative overflow-hidden rounded-xl bg-card text-left shadow-art hover:no-underline",
    // Плитка целиком и есть обложка, поэтому здесь тень растёт у неё самой — тот же ответ
    // на наведение, что и у карточки на полке.
    "transition-shadow duration-200 ease-brand hover:shadow-pop",
    "motion-safe:hover:[&_img]:scale-[1.03]",
    wide ? "col-span-2 aspect-[2/1] max-md:col-span-1 max-md:aspect-square" : "aspect-square",
  );

  const body = (
    <>
      {cover}

      <span
        aria-hidden="true"
        className="absolute inset-0 bg-[linear-gradient(to_top,rgb(0_0_0/80%),rgb(0_0_0/25%)_45%,transparent_72%)]"
      />

      {badge}
      {overlay}

      <span className="absolute inset-x-0 bottom-0 flex flex-col p-3">
        <span className={cn("truncate font-semibold text-white", wide && "text-lg")}>{title}</span>
        {subtitle && <span className="truncate text-sm text-white/70">{subtitle}</span>}
        {footnote && <span className="mt-0.5 truncate text-xs text-white/60">{footnote}</span>}
      </span>
    </>
  );

  if (href) {
    return (
      <Link href={href} className={shell}>
        {body}
      </Link>
    );
  }

  return (
    <button type="button" onClick={onClick} className={shell}>
      {body}
    </button>
  );
}
