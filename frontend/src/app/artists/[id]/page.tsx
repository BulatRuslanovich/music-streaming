"use client";

import { useState } from "react";
import { useParams } from "next/navigation";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { useEntityOpened } from "@/lib/useEntityOpened";
import { Cover } from "@/components/Cover";
import { EditArtistDialog } from "@/components/EditArtistDialog";
import { EditIcon } from "@/components/Icons";
import { TrackList } from "@/components/TrackList";
import {
  AlbumCard,
  LoadError,
  Pagination,
  PlayAllButton,
  SectionHeader,
  Skeleton,
} from "@/components/ui";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 100;

export default function ArtistPage() {
  const t = useT();
  const { isAdmin } = useAuth();

  const params = useParams<{ id: string }>();
  const id = params.id;

  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState(false);

  useEntityOpened("artistOpened", id);

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
            {isAdmin && (
              <button type="button" className="button" onClick={() => setEditing(true)}>
                <EditIcon size={16} /> {t("action.edit")}
              </button>
            )}
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
        <TrackList
          tracks={data.tracks.items}
          showArtist={false}
          onChanged={reload}
          origin={{ source: "artist", sourceId: data.id }}
        />
        <Pagination result={data.tracks} onChange={setPage} />
      </section>

      {editing && (
        <EditArtistDialog
          artist={{ id: data.id, name: data.name, hasImage: data.hasImage }}
          onClose={() => setEditing(false)}
          onSaved={reload}
        />
      )}
    </>
  );
}
