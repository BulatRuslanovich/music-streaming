"use client";

import Link from "next/link";
import { useState } from "react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import type { Artist } from "@/lib/types";
import { Cover } from "@/components/Cover";
import { EditArtistDialog } from "@/components/EditArtistDialog";
import { EditIcon } from "@/components/Icons";
import { LoadError, PageHeader, Pagination, Skeleton } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 50;

export default function AdminArtistsPage() {
  const t = useT();
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<Artist | null>(null);

  const { data, error, loading, reload } = useApi(
    () => api.artists({ page, pageSize: PAGE_SIZE }),
    [page],
  );

  return (
    <>
      <PageHeader
        title={t("nav.artists")}
        subtitle={data ? t("count.artists", { count: data.total }) : undefined}
      />

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton variant="row" count={8} />}

      {data && (
        <>
          {data.items.length === 0 ? (
            <p className="empty-state">{t("artists.empty")}</p>
          ) : (
            <div className="admin-table">
              {data.items.map((artist) => (
                <div className="admin-row admin-row-artist" key={artist.id}>
                  <Cover
                    artistId={artist.id}
                    hasCover={artist.hasImage}
                    name={artist.name}
                    size={40}
                    rounded
                  />

                  <Link href={`/artists/${artist.id}`}>{artist.name}</Link>

                  <span className="muted">
                    {t("count.tracks", { count: artist.trackCount })}
                    {artist.albumCount > 0
                      ? ` · ${t("count.albums", { count: artist.albumCount })}`
                      : ""}
                  </span>

                  <button type="button" className="button" onClick={() => setEditing(artist)}>
                    <EditIcon size={16} /> {t("action.edit")}
                  </button>
                </div>
              ))}
            </div>
          )}

          <Pagination
            page={data.page}
            totalPages={data.totalPages}
            onChange={setPage}
          />
        </>
      )}

      {editing && (
        <EditArtistDialog
          artist={editing}
          onClose={() => setEditing(null)}
          onSaved={reload}
        />
      )}
    </>
  );
}
