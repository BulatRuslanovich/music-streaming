// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import type { CSSProperties } from "react";
import { cn } from "@/lib/cn";
import { moodHue, moodLabel } from "@/lib/moods";
import { queries } from "@/lib/queries";
import { usePlayerActions, usePlayerState } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { MoodIcon } from "@/components/MoodIcon";
import { Section } from "@/components/PageHeader";
import { NowPlayingBars } from "@/components/PlaybackIndicators";
import { hiddenScrollbar } from "@/components/Shelf";

export function MoodRadio() {
  const t = useT();
  const moods = useQuery(queries.moods());
  const { radioSession, isPlaying } = usePlayerState();
  const player = usePlayerActions();

  const start = useMutation({ mutationFn: (mood: string) => player.startRadio(null, mood) });

  if (!moods.data?.length) return null;

  return (
    <Section title={t("radio.moods")} note={t("radio.moodsNote")}>
      <div
        style={
          {
            "--per-row": balanced(moods.data.length, 4),
            "--per-row-wide": balanced(moods.data.length, 8),
          } as CSSProperties
        }
        className={cn(
          "grid grid-cols-[repeat(var(--per-row),minmax(0,1fr))] gap-3 xl:grid-cols-[repeat(var(--per-row-wide),minmax(0,1fr))]",
          "max-md:-mx-4 max-md:flex max-md:overflow-x-auto max-md:px-4 max-md:[&>*]:w-32 max-md:[&>*]:shrink-0",
          hiddenScrollbar,
        )}
      >
        {moods.data.map((key) => {
          const active = radioSession?.mood === key;

          return (
            <button
              key={key}
              type="button"
              aria-pressed={active}
              onClick={() => start.mutate(key)}
              disabled={start.isPending}
              style={{ "--mood-hue": moodHue(key) } as CSSProperties}
              className={cn(
                "mood-tile group relative isolate flex h-24 flex-col justify-between overflow-hidden rounded-md p-3.5 text-left",
                "transition-[translate,box-shadow,opacity] duration-200 ease-brand hover:-translate-y-0.5 hover:shadow-art",
                "outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring",
                "disabled:cursor-progress motion-reduce:hover:translate-y-0",
                active && "ring-2 ring-foreground ring-offset-2 ring-offset-background",
                start.isPending && start.variables !== key && "opacity-60",
                start.isPending && start.variables === key && "animate-pulse",
              )}
            >
              {/* Крупный знак настроения, срезанный углом плитки: он и различает плитки, когда цвета близки. */}
              <MoodIcon
                mood={key}
                aria-hidden="true"
                strokeWidth={1.5}
                className="absolute -right-4 -bottom-5 -z-10 size-24 -rotate-12 text-(--mood-mark) transition-transform duration-300 ease-brand group-hover:rotate-0 motion-reduce:transition-none"
              />
              <span className="flex items-center justify-between">
                <MoodIcon mood={key} size={22} aria-hidden="true" />
                {active && isPlaying && <NowPlayingBars />}
              </span>
              <span className="truncate font-semibold">{moodLabel(key, t)}</span>
            </button>
          );
        })}
      </div>
    </Section>
  );
}

// Ряды поровну: 8 плиток — 4 + 4, а не 7 + 1, и так же для любого числа настроений с сервера.
function balanced(count: number, max: number): number {
  return Math.ceil(count / Math.ceil(count / max));
}
