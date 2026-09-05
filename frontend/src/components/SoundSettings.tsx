// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ReactNode } from "react";
import {
  useSoundSettings,
  updateSoundSettings,
  type SoundSettings as Settings,
} from "@/lib/soundSettings";
import { useT } from "@/contexts/I18nContext";
import { Seekbar } from "./Seekbar";
import { RadioCard, RadioGroup } from "./ui/radio-group";

type SoundChoice = Settings["normalization"] | Settings["transition"];

const NORMALIZATION = ["off", "track", "album"] as const;
const TRANSITIONS = ["off", "crossfade", "gapless"] as const;

/**
 * Раскладка ровно та же, что у остальных настроек воспроизведения: подпись, пояснение,
 * карточки выбора. Раньше блок был вложенной карточкой со своим заголовком уровня раздела
 * и двумя системными `select` — единственными системными контролами на странице. Пояснение
 * при этом стояло под списком, между двумя настройками, и не было видно, к какой из них оно.
 */
export function SoundSettings() {
  const sound = useSoundSettings();
  const t = useT();

  return (
    <fieldset className="flex flex-col gap-2 border-0 p-0">
      <legend className="font-semibold">{t("sound.title")}</legend>
      <p className="text-sm text-muted-foreground">{t("sound.deviceHint")}</p>

      <div className="mt-2 flex flex-col gap-5">
        <Choice
          label={t("sound.normalization")}
          hint={t("sound.normalizationHint")}
          options={NORMALIZATION}
          value={sound.normalization}
          onChange={(normalization) => updateSoundSettings({ normalization })}
        />

        <Choice
          label={t("sound.transitions")}
          hint={t("sound.transitionHint")}
          options={TRANSITIONS}
          value={sound.transition}
          onChange={(transition) => updateSoundSettings({ transition })}
        >
          {sound.transition === "crossfade" && <Crossfade seconds={sound.crossfadeSeconds} />}
        </Choice>
      </div>
    </fieldset>
  );
}

function Choice<T extends SoundChoice>({
  label,
  hint,
  options,
  value,
  onChange,
  children,
}: {
  label: string;
  hint: string;
  options: readonly T[];
  value: T;
  onChange: (value: T) => void;
  children?: ReactNode;
}) {
  const t = useT();

  return (
    <fieldset className="flex flex-col gap-2 border-0 p-0">
      <legend className="text-sm font-medium">{label}</legend>
      <p className="text-sm text-muted-foreground">{hint}</p>

      <RadioGroup className="mt-1" value={value} onValueChange={(next) => onChange(next as T)}>
        {options.map((option) => (
          <RadioCard key={option} value={option} label={t(`sound.${option}`)} />
        ))}
      </RadioGroup>

      {children}
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
