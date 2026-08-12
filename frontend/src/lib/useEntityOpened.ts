"use client";

import { useEffect } from "react";
import { recordEvent, type PlaybackEventType } from "@/lib/events";

type OpenedEvent = Extract<
  PlaybackEventType,
  "artistOpened" | "albumOpened" | "playlistOpened"
>;

/**
 * Reports that a detail page was opened.
 *
 * Browsing is a weak signal — far weaker than listening — but it is the only one a listener leaves
 * while deciding what to play, and it is what distinguishes an artist they keep coming back to
 * from one whose track happened to be in a queue.
 */
export function useEntityOpened(type: OpenedEvent, entityId: string | undefined) {
  useEffect(() => {
    if (!entityId) return;

    recordEvent({ type, entityId });
  }, [type, entityId]);
}
