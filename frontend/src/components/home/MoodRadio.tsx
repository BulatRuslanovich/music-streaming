// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import {
  CarIcon,
  CloudRainIcon,
  CoffeeIcon,
  DumbbellIcon,
  MoonIcon,
  PartyPopperIcon,
  RadioIcon,
  SunIcon,
  TargetIcon,
  type LucideIcon,
} from "lucide-react";
import { cn } from "@/lib/cn";
import { moodLabel } from "@/lib/moods";
import { queries } from "@/lib/queries";
import { usePlayerActions, usePlayerState } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { Section } from "@/components/PageHeader";

const ICONS: Record<string, LucideIcon> = {
  workout: DumbbellIcon,
  drive: CarIcon,
  party: PartyPopperIcon,
  focus: TargetIcon,
  chill: CoffeeIcon,
  sleep: MoonIcon,
  happy: SunIcon,
  sad: CloudRainIcon,
};

export function MoodRadio() {
  const t = useT();
  const moods = useQuery(queries.moods());
  const { radioSession } = usePlayerState();
  const player = usePlayerActions();

  const start = useMutation({ mutationFn: (mood: string) => player.startRadio(null, mood) });

  if (!moods.data?.length) return null;

  return (
    <Section title={t("radio.moods")} note={t("radio.moodsNote")}>
      <div className="flex flex-wrap gap-2 max-md:-mx-4 max-md:flex-nowrap max-md:overflow-x-auto max-md:px-4 max-md:[scrollbar-width:none]">
        {moods.data.map(({ key }) => {
          const Icon = ICONS[key] ?? RadioIcon;
          const active = radioSession?.mood === key;
          const pending = start.isPending && start.variables === key;

          return (
            <button
              key={key}
              type="button"
              onClick={() => start.mutate(key)}
              disabled={start.isPending}
              aria-pressed={active}
              className={cn(
                "inline-flex h-10 shrink-0 items-center gap-2 rounded-full bg-card px-4 text-sm font-medium",
                "transition-colors duration-150 ease-brand hover:bg-raised disabled:cursor-progress",
                active && "bg-primary/15 text-primary hover:bg-primary/20",
                pending && "animate-pulse",
              )}
            >
              <Icon size={16} className={cn(!active && "text-muted-foreground")} />
              {moodLabel(key, t)}
            </button>
          );
        })}
      </div>
    </Section>
  );
}
