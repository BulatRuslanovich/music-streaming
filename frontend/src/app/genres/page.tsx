// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { GenreIcon } from "@/components/Icons";
import { useSearchParams } from "next/navigation";
import { Suspense, useEffect, useRef, useState } from "react";
import { useReducedMotion } from "motion/react";
import { queries } from "@/lib/queries";
import { usePage } from "@/lib/usePage";
import type { Genre } from "@/lib/types";
import { AlbumMosaic } from "@/components/collection/CoverMosaic";
import { Section } from "@/components/collection/Section";
import { EmptyState } from "@/components/EmptyState";
import { Card } from "@/components/MediaCard";
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

  const initial = useSearchParams().get("id");
  const [selected, setSelected] = useState<string | null>(initial);
  const [page, setPage] = usePage([selected]);

  // Сетка жанров не пагинируется и бывает на сотню карточек, а треки выбранного жанра
  // рендерятся под ней — без этого до них надо прокрутить весь каталог.
  const tracksRef = useRef<HTMLElement>(null);
  const reduceMotion = useReducedMotion();

  useEffect(() => {
    if (selected === null) return;

    tracksRef.current?.scrollIntoView({
      block: "start",
      behavior: reduceMotion ? "auto" : "smooth",
    });
  }, [selected, reduceMotion]);

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

      <Query
        result={genres}
        skeletonCount={8}
        empty={{ icon: <GenreIcon size={24} />, title: t("genres.empty") }}
      >
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
        <Section title={selectedGenre.name} ref={tracksRef}>
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
        genres.data.length > 0 && (
          <EmptyState icon={<GenreIcon size={24} />} title={t("genres.pickHint")} />
        )
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
    <Card
      onClick={onSelect}
      active={active}
      current={active}
      title={genre.name}
      subtitle={t("count.tracks", { count: genre.trackCount })}
      cover={<AlbumMosaic albumIds={genre.coverAlbumIds} name={genre.name} />}
    />
  );
}
