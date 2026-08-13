"use client";

import { useEffect } from "react";
import { recordEvent, type PlaybackEventType } from "@/lib/events";

type OpenedEvent = Extract<PlaybackEventType, "artistOpened" | "albumOpened" | "playlistOpened">;

export function useEntityOpened(type: OpenedEvent, entityId: string | undefined) {
  useEffect(() => {
    if (!entityId) return;

    recordEvent({ type, entityId });
  }, [type, entityId]);
}
