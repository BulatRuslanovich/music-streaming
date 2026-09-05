// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback, useEffect, useRef } from "react";
import { useReducedMotion } from "motion/react";
import { TRACK_PAGE_SIZE } from "@/lib/pageSizes";
import { queries } from "@/lib/queries";
import { tagLabel } from "@/lib/tagLabel";
import { usePage } from "@/lib/usePage";
import type { Tag } from "@/lib/types";
import { AlbumMosaic } from "@/components/collection/CoverMosaic";
import { Section } from "@/components/collection/Section";
import { EmptyState } from "@/components/EmptyState";
import { TagIcon } from "@/components/Icons";
import { ArtistCard, Card } from "@/components/MediaCard";
import { CardGrid, PageHeader } from "@/components/PageHeader";
import { Pagination } from "@/components/PageToolbar";
import { PlayAllButton } from "@/components/PlayAllButton";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { useT } from "@/contexts/I18nContext";

export function TagsPage() {
  const t = useT();

  return (
    <Suspense fallback={<PageHeader title={t("nav.tags")} />}>
      <TagsView />
    </Suspense>
  );
}

function TagsView() {
  const t = useT();
  const router = useRouter();

  // Выбранный тег живёт в адресе, а не в состоянии компонента: сюда приходят по ссылке с чипа —
  // с оборота обложки и со страницы исполнителя, — и такой переход обязан менять выбор.
  const selected = useSearchParams().get("name");
  const [page, setPage] = usePage([selected]);

  const select = useCallback(
    (name: string | null) =>
      router.replace(name === null ? "/tags" : `/tags?name=${encodeURIComponent(name)}`, {
        scroll: false,
      }),
    [router],
  );

  // Сетка тегов не пагинируется и бывает на шесть десятков карточек, а выбранное показывается
  // под ней — без прокрутки до него надо пролистать весь список.
  const chosenRef = useRef<HTMLDivElement>(null);
  const reduceMotion = useReducedMotion();

  useEffect(() => {
    if (selected === null) return;

    chosenRef.current?.scrollIntoView({
      block: "start",
      behavior: reduceMotion ? "auto" : "smooth",
    });
  }, [selected, reduceMotion]);

  const tags = useQuery(queries.tags());
  const tracks = useQuery(queries.tagTracks(selected, { page, pageSize: TRACK_PAGE_SIZE }));
  const artists = useQuery(queries.tagArtists(selected));

  return (
    <>
      <PageHeader
        title={t("nav.tags")}
        subtitle={tags.data ? t("count.tags", { count: tags.data.length }) : undefined}
        actions={
          tracks.data && tracks.data.items.length > 0 ? (
            <PlayAllButton tracks={tracks.data.items} name={selected ? tagLabel(selected) : ""} />
          ) : undefined
        }
      />

      <Query
        result={tags}
        skeletonCount={8}
        empty={{ icon: <TagIcon size={24} />, title: t("tags.empty") }}
      >
        {(list) => (
          <CardGrid>
            {list.map((tag) => (
              <TagCard
                key={tag.name}
                tag={tag}
                active={selected === tag.name}
                onSelect={() => select(selected === tag.name ? null : tag.name)}
              />
            ))}
          </CardGrid>
        )}
      </Query>

      {selected !== null ? (
        <div ref={chosenRef} className="flex flex-col gap-8">
          {artists.data && artists.data.length > 0 && (
            <Section title={t("tags.artists")}>
              <CardGrid>
                {artists.data.map((artist) => (
                  <ArtistCard key={artist.id} artist={artist} />
                ))}
              </CardGrid>
            </Section>
          )}

          <Section title={tagLabel(selected)}>
            {/* По чипу можно прийти на тег, которого нет в сетке: у него меньше трёх треков
                или он остался в старой ссылке. */}
            <Query
              result={tracks}
              skeleton="row"
              skeletonCount={6}
              empty={{ icon: <TagIcon size={24} />, title: t("tags.nothing") }}
            >
              {(result) =>
                result === null ? null : (
                  <>
                    <TrackList tracks={result.items} origin={{ source: "tag" }} />
                    <Pagination result={result} onChange={setPage} />
                  </>
                )
              }
            </Query>
          </Section>
        </div>
      ) : (
        tags.data !== undefined &&
        tags.data.length > 0 && (
          <EmptyState icon={<TagIcon size={24} />} title={t("tags.pickHint")} />
        )
      )}
    </>
  );
}

function TagCard({ tag, active, onSelect }: { tag: Tag; active: boolean; onSelect: () => void }) {
  const t = useT();
  const label = tagLabel(tag.name);

  return (
    <Card
      onClick={onSelect}
      active={active}
      current={active}
      title={label}
      subtitle={t("count.tracks", { count: tag.trackCount })}
      cover={<AlbumMosaic albumIds={tag.coverAlbumIds} name={label} />}
    />
  );
}
