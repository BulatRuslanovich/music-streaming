"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { usePagedApi } from "@/lib/usePagedApi";
import { TrackList } from "@/components/TrackList";
import { LoadError, PageHeader, Pagination, PlayAllButton, Skeleton } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 100;

export default function GenresPage() {
  const t = useT();
  const [selected, setSelected] = useState<string | null>(null);

  const genres = useApi(() => api.genres(), [], "genres");
  const tracks = usePagedApi(
    (page) => (selected ? api.genreTracks(selected, { page, pageSize: PAGE_SIZE }) : Promise.resolve(null)),
    [selected],
    "genreTracks",
  );

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

      {genres.error && <LoadError message={genres.error} onRetry={genres.reload} />}
      {genres.loading && !genres.data && <Skeleton count={8} />}

      {genres.data && genres.data.length === 0 && (
        <p className="empty-state">{t("genres.empty")}</p>
      )}

      {genres.data && genres.data.length > 0 && (
        <div className="chip-row">
          {genres.data.map((genre) => (
            <button
              key={genre.id}
              type="button"
              className={`chip ${selected === genre.id ? "is-active" : ""}`}
              onClick={() => setSelected(selected === genre.id ? null : genre.id)}
              aria-pressed={selected === genre.id}
            >
              {genre.name}
              <span className="chip-count">{genre.trackCount}</span>
            </button>
          ))}
        </div>
      )}

      {selectedGenre && (
        <section>
          <h2 className="section-title">{selectedGenre.name}</h2>
          {tracks.loading && !tracks.data && <Skeleton variant="row" count={6} />}
          {tracks.error && <LoadError message={tracks.error} onRetry={tracks.reload} />}
          {tracks.data && (
            <>
              <TrackList
                tracks={tracks.data.items}
                onChanged={tracks.reload}
                origin={{ source: "genre", sourceId: selected ?? undefined }}
              />
              <Pagination result={tracks.data} onChange={tracks.setPage} />
            </>
          )}
        </section>
      )}
    </>
  );
}
