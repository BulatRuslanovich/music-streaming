// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import type { Genre } from "@/lib/types";
import { ToggleGroup } from "@/components/ui/tabs";

export function GenreChips({ genres }: { genres: Genre[] }) {
  return (
    <ToggleGroup>
      {genres.map((genre) => (
        <Link
          key={genre.id}
          href={`/genres?id=${genre.id}`}
          className="flex items-center gap-2 rounded-full bg-raised px-4 py-2 text-sm font-semibold text-muted-foreground transition-colors duration-150 ease-brand hover:bg-accent hover:text-foreground hover:no-underline"
        >
          {genre.name}
          <span className="text-2xs text-faint tabular-nums">{genre.trackCount}</span>
        </Link>
      ))}
    </ToggleGroup>
  );
}
