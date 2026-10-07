// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { cn } from "@/lib/cn";
import { moodLabel } from "@/lib/moods";
import { queries } from "@/lib/queries";
import { usePlayerActions, usePlayerState } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { MoodIcon } from "@/components/MoodIcon";
import { Section } from "@/components/PageHeader";
import { hiddenScrollbar } from "@/components/Shelf";
import { ToggleGroup, ToggleGroupButton } from "@/components/ui/toggle-group";

export function MoodRadio() {
  const t = useT();
  const moods = useQuery(queries.moods());
  const { radioSession } = usePlayerState();
  const player = usePlayerActions();

  const start = useMutation({ mutationFn: (mood: string) => player.startRadio(null, mood) });

  if (!moods.data?.length) return null;

  return (
    <Section title={t("radio.moods")} note={t("radio.moodsNote")}>
      <ToggleGroup
        className={cn(
          "max-md:-mx-4 max-md:flex-nowrap max-md:overflow-x-auto max-md:px-4",
          hiddenScrollbar,
        )}
      >
        {moods.data.map((key) => {
          return (
            <ToggleGroupButton
              key={key}
              active={radioSession?.mood === key}
              onClick={() => start.mutate(key)}
              disabled={start.isPending}
              className={cn(start.isPending && start.variables === key && "animate-pulse")}
            >
              <MoodIcon mood={key} size={16} />
              {moodLabel(key, t)}
            </ToggleGroupButton>
          );
        })}
      </ToggleGroup>
    </Section>
  );
}
