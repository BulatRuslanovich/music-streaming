"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { TrackList } from "@/components/TrackList";
import { LoadError, PageHeader, Pagination, Skeleton } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 50;

export default function AdminTracksPage() {
  const t = useT();
  const [page, setPage] = useState(1);

  const { data, error, loading, reload } = useApi(
    () => api.tracks({ page, pageSize: PAGE_SIZE, sort: "Recent" }),
    [page],
  );

  return (
    <>
      <PageHeader
        title={t("nav.tracks")}
        subtitle={
          data
            ? `${t("count.tracks", { count: data.total })} · ${t("sort.newestFirst")}`
            : undefined
        }
      />

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton variant="row" count={10} />}

      {data && (
        <>
          <TrackList
            tracks={data.items}
            showAlbum
            onChanged={reload}
                      />

          <Pagination
            page={data.page}
            totalPages={data.totalPages}
            onChange={setPage}
          />
        </>
      )}
    </>
  );
}
