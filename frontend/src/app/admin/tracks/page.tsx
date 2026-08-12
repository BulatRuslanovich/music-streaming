"use client";

import { api } from "@/lib/api";
import { usePagedApi } from "@/lib/usePagedApi";
import { TrackList } from "@/components/TrackList";
import { LoadError, PageHeader, Pagination, Skeleton } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 50;

export default function AdminTracksPage() {
  const t = useT();
  const { data, error, loading, reload, setPage } = usePagedApi(
    (page) => api.tracks({ page, pageSize: PAGE_SIZE, sort: "Recent" }),
    [],
    "adminTracks",
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

          <Pagination result={data} onChange={setPage} />
        </>
      )}
    </>
  );
}
