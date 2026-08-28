// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import { useRef, type ReactNode } from "react";
import { cn } from "@/lib/cn";
import { capEightOnMobile, cardGrid, cardShelf, scrollFade } from "@/components/collection/layout";
import { useShelfEdges } from "@/components/collection/shelfScroll";
import { useT } from "@/contexts/I18nContext";
import { Button } from "./ui/button";
import { ChevronLeftIcon, ChevronRightIcon } from "./Icons";

export function PageHeader({
  title,
  subtitle,
  actions,
}: {
  title: string;
  subtitle?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <header className="flex flex-wrap items-end justify-between gap-5 max-md:items-start">
      <div className="min-w-0">
        <h1 className="text-[clamp(1.75rem,1.2rem+1.8vw,2.6rem)]">{title}</h1>
        {subtitle && <p className="mt-1 text-sm text-faint">{subtitle}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-4">{actions}</div>}
    </header>
  );
}

export function SectionHeader({
  eyebrow,
  title,
  href,
  children,
}: {
  eyebrow?: string;
  title: string;
  href?: string;
  children?: ReactNode;
}) {
  const t = useT();

  return (
    <div className="flex items-end justify-between gap-3">
      <div className="min-w-0">
        {eyebrow && (
          <p className="text-2xs font-bold tracking-wider text-faint uppercase">{eyebrow}</p>
        )}
        <h2 className="truncate text-lg font-bold">{title}</h2>
      </div>
      <div className="flex shrink-0 items-center gap-1">
        {href && (
          <Link
            href={href}
            className={cn(
              "flex items-center gap-0.5 text-sm font-semibold text-faint transition-colors duration-150 ease-brand",
              "group-hover/section:text-foreground hover:text-foreground hover:no-underline",
              "focus-visible:text-foreground max-md:text-muted-foreground",
            )}
          >
            {t("action.seeAll")}
            <ChevronRightIcon size={16} />
          </Link>
        )}
        {children}
      </div>
    </div>
  );
}

export function CardGrid({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cn(cardGrid, className)}>{children}</div>;
}

export function Shelf({
  eyebrow,
  title,
  href,
  className,
  children,
}: {
  eyebrow?: string;
  title: string;
  href?: string;
  className?: string;
  children: ReactNode;
}) {
  const t = useT();
  const shelf = useRef<HTMLDivElement>(null);
  const { atStart, atEnd, scrollShelf } = useShelfEdges(shelf);

  return (
    <section className={cn("group/section flex flex-col gap-3", className)}>
      <SectionHeader eyebrow={eyebrow} title={title} href={href}>
        <div
          className={cn(
            "max-md:hidden flex gap-1",
            "opacity-0 transition-opacity duration-150 ease-brand",
            "group-hover/section:opacity-100 group-focus-within/section:opacity-100",
          )}
        >
          <Button
            variant="ghost"
            size="icon"
            onClick={() => scrollShelf(-1)}
            disabled={atStart}
            aria-label={t("shelf.scrollBackwards", { title })}
          >
            <ChevronLeftIcon size={20} />
          </Button>
          <Button
            variant="ghost"
            size="icon"
            onClick={() => scrollShelf(1)}
            disabled={atEnd}
            aria-label={t("shelf.scrollForwards", { title })}
          >
            <ChevronRightIcon size={20} />
          </Button>
        </div>
      </SectionHeader>

      <div ref={shelf} className={cn(cardShelf, scrollFade, capEightOnMobile, "px-0 pt-1 pb-2")}>
        {children}
      </div>
    </section>
  );
}
