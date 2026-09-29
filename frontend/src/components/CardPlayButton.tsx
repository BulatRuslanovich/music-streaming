// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMutation } from "@tanstack/react-query";
import type { Track } from "@/lib/types";
import { usePlayback } from "@/lib/playback/usePlayback";
import { useT } from "@/contexts/I18nContext";
import { PlayBadge } from "./PlayBadge";

/**
 * Кнопка запуска поверх обложки карточки-ссылки. Треки подтягиваются по клику: к этому
 * моменту тот же запрос обычно уже лежит в кэше после префетча по наведению.
 */
export function CardPlayButton({
  name,
  playing,
  load,
}: {
  name: string;
  playing: boolean;
  load: () => Promise<Track[]>;
}) {
  const t = useT();
  const { playSet } = usePlayback();
  // Треки известны только после загрузки, поэтому решение «пауза или play» принимает
  // playSet уже с ними на руках — то же правило, что и у кнопки на странице альбома.
  const play = useMutation({ mutationFn: load, onSuccess: (tracks) => playSet(tracks) });

  return (
    <button
      type="button"
      onClick={() => play.mutate()}
      disabled={play.isPending}
      aria-label={playing ? t("action.pause") : t("action.playNamed", { name })}
      className="pointer-events-auto absolute right-2.5 bottom-2.5 rounded-full"
    >
      {/* Карточка-ссылка, и это единственная кнопка запуска на ней. */}
      <PlayBadge playing={playing} visible={playing} standalone />
    </button>
  );
}
