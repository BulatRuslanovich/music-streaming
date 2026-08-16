"use client";

import type { ReactNode } from "react";
import { cn } from "@/lib/cn";

/**
 * Один вид пустого состояния вместо двух прежних. Раньше половина страниц показывала серую
 * строчку `.empty-state`, а половина — панель `.empty-panel`, и на соседних экранах это
 * читалось как два разных приложения.
 */
export function EmptyState({
  title,
  description,
  action,
  className,
}: {
  title: string;
  description?: string;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <div
      className={cn(
        "flex flex-col items-start gap-2 rounded-xl border border-dashed border-border-strong bg-card px-6 py-8",
        className,
      )}
    >
      <h3 className="text-base font-bold">{title}</h3>
      {description && <p className="text-muted-foreground">{description}</p>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  );
}
