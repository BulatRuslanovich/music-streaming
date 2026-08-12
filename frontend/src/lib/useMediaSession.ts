"use client";

import { useEffect } from "react";
import { mediaUrl } from "@/lib/media";
import { formatArtists } from "@/lib/format";
import type { Track } from "@/lib/types";

interface MediaSessionControls {
  play: () => void;
  pause: () => void;
  next: () => void;
  previous: () => void;
}

export function useMediaSession(
  track: Track | null,
  isPlaying: boolean,
  controls: MediaSessionControls,
) {
  const { play, pause, next, previous } = controls;

  useEffect(() => {
    if (!("mediaSession" in navigator) || !track) return;

    navigator.mediaSession.metadata = new MediaMetadata({
      title: track.title,
      artist: formatArtists(track),
      album: track.albumTitle ?? undefined,
      artwork: track.hasCover ? [{ src: mediaUrl.trackCover(track.id), sizes: "640x640" }] : undefined,
    });

    navigator.mediaSession.playbackState = isPlaying ? "playing" : "paused";

    const handlers: [MediaSessionAction, () => void][] = [
      ["play", play],
      ["pause", pause],
      ["previoustrack", previous],
      ["nexttrack", next],
    ];

    for (const [action, handler] of handlers) {
      try {
        navigator.mediaSession.setActionHandler(action, handler);
      } catch {}
    }

    return () => {
      for (const [action] of handlers) {
        try {
          navigator.mediaSession.setActionHandler(action, null);
        } catch {}
      }
    };
  }, [track, isPlaying, play, pause, next, previous]);
}
