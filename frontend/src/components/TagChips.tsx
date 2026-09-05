// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import { cn } from "@/lib/cn";
import { tagLabel } from "@/lib/tagLabel";
import type { TagWeight } from "@/lib/types";
import { Badge } from "./ui/badge";

/** Дальше шестого тега начинается хвост Last.fm, который описывает уже не эту запись. */
const DEFAULT_LIMIT = 6;

/** Имя тега уезжает параметром: среди тегов попадается «rock/pop», и путь бы он разорвал. */
export function tagHref(name: string): `/tags?name=${string}` {
  return `/tags?name=${encodeURIComponent(name)}`;
}

export function TagChips({
  tags,
  limit = DEFAULT_LIMIT,
  className,
}: {
  tags: TagWeight[];
  limit?: number;
  className?: string;
}) {
  const shown = tags.slice(0, limit);

  if (shown.length === 0) return null;

  return (
    <ul className={cn("flex flex-wrap gap-1.5", className)}>
      {shown.map((tag) => (
        <li key={tag.name}>
          <Badge asChild variant="neutral">
            <Link href={tagHref(tag.name)} className="hover:bg-accent hover:no-underline">
              {tagLabel(tag.name)}
            </Link>
          </Badge>
        </li>
      ))}
    </ul>
  );
}
