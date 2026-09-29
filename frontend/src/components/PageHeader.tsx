// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { ChevronRightIcon } from "lucide-react";
import type { Route } from "next";
import Link from "next/link";
import type { ReactNode, Ref } from "react";
import { cn } from "@/lib/cn";
import { cardGrid } from "@/components/collection/layout";
import { useT } from "@/contexts/I18nContext";

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
        <h1 className="font-display text-display">{title}</h1>
        {subtitle && <p className="mt-2 text-sm text-muted-foreground">{subtitle}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-3">{actions}</div>}
    </header>
  );
}

/**
 * Шапка секции: заголовок, необязательная пояснительная строка под ним и «Все» справа.
 * `actions` встают рядом со ссылкой — туда полка кладёт свои стрелки.
 */
export function SectionHeader<T extends string>({
  title,
  note,
  href,
  actions,
}: {
  title: string;
  note?: string;
  href?: Route<T>;
  actions?: ReactNode;
}) {
  const t = useT();

  return (
    <div className="flex items-end justify-between gap-3">
      <div className="min-w-0">
        <h2 className="truncate text-section font-semibold">{title}</h2>
        {note && <p className="truncate text-sm text-muted-foreground">{note}</p>}
      </div>
      <div className="flex shrink-0 items-center gap-1">
        {href && (
          <Link
            href={href}
            className="flex items-center gap-0.5 text-sm text-muted-foreground transition-colors duration-150 ease-brand hover:text-foreground hover:no-underline"
          >
            {t("action.seeAll")}
            <ChevronRightIcon size={16} />
          </Link>
        )}
        {actions}
      </div>
    </div>
  );
}

export function Section<T extends string>({
  title,
  note,
  href,
  actions,
  className,
  ref,
  children,
}: {
  title: string;
  note?: string;
  href?: Route<T>;
  actions?: ReactNode;
  className?: string;
  ref?: Ref<HTMLElement>;
  children: ReactNode;
}) {
  return (
    <section ref={ref} className={cn("group/section flex flex-col gap-4", className)}>
      <SectionHeader title={title} note={note} href={href} actions={actions} />
      {children}
    </section>
  );
}

export function CardGrid({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cn(cardGrid, className)}>{children}</div>;
}
