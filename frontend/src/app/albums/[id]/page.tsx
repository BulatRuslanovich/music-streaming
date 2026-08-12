"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { api } from "@/lib/api";
import { coverUrl } from "@/lib/media";
import { useFormat } from "@/lib/useFormat";
import { useApi } from "@/lib/useApi";
import { useEntityOpened } from "@/lib/useEntityOpened";
import { useCoverColor } from "@/lib/useCoverColor";
import { Cover } from "@/components/Cover";
import { TrackList } from "@/components/TrackList";
import { LoadError, PlayAllButton, Skeleton } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

export default function AlbumPage() {
  const t = useT();
  const format = useFormat();

  const params = useParams<{ id: string }>();
  const id = params.id;

  const { data, error, loading, reload } = useApi(() => api.album(id), [id], "album");

  useEntityOpened("albumOpened", id);
  const tint = useCoverColor(data ? coverUrl({ albumId: data.id, hasCover: data.hasCover }) : null);

  if (error) return <LoadError message={error} onRetry={reload} />;
  if (loading && !data) return <Skeleton variant="row" count={8} />;
  if (!data) return null;

  return (
    <>
      <header className="detail-header" style={{ ["--cover-tint" as string]: tint ?? "" }}>
        <div className="detail-art">
          <Cover albumId={data.id} hasCover={data.hasCover} name={data.title} variant="full" />
        </div>

        <div className="detail-meta">
          <span className="detail-kind">{t("albums.kind")}</span>
          <h1>{data.title}</h1>
          <p className="detail-facts">
            <Link href={`/artists/${data.artistId}`} className="detail-artist">
              {data.artistName}
            </Link>
            {data.year ? <span> · {data.year}</span> : null}
            <span> · {t("count.tracks", { count: data.tracks.length })}</span>
            {data.durationSeconds > 0 && <span> · {format.totalDuration(data.durationSeconds)}</span>}
          </p>

          <div className="detail-actions">
            <PlayAllButton tracks={data.tracks} name={data.title} />
          </div>
        </div>
      </header>

      <TrackList
        tracks={data.tracks}
        showAlbum={false}
        showCover={false}
        showArtist
        useTrackNumbers
        onChanged={reload}
        origin={{ source: "album", sourceId: data.id }}
      />
    </>
  );
}
