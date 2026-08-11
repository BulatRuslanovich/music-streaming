"use client";

import { useState } from "react";
import { useParams } from "next/navigation";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { Cover } from "@/components/Cover";
import { TrackList } from "@/components/TrackList";
import {
  AlbumCard,
  LoadError,
  Pagination,
  PlayAllButton,
  SectionHeader,
  Skeleton,
} from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 100;

export default function ArtistPage() {
  const t = useT();

  const params = useParams<{ id: string }>();
  const id = params.id;

  const [page, setPage] = useState(1);

  const { data, error, loading, reload } = useApi(
    () => api.artist(id, { page, pageSize: PAGE_SIZE }),
    [id, page],
    "artist",
  );

  if (error) return <LoadError message={error} onRetry={reload} />;
  if (loading && !data) return <Skeleton variant="row" count={8} />;
  if (!data) return null;

  return (
    <>
      <header className="detail-header">
        <div className="detail-art detail-art-round">
          <Cover artistId={data.id} hasCover={data.hasImage} name={data.name} rounded />
        </div>

        <div className="detail-meta">
          <span className="detail-kind">{t("artists.kind")}</span>
          <h1>{data.name}</h1>
          <p className="detail-facts">
            {t("count.albums", { count: data.albums.length })} ·{" "}
            {t("count.tracks", { count: data.tracks.total })}
          </p>

          <div className="detail-actions">
            <PlayAllButton tracks={data.tracks.items} name={data.name} />
          </div>
        </div>
      </header>

      {data.albums.length > 0 && (
        <section>
          <SectionHeader title={t("nav.albums")} />
          <div className="card-grid">
            {data.albums.map((album) => (
              <AlbumCard key={album.id} album={album} />
            ))}
          </div>
        </section>
      )}

      <section>
        <SectionHeader title={t("nav.tracks")} />
        <TrackList tracks={data.tracks.items} showArtist={false} onChanged={reload} />
        <Pagination
          page={data.tracks.page}
          totalPages={data.tracks.totalPages}
          onChange={setPage}
        />
      </section>
    </>
  );
}
