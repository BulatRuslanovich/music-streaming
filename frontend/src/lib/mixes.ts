// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { TranslationKey } from "@/lib/i18n";
import type { HomeMixSlug } from "@/lib/types";

export const MIXES = {
  daily: { title: "home.dailyMix", description: "mixes.dailyDescription" },
  new: { title: "home.newArrivals", description: "mixes.newDescription" },
  top: { title: "home.topThisWeek", description: "mixes.topDescription" },
} satisfies Record<HomeMixSlug, { title: TranslationKey; description: TranslationKey }>;

export function isMixSlug(value: string): value is HomeMixSlug {
  return Object.hasOwn(MIXES, value);
}
