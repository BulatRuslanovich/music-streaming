// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ComponentType } from "react";
import { cn } from "@/lib/cn";
import type { DjMode } from "@/lib/types";
import { usePlayer } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { ClockIcon, HeartIcon, RadioIcon, ShuffleIcon, type IconProps } from "../Icons";

const MODES: { mode: DjMode; icon: ComponentType<IconProps> }[] = [
  { mode: "ForYou", icon: HeartIcon },
  { mode: "Rediscover", icon: ClockIcon },
  { mode: "Discover", icon: ShuffleIcon },
  { mode: "Flow", icon: RadioIcon },
];

export function DjLauncher() {
  const t = useT();
  const player = usePlayer();

  return (
    <section className="flex flex-col gap-3" aria-labelledby="dj-heading">
      <div>
        <h2 id="dj-heading" className="text-xl font-bold">
          {t("dj.title")}
        </h2>
        <p className="mt-0.5 text-sm text-muted-foreground">{t("dj.subtitle")}</p>
      </div>

      <div className="grid grid-cols-4 gap-3 max-lg:grid-cols-2 max-sm:grid-cols-1">
        {MODES.map(({ mode, icon: Icon }) => {
          const active = player.dj?.mode === mode;

          return (
            <button
              key={mode}
              type="button"
              disabled={player.djLoading}
              aria-pressed={active}
              onClick={() =>
                void player.startDj(mode, mode === "Flow" ? player.currentTrack : null)
              }
              className={cn(
                "group flex min-h-24 items-center gap-3 rounded-xl border p-4 text-left shadow-panel",
                "transition-[background-color,border-color,transform] duration-150 ease-brand hover:-translate-y-0.5",
                "disabled:pointer-events-none disabled:opacity-55",
                active
                  ? "border-primary/40 bg-primary-soft"
                  : "border-border bg-card hover:border-border-strong hover:bg-raised",
              )}
            >
              <span className="grid size-11 shrink-0 place-items-center rounded-full bg-primary-soft text-primary group-aria-pressed:bg-primary group-aria-pressed:text-primary-foreground">
                <Icon size={20} />
              </span>
              <span className="min-w-0">
                <span className="block font-bold">{t(`dj.mode.${mode}`)}</span>
                <span className="mt-0.5 block text-xs leading-snug text-muted-foreground">
                  {t(`dj.mode.${mode}.hint`)}
                </span>
              </span>
            </button>
          );
        })}
      </div>
    </section>
  );
}
