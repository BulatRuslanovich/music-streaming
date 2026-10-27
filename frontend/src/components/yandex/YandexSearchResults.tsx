// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { PlusIcon, SearchIcon } from "lucide-react";
import { queries } from "@/lib/queries";
import { formatDuration } from "@/lib/format";
import { cn } from "@/lib/cn";
import type { YandexAlbum, YandexTrack } from "@/lib/types";
import { CardGrid, Section } from "@/components/PageHeader";
import { Query } from "@/components/Query";
import { Button } from "@/components/ui/button";
import { useT } from "@/contexts/I18nContext";
import { useStartYandexImport } from "./useStartYandexImport";

export function YandexSearchResults({ query }: { query: string }) {
  const t = useT();
  const result = useQuery(queries.yandexSearch(query));
  const start = useStartYandexImport();

  const [queued, setQueued] = useState<ReadonlySet<string>>(new Set());

  const add = (kind: "Track" | "Album", id: string) =>
    start.mutate(
      { kind, id },
      { onSuccess: () => setQueued((previous) => new Set(previous).add(`${kind}:${id}`)) },
    );

  return (
    <Query
      result={result}
      isEmpty={(data) => data.tracks.length === 0 && data.albums.length === 0}
      empty={{ icon: <SearchIcon size={24} />, title: t("search.nothingFound") }}
    >
      {(data) => (
        <>
          {data.tracks.length > 0 && (
            <Section title={t("nav.tracks")}>
              <ul className="flex flex-col">
                {data.tracks.map((track) => (
                  <TrackRow
                    key={track.id}
                    track={track}
                    queued={queued.has(`Track:${track.id}`)}
                    onAdd={() => add("Track", track.id)}
                  />
                ))}
              </ul>
            </Section>
          )}

          {data.albums.length > 0 && (
            <Section title={t("nav.albums")}>
              <CardGrid>
                {data.albums.map((album) => (
                  <AlbumCard
                    key={album.id}
                    album={album}
                    queued={queued.has(`Album:${album.id}`)}
                    onAdd={() => add("Album", album.id)}
                  />
                ))}
              </CardGrid>
            </Section>
          )}
        </>
      )}
    </Query>
  );
}

function Artwork({ url, className }: { url?: string | null; className?: string }) {
  return url ? (
    // eslint-disable-next-line @next/next/no-img-element
    <img src={url} alt="" loading="lazy" className={cn("bg-raised object-cover", className)} />
  ) : (
    <span className={cn("bg-raised", className)} />
  );
}

function TrackRow({
  track,
  queued,
  onAdd,
}: {
  track: YandexTrack;
  queued: boolean;
  onAdd: () => void;
}) {
  const t = useT();
  const note = track.inLibrary
    ? t("yandex.inLibrary")
    : !track.available
      ? t("yandex.unavailable")
      : queued
        ? t("yandex.queued")
        : null;

  return (
    <li className="flex min-h-14 items-center gap-3 rounded-md px-3 py-2 text-sm hover:bg-raised">
      <Artwork url={track.coverUrl} className="size-10 shrink-0 rounded-sm" />

      <span className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className={cn("truncate font-medium", !track.available && "text-muted-foreground")}>
          {track.title}
        </span>
        <span className="truncate text-muted-foreground">
          {track.album ? `${track.artist} · ${track.album}` : track.artist}
        </span>
      </span>

      <span className="shrink-0 text-muted-foreground tabular-nums max-md:hidden">
        {formatDuration(track.durationSeconds)}
      </span>

      {note ? (
        <span className="w-28 shrink-0 text-right text-xs text-muted-foreground">{note}</span>
      ) : (
        <span className="flex w-28 shrink-0 justify-end">
          <Button
            variant="ghost"
            size="icon-sm"
            onClick={onAdd}
            aria-label={t("yandex.addNamed", { name: track.title })}
          >
            <PlusIcon size={16} />
          </Button>
        </span>
      )}
    </li>
  );
}

function AlbumCard({
  album,
  queued,
  onAdd,
}: {
  album: YandexAlbum;
  queued: boolean;
  onAdd: () => void;
}) {
  const t = useT();

  return (
    <div className="flex min-w-0 flex-col gap-2">
      <Artwork url={album.coverUrl} className="aspect-square w-full rounded-md" />

      <div className="flex min-w-0 flex-col">
        <span className="truncate font-medium">{album.title}</span>
        <span className="truncate text-sm text-muted-foreground">
          {album.year ? `${album.artist} · ${album.year}` : album.artist}
        </span>
      </div>

      <Button variant="outline" size="sm" className="self-start" disabled={queued} onClick={onAdd}>
        {queued ? t("yandex.queued") : t("yandex.addAlbum")}
      </Button>
    </div>
  );
}
