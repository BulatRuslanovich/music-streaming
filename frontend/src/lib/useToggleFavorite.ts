// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMutation } from "@tanstack/react-query";
import { useCallback } from "react";
import { api } from "@/lib/api";
import { recordEvent } from "@/lib/events";
import { useInvalidate } from "@/lib/useInvalidate";
import { usePlayerActions } from "@/contexts/PlayerContext";

interface Toggle {
  track: { id: string; isFavorite: boolean };
  onLocal?: (next: boolean) => void;
}

/**
 * Оптимистичный лайк: сначала правим то, что видно, потом ходим на сервер и откатываемся,
 * если он отказал. Пока эти правила жили копиями в каждом компоненте, копии разошлись —
 * только одна из них инвалидировала избранное.
 *
 * `patchTrack` чинит очередь: тот же трек может быть и в списке, и в плеере одновременно.
 * `onLocal` — для списков, которые ведут собственный словарь лайков поверх данных запроса.
 */
export function useToggleFavorite() {
  const { patchTrack } = usePlayerActions();
  const invalidate = useInvalidate();

  const show = ({ track, onLocal }: Toggle, favorite: boolean) => {
    onLocal?.(favorite);
    patchTrack(track.id, { isFavorite: favorite });
  };

  const { mutate } = useMutation({
    mutationFn: ({ track }: Toggle) =>
      track.isFavorite ? api.removeFavorite(track.id) : api.addFavorite(track.id),
    onMutate: (toggle) => show(toggle, !toggle.track.isFavorite),
    onError: (_, toggle) => show(toggle, toggle.track.isFavorite),
    onSuccess: (_, { track }) => {
      recordEvent({ type: track.isFavorite ? "trackUnliked" : "trackLiked", trackId: track.id });
      invalidate("favorites");
    },
  });

  return useCallback(
    (track: Toggle["track"], onLocal?: Toggle["onLocal"]) => mutate({ track, onLocal }),
    [mutate],
  );
}
