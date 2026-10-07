// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { createElement } from "react";
import type { LucideProps } from "lucide-react";
import { moodIcon } from "@/lib/moods";

export function MoodIcon({ mood, ...props }: { mood: string } & LucideProps) {
  return createElement(moodIcon(mood), props);
}
