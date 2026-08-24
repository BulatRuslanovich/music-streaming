// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { cn } from "@/lib/cn";

export function PosterGrid({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div
      className={cn(
        "grid grid-cols-[repeat(auto-fill,minmax(10.25rem,1fr))] gap-5",
        "max-md:grid-cols-[repeat(auto-fill,minmax(8.75rem,1fr))] max-md:gap-3",
        "[&>*]:animate-rise",
        className,
      )}
    >
      {children}
    </div>
  );
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
