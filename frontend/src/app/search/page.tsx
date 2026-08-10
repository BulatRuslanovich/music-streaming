"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { SearchIcon } from "@/components/Icons";
import { TrackList } from "@/components/TrackList";
import {
  AlbumCard,
  ArtistCard,
  LoadError,
  PageHeader,
  SectionHeader,
  Skeleton,
} from "@/components/ui";

export default function SearchPage() {
  // useSearchParams needs a Suspense boundary, since it makes the page depend on the URL.
  return (
    <Suspense fallback={<PageHeader title="Search" />}>
      <SearchView />
    </Suspense>
  );
}

function SearchView() {
  const searchParams = useSearchParams();
  const router = useRouter();

  const initialQuery = searchParams.get("q") ?? "";
  const [input, setInput] = useState(initialQuery);
  const [query, setQuery] = useState(initialQuery);

  // Debounce so typing does not fire a request per keystroke.
  useEffect(() => {
    const timer = window.setTimeout(() => setQuery(input.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [input]);

  // Keep the URL shareable and the back button meaningful, without adding a history entry
  // for every intermediate keystroke.
  useEffect(() => {
    const current = searchParams.get("q") ?? "";
    if (query !== current) {
      router.replace(query ? `/search?q=${encodeURIComponent(query)}` : "/search");
    }
  }, [query, router, searchParams]);

  const { data, error, loading, reload } = useApi(
    () => (query ? api.search(query, 25) : Promise.resolve(null)),
    [query],
  );

  const isEmpty =
    data !== null &&
    data !== undefined &&
    data.artists.length === 0 &&
    data.albums.length === 0 &&
    data.tracks.length === 0 &&
    data.genres.length === 0;

  return (
    <>
      <PageHeader title="Search" />

      <div className="search-field">
        <SearchIcon size={18} />
        <label htmlFor="search-input" className="sr-only">
          Search your library
        </label>
        <input
          id="search-input"
          type="search"
          placeholder="Tracks, albums, artists, genres…"
          value={input}
          autoFocus
          autoComplete="off"
          onChange={(event) => setInput(event.target.value)}
        />
      </div>

      {!query && (
        <p className="empty-state">Start typing to search across your whole library.</p>
      )}

      {error && <LoadError message={error} onRetry={reload} />}
      {query && loading && !data && <Skeleton count={6} />}

      {query && isEmpty && !loading && (
        <p className="empty-state">Nothing matched “{query}”.</p>
      )}

      {data && !isEmpty && (
        <>
          {data.tracks.length > 0 && (
            <section>
              <SectionHeader title="Tracks" />
              <TrackList tracks={data.tracks} onChanged={reload} />
            </section>
          )}

          {data.albums.length > 0 && (
            <section>
              <SectionHeader title="Albums" />
              <div className="card-grid">
                {data.albums.map((album) => (
                  <AlbumCard key={album.id} album={album} />
                ))}
              </div>
            </section>
          )}

          {data.artists.length > 0 && (
            <section>
              <SectionHeader title="Artists" />
              <div className="card-grid">
                {data.artists.map((artist) => (
                  <ArtistCard key={artist.id} artist={artist} />
                ))}
              </div>
            </section>
          )}

          {data.genres.length > 0 && (
            <section>
              <SectionHeader title="Genres" />
              <div className="chip-row">
                {data.genres.map((genre) => (
                  <span key={genre.id} className="chip">
                    {genre.name}
                    <span className="chip-count">{genre.trackCount}</span>
                  </span>
                ))}
              </div>
            </section>
          )}
        </>
      )}
    </>
  );
}
