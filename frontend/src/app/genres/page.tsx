// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { cn } from "@/lib/cn";
import { queries } from "@/lib/queries";
import { usePage } from "@/lib/usePage";
import type { Genre } from "@/lib/types";
import { AlbumMosaic } from "@/components/collection/CoverMosaic";
import { Section } from "@/components/collection/Section";
import { EmptyState } from "@/components/EmptyState";
import { CardGrid, PageHeader } from "@/components/PageHeader";
import { Pagination } from "@/components/PageToolbar";
import { PlayAllButton } from "@/components/PlayAllButton";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 100;

export default function GenresPage() {
  const t = useT();

  return (
    <Suspense fallback={<PageHeader title={t("nav.genres")} />}>
      <GenresView />
    </Suspense>
  );
}

function GenresView() {
  const t = useT();

  // Поиск и «лучшее совпадение» ссылаются сюда как `/genres?id=…`, поэтому выбранный жанр может
  // приехать из адреса. Дальше им управляет сама страница — обратно в URL он не пишется.
  const initial = useSearchParams().get("id");
  const [selected, setSelected] = useState<string | null>(initial);
  const [page, setPage] = usePage([selected]);

  const genres = useQuery(queries.genres());
  const tracks = useQuery(queries.genreTracks(selected, { page, pageSize: PAGE_SIZE }));

  const selectedGenre = genres.data?.find((genre) => genre.id === selected) ?? null;

  return (
    <>
      <PageHeader
        title={t("nav.genres")}
        subtitle={genres.data ? t("count.genres", { count: genres.data.length }) : undefined}
        actions={
          tracks.data && tracks.data.items.length > 0 ? (
            <PlayAllButton tracks={tracks.data.items} />
          ) : undefined
        }
      />

      <Query result={genres} skeletonCount={8} empty={{ title: t("genres.empty") }}>
        {(list) => (
          <CardGrid>
            {list.map((genre) => (
              <GenreCard
                key={genre.id}
                genre={genre}
                active={selected === genre.id}
                onSelect={() => setSelected(selected === genre.id ? null : genre.id)}
              />
            ))}
          </CardGrid>
        )}
      </Query>

      {selectedGenre ? (
        <Section title={selectedGenre.name}>
          <Query result={tracks} skeleton="row" skeletonCount={6}>
            {(result) =>
              result === null ? null : (
                <>
                  <TrackList
                    tracks={result.items}
                    origin={{ source: "genre", sourceId: selectedGenre.id }}
                  />
                  <Pagination result={result} onChange={setPage} />
                </>
              )
            }
          </Query>
        </Section>
      ) : (
        genres.data !== undefined &&
        genres.data.length > 0 && <EmptyState title={t("genres.pickHint")} />
      )}
    </>
  );
}

function GenreCard({
  genre,
  active,
  onSelect,
}: {
  genre: Genre;
  active: boolean;
  onSelect: () => void;
}) {
  const t = useT();

  return (
    <button
      type="button"
      onClick={onSelect}
      aria-pressed={active}
      className={cn(
        "group flex min-w-0 flex-col gap-1 rounded-xl border border-transparent p-3 text-left",
        "transition-[background-color,border-color] duration-150 ease-brand",
        active ? "border-primary bg-primary-soft" : "bg-card hover:border-border hover:bg-raised",
      )}
    >
      <span className="relative mb-2 aspect-square w-full overflow-hidden rounded-md bg-raised shadow-art">
        <AlbumMosaic albumIds={genre.coverAlbumIds} name={genre.name} />
      </span>
      <span className={cn("truncate font-semibold", active && "text-primary")}>{genre.name}</span>
      <span className="truncate text-sm text-muted-foreground">
        {t("count.tracks", { count: genre.trackCount })}
      </span>
    </button>
  );
}
