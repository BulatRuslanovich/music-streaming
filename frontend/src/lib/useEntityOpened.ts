"use client";

import { useEffect } from "react";
import { recordEvent, type PlaybackEventType } from "@/lib/events";

type OpenedEvent = Extract<
  PlaybackEventType,
  "artistOpened" | "albumOpened" | "playlistOpened"
>;

/**
 * Сообщает, что открыли страницу сущности.
 *
 * Просмотр — слабый сигнал, куда слабее прослушивания, но это единственное, что слушатель оставляет
 * за собой, пока решает, что включить, и именно он отличает исполнителя, к которому возвращаются,
 * от того, чей трек просто оказался в очереди.
 */
export function useEntityOpened(type: OpenedEvent, entityId: string | undefined) {
  useEffect(() => {
    if (!entityId) return;

    recordEvent({ type, entityId });
  }, [type, entityId]);
}
