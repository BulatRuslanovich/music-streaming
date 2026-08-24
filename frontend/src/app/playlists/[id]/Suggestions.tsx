// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { api } from "@/lib/api";
import { formatArtists } from "@/lib/format";
import { queries } from "@/lib/queries";
import { useInvalidate } from "@/lib/useInvalidate";
import { Section } from "@/components/collection/Section";
import { Tile } from "@/components/collection/Tile";
import { QuickRow } from "@/components/collection/Tile";
import { TrackCover } from "@/components/Cover";
import { PlusIcon } from "@/components/Icons";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";

export function Suggestions({ playlistId }: { playlistId: string }) {
  const t = useT();
  const invalidate = useInvalidate();
  const { notifyError } = useToast();

  const suggestions = useQuery(queries.playlistSuggestions(playlistId));
  const [added, setAdded] = useState<ReadonlySet<string>>(() => new Set());
  const [pending, setPending] = useState<string | null>(null);

  const items = (suggestions.data ?? []).filter((item) => !added.has(item.track.id));

  if (items.length === 0) return null;

  const add = async (trackId: string) => {
    setPending(trackId);
    try {
      await api.addToPlaylist(playlistId, trackId);
      setAdded((current) => new Set(current).add(trackId));
      invalidate("playlists");
    } catch (reason) {
      notifyError(reason, t("playlists.addFailed"));
    } finally {
      setPending(null);
    }
  };

  return (
    <Section title={t("playlists.suggestions")}>
      <QuickRow>
        {items.map((item) => (
          <Tile
            key={item.track.id}
            label={item.track.title}
            sublabel={formatArtists(item.track)}
            art={<TrackCover track={item.track} className="size-full rounded-none" />}
            disabled={pending === item.track.id}
            onClick={() => void add(item.track.id)}
            action={<PlusIcon size={18} />}
          />
        ))}
      </QuickRow>
    </Section>
  );
}
