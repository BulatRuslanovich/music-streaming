// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { cn } from "@/lib/cn";

export function QuickRow({ children }: { children: ReactNode }) {
  return (
    <div
      className={cn(
        "grid grid-flow-col auto-cols-[16rem] gap-3 overflow-x-auto overscroll-x-contain pb-1",
        "[scroll-snap-type:x_proximity]",
        "[mask-image:linear-gradient(to_right,#000_calc(100%-3.5rem),transparent)]",
        "max-md:auto-cols-[13rem] max-md:gap-2 max-md:[mask-image:none]",
        "[&>*]:animate-rise [&>*]:[scroll-snap-align:start]",
      )}
    >
      {children}
    </div>
  );
}

export function Tile({
  href,
  onClick,
  art,
  label,
  sublabel,
  current = false,
  action,
  className,
  disabled = false,
  pressed,
}: {
  href?: string;
  onClick?: () => void;
  art: ReactNode;
  label: string;
  sublabel?: ReactNode;
  current?: boolean;
  action?: ReactNode;
  className?: string;
  disabled?: boolean;
  pressed?: boolean;
}) {
  const body = (
    <>
      <span className="size-16 shrink-0 overflow-hidden max-md:size-14">{art}</span>

      <span className="flex min-w-0 flex-1 flex-col">
        <span className={cn("truncate font-semibold", current && "text-primary")}>{label}</span>
        {sublabel && <span className="truncate text-sm text-muted-foreground">{sublabel}</span>}
      </span>

      {action}
    </>
  );

  const shell = cn(
    "group flex h-16 items-center gap-3 overflow-hidden rounded-lg bg-card pr-3 text-left shadow-panel max-md:h-14",
    "transition-colors duration-150 ease-brand hover:bg-raised hover:no-underline",
    "disabled:pointer-events-none disabled:opacity-55",
    className,
  );

  if (href) {
    return (
      <Link href={href} className={shell}>
        {body}
      </Link>
    );
  }

  return (
    <button
      type="button"
      onClick={onClick}
      className={shell}
      disabled={disabled}
      aria-pressed={pressed}
    >
      {body}
    </button>
  );
}
