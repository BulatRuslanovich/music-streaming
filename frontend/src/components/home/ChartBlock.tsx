// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { HomeBlock } from "@/lib/types";
import type { PlaybackOrigin } from "@/contexts/PlayerContext";
import { capFiveOnMobile } from "@/components/collection/layout";
import { RankedList } from "@/components/collection/RankedList";

/**
 * Чарт — единственный блок ленты, который не построен на обложках, но подавался как все:
 * голый нумерованный список между двумя лентами карточек. Подложка `bg-card` — та же, что
 * у геро и у карточек, — делает его вторым якорем страницы и разрывает цепочку одинаковых
 * полок, не добавляя странице ни одного нового приёма.
 *
 * Паддинг меньше, чем у геро: у строк есть собственный `px-2` под подсветку наведения,
 * и он должен лежать внутри поля панели, а не складываться с ним в отступ на два пальца.
 */
export function ChartBlock({ block, origin }: { block: HomeBlock; origin: PlaybackOrigin }) {
  return (
    <div className="rounded-xl bg-card p-3 max-md:p-2">
      <RankedList tracks={block.tracks ?? []} origin={origin} className={capFiveOnMobile} />
    </div>
  );
}
