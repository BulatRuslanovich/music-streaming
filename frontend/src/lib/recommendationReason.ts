// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { RecommendationReason } from "@/lib/types";
import type { Translate } from "@/contexts/I18nContext";

/**
 * Почему трек (или полка) здесь оказался — одной строкой. Ключи `kind` приходят с бэкенда,
 * это константы `ReasonKinds`; всё нераспознанное сводится к discovery, потому что подпись
 * тут украшение, а не контракт: новый вид причины не должен ронять очередь.
 */
export function reasonLabel(reason: RecommendationReason, t: Translate): string {
  const subject = reason.subject ?? "";

  switch (reason.kind) {
    case "becauseYouListened":
      return t("rec.reason.becauseYouListened", { subject });
    case "popularWithSimilarTaste":
      return t("rec.reason.similarTaste");
    case "newFromArtistYouPlay":
      return t("rec.reason.newFromArtist", { subject });
    case "fromGenreYouLike":
      return t("rec.reason.genre", { subject });
    case "trending":
      return t("rec.reason.trending");
    case "freshInLibrary":
      return t("rec.reason.fresh");
    case "soundsLike":
      return t("rec.reason.soundsLike", { subject });
    case "matchesYourTaste":
      return t("rec.reason.matchesYourTaste");
    default:
      return t("rec.reason.discovery");
  }
}
