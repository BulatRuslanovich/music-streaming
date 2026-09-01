// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useState } from "react";
import type { Track } from "@/lib/types";
import { usePlayback } from "@/lib/usePlayback";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
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
  const { notifyError } = useToast();
  const [busy, setBusy] = useState(false);

  const play = async () => {
    if (busy) return;

    setBusy(true);
    try {
      // Треки известны только после загрузки, поэтому решение «пауза или play» принимает
      // playSet уже с ними на руках — то же правило, что и у кнопки на странице альбома.
      playSet(await load());
    } catch (error) {
      notifyError(error, t("error.load"));
    } finally {
      setBusy(false);
    }
  };

  return (
    <button
      type="button"
      onClick={() => void play()}
      disabled={busy}
      aria-label={playing ? t("action.pause") : t("action.playNamed", { name })}
      className="pointer-events-auto absolute right-2 bottom-2 rounded-full"
    >
      {/* Карточка-ссылка, и это единственная кнопка запуска на ней. */}
      <PlayBadge playing={playing} visible={playing} standalone />
    </button>
  );
}
