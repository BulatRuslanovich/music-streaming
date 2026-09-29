// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { TagsIcon } from "lucide-react";
import { useSearchParams } from "next/navigation";
import { Suspense, useEffect, useRef, useState } from "react";
import { TRACK_PAGE_SIZE } from "@/lib/pageSizes";
import { queries } from "@/lib/queries";
import { smoothUnlessReduced } from "@/lib/scroll";
import { usePage } from "@/lib/usePage";
import type { Genre } from "@/lib/types";
import { AlbumMosaic } from "@/components/collection/CoverMosaic";
import { Card } from "@/components/MediaCard";
import { CardGrid, PageHeader, Section } from "@/components/PageHeader";
import { Pagination } from "@/components/PageToolbar";
import { PlayAllButton } from "@/components/PlayAllButton";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { useT } from "@/contexts/I18nContext";

export function GenresPage() {
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

  useEffect(() => {
    if (selected === null) return;

    tracksRef.current?.scrollIntoView({
      block: "start",
      behavior: smoothUnlessReduced(),
    });
  }, [selected]);

  const genres = useQuery(queries.genres());
  const tracks = useQuery(queries.genreTracks(selected, { page, pageSize: TRACK_PAGE_SIZE }));

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

      <Query result={genres} empty={{ icon: <TagsIcon size={24} />, title: t("genres.empty") }}>
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
          <Query result={tracks}>
            {(result) =>
              result === null ? null : (
                <>
                  <TrackList tracks={result.items} />
                  <Pagination result={result} onChange={setPage} />
                </>
              )
            }
          </Query>
        </Section>
      ) : (
        genres.data !== undefined &&
        genres.data.length > 0 && (
          // Подсказка строкой, а не карточкой EmptyState: крупная пустая карточка под сеткой
          // жанров читалась как ошибка, хотя ничего не случилось — жанр просто не выбран.
          <p className="text-sm text-muted-foreground">{t("genres.pickHint")}</p>
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
