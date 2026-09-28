// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import {
  useSoundSettings,
  updateSoundSettings,
  type SoundSettings as Settings,
} from "@/lib/playback/soundSettings";
import { useT } from "@/contexts/I18nContext";
import { Seekbar } from "./Seekbar";
import { RadioCard, RadioGroup } from "./ui/radio-group";

const TRANSITIONS = ["off", "crossfade", "gapless"] as const;

/**
 * Раскладка ровно та же, что у остальных настроек воспроизведения: подпись, пояснение,
 * карточки выбора — не вложенная карточка со своим заголовком уровня раздела и системными
 * `select`, единственными такими на странице.
 */
export function SoundSettings() {
  const sound = useSoundSettings();
  const t = useT();

  return (
    <fieldset className="flex flex-col gap-2 border-0 p-0">
      <legend className="font-semibold">{t("sound.transitions")}</legend>
      <p className="text-sm text-muted-foreground">{t("sound.transitionHint")}</p>
      <p className="text-sm text-muted-foreground">{t("sound.deviceHint")}</p>

      <RadioGroup
        className="mt-1"
        value={sound.transition}
        onValueChange={(transition) =>
          updateSoundSettings({ transition: transition as Settings["transition"] })
        }
      >
        {TRANSITIONS.map((option) => (
          <RadioCard key={option} value={option} label={t(`sound.${option}`)} />
        ))}
      </RadioGroup>

      {sound.transition === "crossfade" && <Crossfade seconds={sound.crossfadeSeconds} />}
    </fieldset>
  );
}

/** Длительность смешивания появляется только вместе с самим смешиванием. */
function Crossfade({ seconds }: { seconds: number }) {
  const t = useT();

  return (
    <div className="mt-1 flex items-center gap-3">
      <Seekbar
        value={seconds}
        max={12}
        step={1}
        onSeek={(next) => updateSoundSettings({ crossfadeSeconds: Math.max(1, next) })}
        ariaLabel={t("sound.crossfade")}
        className="max-w-56 flex-1"
      />
      <span className="text-sm tabular-nums text-muted-foreground">
        {t("sound.duration", { seconds })}
      </span>
    </div>
  );
}
