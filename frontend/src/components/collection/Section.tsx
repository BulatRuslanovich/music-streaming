// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ReactNode, Ref } from "react";
import { cn } from "@/lib/cn";
import { SectionHeader } from "@/components/PageHeader";

export function Section({
  eyebrow,
  title,
  href,
  actions,
  className,
  ref,
  children,
}: {
  eyebrow?: string;
  title: string;
  href?: string;
  actions?: ReactNode;
  className?: string;
  ref?: Ref<HTMLElement>;
  children: ReactNode;
}) {
  return (
    <section ref={ref} className={cn("group/section flex flex-col gap-3", className)}>
      <SectionHeader eyebrow={eyebrow} title={title} href={href}>
        {actions}
      </SectionHeader>
      {children}
    </section>
  );
}
