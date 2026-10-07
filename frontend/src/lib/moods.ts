// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { Translate } from "@/contexts/I18nContext";
import type { TranslationKey } from "@/lib/i18n/en";

// Список настроений приходит с сервера: настроение, которое убрали из moods.json, пропадает
// у всех клиентов без их обновления, а незнакомое показывается по ключу.
const LABELS: Record<string, TranslationKey> = {
  workout: "radio.mood.workout",
  drive: "radio.mood.drive",
  party: "radio.mood.party",
  focus: "radio.mood.focus",
  chill: "radio.mood.chill",
  sleep: "radio.mood.sleep",
  happy: "radio.mood.happy",
  sad: "radio.mood.sad",
};

export function moodLabel(key: string, t: Translate): string {
  const label = LABELS[key];
  return label ? t(label) : key.charAt(0).toUpperCase() + key.slice(1);
}
