// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import {
  useSoundSettings,
  updateSoundSettings,
  type SoundSettings as Settings,
} from "@/lib/soundSettings";
import { useT } from "@/contexts/I18nContext";
import { Surface } from "./ui/card";

export function SoundSettings() {
  const sound = useSoundSettings();
  const t = useT();
  return (
    <Surface className="flex flex-col gap-4">
      <h2 className="text-section font-semibold">{t("sound.title")}</h2>
      <p className="text-sm text-muted-foreground">{t("sound.deviceHint")}</p>
      <label className="flex flex-wrap items-center justify-between gap-3">
        {t("sound.normalization")}
        <select
          className="rounded-md border bg-background p-2"
          value={sound.normalization}
          onChange={(event) =>
            updateSoundSettings({ normalization: event.target.value as Settings["normalization"] })
          }
        >
          {(["off", "track", "album"] as const).map((mode) => (
            <option key={mode} value={mode}>
              {t(`sound.${mode}`)}
            </option>
          ))}
        </select>
      </label>
      <p className="text-sm text-muted-foreground">{t("sound.normalizationHint")}</p>
      <label className="flex flex-wrap items-center justify-between gap-3">
        {t("sound.transitions")}
        <select
          className="rounded-md border bg-background p-2"
          value={sound.transition}
          onChange={(event) =>
            updateSoundSettings({ transition: event.target.value as Settings["transition"] })
          }
        >
          {(["off", "crossfade", "gapless"] as const).map((mode) => (
            <option key={mode} value={mode}>
              {t(`sound.${mode}`)}
            </option>
          ))}
        </select>
      </label>
      {sound.transition === "crossfade" && (
        <label className="flex items-center gap-3">
          {t("sound.duration", { seconds: sound.crossfadeSeconds })}
          <input
            type="range"
            min="1"
            max="12"
            step="1"
            value={sound.crossfadeSeconds}
            onChange={(event) =>
              updateSoundSettings({ crossfadeSeconds: Number(event.target.value) })
            }
          />
        </label>
      )}
      <p className="text-sm text-muted-foreground">{t("sound.transitionHint")}</p>
    </Surface>
  );
}
