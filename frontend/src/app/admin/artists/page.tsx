"use client";

import Link from "next/link";
import { useState } from "react";
import { api } from "@/lib/api";
import { usePagedApi } from "@/lib/usePagedApi";
import { Cover } from "@/components/Cover";
import { ArtistMenu } from "@/components/ArtistMenu";
import { SearchField } from "@/components/SearchField";
import { LoadError, PageHeader, Pagination, Skeleton } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 50;

export default function AdminArtistsPage() {
  const t = useT();
  const [search, setSearch] = useState("");
  const [menuFor, setMenuFor] = useState<string | null>(null);

  const { data, error, loading, reload, setPage } = usePagedApi(
    (page) => api.artists({ page, pageSize: PAGE_SIZE, q: search || undefined }),
    [search],
    "adminArtists",
  );

  return (
    <>
      <PageHeader
        title={t("nav.artists")}
        subtitle={data ? t("count.artists", { count: data.total }) : undefined}
      />

      <div className="page-tools">
        <SearchField
          value={search}
          onChange={setSearch}
          placeholder={t("filter.artists")}
        />
      </div>

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton variant="row" count={8} />}

      {data && (
        <>
          {data.items.length === 0 ? (
            <p className="empty-state">
              {search ? t("filter.nothingMatched") : t("artists.empty")}
            </p>
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

                  <Link href={`/artists/${artist.id}`} className="admin-row-name">
                    {artist.name}
                  </Link>

                  <span className="muted">
                    {t("count.tracks", { count: artist.trackCount })}
                    {artist.albumCount > 0
                      ? ` · ${t("count.albums", { count: artist.albumCount })}`
                      : ""}
                  </span>

                  <ArtistMenu
                    artist={{ id: artist.id, name: artist.name, hasImage: artist.hasImage }}
                    open={menuFor === artist.id}
                    onOpenChange={(open) => setMenuFor(open ? artist.id : null)}
                    onChanged={reload}
                  />
                </div>
              ))}
            </div>
          )}

          <Pagination result={data} onChange={setPage} />
        </>
      )}
    </>
  );
}
