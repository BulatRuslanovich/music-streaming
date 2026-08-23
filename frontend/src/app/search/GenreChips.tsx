// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import type { Genre } from "@/lib/types";
import { ToggleGroup } from "@/components/ui/tabs";

/**
 * В поиске жанры раньше рисовались неинтерактивным `Badge` — выглядели как чипы на `/genres`,
 * но никуда не вели. Теперь это ссылки, и вид у них тот же, что на странице жанров.
 */
export function GenreChips({ genres }: { genres: Genre[] }) {
  return (
    <ToggleGroup variant="chip">
      {genres.map((genre) => (
        <Link
          key={genre.id}
          href={`/genres?id=${genre.id}`}
          className="flex items-center gap-2 rounded-full border border-border px-3.5 py-1.5 text-sm font-semibold text-muted-foreground transition-colors duration-150 ease-brand hover:border-border-strong hover:text-foreground hover:no-underline"
        >
          {genre.name}
          <span className="text-2xs text-faint tabular-nums">{genre.trackCount}</span>
        </Link>
      ))}
    </ToggleGroup>
  );
}
