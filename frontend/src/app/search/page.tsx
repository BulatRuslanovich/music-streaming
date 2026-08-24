// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback, useEffect, useSyncExternalStore } from "react";
import { queries, type SearchTab } from "@/lib/queries";
import {
  clearRecentSearches,
  getRecentSearches,
  getServerRecentSearches,
  rememberSearch,
  subscribeToRecentSearches,
} from "@/lib/recentSearches";
import { usePage } from "@/lib/usePage";
import { Section } from "@/components/collection/Section";
import { AlbumCard, ArtistCard } from "@/components/MediaCard";
import { CardGrid, PageHeader } from "@/components/PageHeader";
import { Pagination, PageToolbar } from "@/components/PageToolbar";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { EmptyState } from "@/components/EmptyState";
import { Button } from "@/components/ui/button";
import { ToggleGroup, ToggleGroupButton } from "@/components/ui/tabs";
import { useT } from "@/contexts/I18nContext";
import { GenreChips } from "./GenreChips";
import { TopResult } from "./TopResult";

const PAGE_SIZE = 50;

const PREVIEW = 5;

const TABS: SearchTab[] = ["tracks", "albums", "artists", "genres"];

const TAB_LABELS = {
  tracks: "nav.tracks",
  albums: "nav.albums",
  artists: "nav.artists",
  genres: "nav.genres",
} as const;

export default function SearchPage() {
  const t = useT();

  return (
    <Suspense fallback={<PageHeader title={t("nav.search")} />}>
      <SearchView />
    </Suspense>
  );
}

function isTab(value: string | null): value is SearchTab {
  return value !== null && (TABS as string[]).includes(value);
}

function SearchView() {
  const t = useT();
  const router = useRouter();
  const params = useSearchParams();

  const query = (params.get("q") ?? "").trim();
  const tabParam = params.get("tab");
  const tab = isTab(tabParam) ? tabParam : null;

  const recent = useSyncExternalStore(
    subscribeToRecentSearches,
    getRecentSearches,
    getServerRecentSearches,
  );

  useEffect(() => {
    if (query.length === 0) return;

    const timer = setTimeout(() => rememberSearch(query), 1200);
    return () => clearTimeout(timer);
  }, [query]);

  const navigate = useCallback(
    (next: string, nextTab: SearchTab | null) => {
      if (!next) {
        router.replace("/search");
        return;
      }

      const search = new URLSearchParams({ q: next });
      if (nextTab) search.set("tab", nextTab);

      router.replace(`/search?${search}`);
    },
    [router],
  );

  const results = useQuery(queries.search(query));

  return (
    <>
      <PageHeader title={t("nav.search")} />

      <PageToolbar
        search={query}
        onSearch={(next) => navigate(next, tab)}
        placeholder={t("search.placeholder")}
      />

      {!query ? (
        <RecentSearches
          recent={recent}
          onPick={(value) => navigate(value, null)}
          onClear={clearRecentSearches}
        />
      ) : (
        <>
          <ToggleGroup aria-label={t("search.tabs")}>
            <ToggleGroupButton active={tab === null} onClick={() => navigate(query, null)}>
              {t("search.tab.all")}
            </ToggleGroupButton>
            {TABS.map((value) => (
              <ToggleGroupButton
                key={value}
                active={tab === value}
                onClick={() => navigate(query, value)}
              >
                {t(TAB_LABELS[value])}
              </ToggleGroupButton>
            ))}
          </ToggleGroup>

          {tab === null ? (
            <Query
              result={results}
              skeletonCount={6}
              isEmpty={(data) =>
                data.artists.length === 0 &&
                data.albums.length === 0 &&
                data.tracks.length === 0 &&
                data.genres.length === 0
              }
              empty={{ title: t("search.nothingFound") }}
            >
              {(data) => (
                <>
                  {data.top && <TopResult top={data.top} />}

                  {data.tracks.length > 0 && (
                    <Section
                      title={t("nav.tracks")}
                      href={seeAll(query, "tracks", data.tracks.length)}
                    >
                      <TrackList
                        tracks={data.tracks.slice(0, PREVIEW)}
                        origin={{ source: "search" }}
                      />
                    </Section>
                  )}

                  {data.albums.length > 0 && (
                    <Section
                      title={t("nav.albums")}
                      href={seeAll(query, "albums", data.albums.length)}
                    >
                      <CardGrid>
                        {data.albums.slice(0, PREVIEW).map((album) => (
                          <AlbumCard key={album.id} album={album} />
                        ))}
                      </CardGrid>
                    </Section>
                  )}

                  {data.artists.length > 0 && (
                    <Section
                      title={t("nav.artists")}
                      href={seeAll(query, "artists", data.artists.length)}
                    >
                      <CardGrid>
                        {data.artists.slice(0, PREVIEW).map((artist) => (
                          <ArtistCard key={artist.id} artist={artist} />
                        ))}
                      </CardGrid>
                    </Section>
                  )}

                  {data.genres.length > 0 && (
                    <Section
                      title={t("nav.genres")}
                      href={seeAll(query, "genres", data.genres.length)}
                    >
                      <GenreChips genres={data.genres.slice(0, PREVIEW * 2)} />
                    </Section>
                  )}
                </>
              )}
            </Query>
          ) : (
            <TabResults tab={tab} query={query} />
          )}
        </>
      )}
    </>
  );
}

function seeAll(query: string, tab: SearchTab, shown: number): string | undefined {
  return shown > PREVIEW ? `/search?q=${encodeURIComponent(query)}&tab=${tab}` : undefined;
}

function TabResults({ tab, query }: { tab: SearchTab; query: string }) {
  if (tab === "tracks") return <TracksTab query={query} />;
  if (tab === "albums") return <AlbumsTab query={query} />;
  if (tab === "artists") return <ArtistsTab query={query} />;

  return <GenresTab query={query} />;
}

function useTabPage(tab: SearchTab, query: string) {
  return usePage([tab, query]);
}

function TracksTab({ query }: { query: string }) {
  const t = useT();
  const [page, setPage] = useTabPage("tracks", query);
  const result = useQuery(queries.searchTab("tracks", query, { page, pageSize: PAGE_SIZE }));

  return (
    <Query
      result={result}
      skeleton="row"
      skeletonCount={12}
      empty={{ title: t("search.nothingFound") }}
    >
      {(data) => (
        <>
          <TrackList tracks={data.items} origin={{ source: "search" }} />
          <Pagination result={data} onChange={setPage} />
        </>
      )}
    </Query>
  );
}

function AlbumsTab({ query }: { query: string }) {
  const t = useT();
  const [page, setPage] = useTabPage("albums", query);
  const result = useQuery(queries.searchTab("albums", query, { page, pageSize: PAGE_SIZE }));

  return (
    <Query result={result} skeletonCount={12} empty={{ title: t("search.nothingFound") }}>
      {(data) => (
        <>
          <CardGrid>
            {data.items.map((album) => (
              <AlbumCard key={album.id} album={album} />
            ))}
          </CardGrid>
          <Pagination result={data} onChange={setPage} />
        </>
      )}
    </Query>
  );
}

function ArtistsTab({ query }: { query: string }) {
  const t = useT();
  const [page, setPage] = useTabPage("artists", query);
  const result = useQuery(queries.searchTab("artists", query, { page, pageSize: PAGE_SIZE }));

  return (
    <Query result={result} skeletonCount={12} empty={{ title: t("search.nothingFound") }}>
      {(data) => (
        <>
          <CardGrid>
            {data.items.map((artist) => (
              <ArtistCard key={artist.id} artist={artist} />
            ))}
          </CardGrid>
          <Pagination result={data} onChange={setPage} />
        </>
      )}
    </Query>
  );
}

function GenresTab({ query }: { query: string }) {
  const t = useT();
  const [page, setPage] = useTabPage("genres", query);
  const result = useQuery(queries.searchTab("genres", query, { page, pageSize: PAGE_SIZE }));

  return (
    <Query result={result} skeletonCount={12} empty={{ title: t("search.nothingFound") }}>
      {(data) => (
        <>
          <GenreChips genres={data.items} />
          <Pagination result={data} onChange={setPage} />
        </>
      )}
    </Query>
  );
}

function RecentSearches({
  recent,
  onPick,
  onClear,
}: {
  recent: string[];
  onPick: (value: string) => void;
  onClear: () => void;
}) {
  const t = useT();

  if (recent.length === 0) return <EmptyState title={t("search.hint")} />;

  return (
    <Section
      title={t("search.recent")}
      actions={
        <Button variant="text" size="auto" onClick={onClear}>
          {t("search.clearRecent")}
        </Button>
      }
    >
      <div className="flex flex-wrap gap-2.5">
        {recent.map((value) => (
          <Button key={value} variant="outline" size="sm" onClick={() => onPick(value)}>
            {value}
          </Button>
        ))}
      </div>
    </Section>
  );
}
