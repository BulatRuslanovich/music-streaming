// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useCallback } from "react";
import { api } from "@/lib/api";
import { recordEvent } from "@/lib/events";
import { useInvalidate } from "@/lib/useInvalidate";
import { usePlayerActions } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";

/**
 * Оптимистичный лайк: сначала правим то, что видно, потом ходим на сервер и откатываемся,
 * если он отказал. Раньше это жило в трёх копиях (Player, TrackList, CommandPalette) и они
 * уже разошлись — только одна из них инвалидировала избранное.
 *
 * `patchTrack` чинит очередь: тот же трек может быть и в списке, и в плеере одновременно.
 * `onLocal` — для списков, которые ведут собственный словарь лайков поверх данных запроса.
 */
export function useToggleFavorite(): (
  track: { id: string; isFavorite: boolean },
  onLocal?: (next: boolean) => void,
) => Promise<void> {
  const { patchTrack } = usePlayerActions();
  const { notifyError } = useToast();
  const invalidate = useInvalidate();
  const t = useT();

  return useCallback(
    async (track, onLocal) => {
      const next = !track.isFavorite;

      onLocal?.(next);
      patchTrack(track.id, { isFavorite: next });

      try {
        if (next) await api.addFavorite(track.id);
        else await api.removeFavorite(track.id);

        recordEvent({ type: next ? "trackLiked" : "trackUnliked", trackId: track.id });
        invalidate("favorites");
      } catch (error) {
        onLocal?.(!next);
        patchTrack(track.id, { isFavorite: !next });
        notifyError(error, t("tracks.favoritesFailed"));
      }
    },
    [patchTrack, invalidate, notifyError, t],
  );
}
