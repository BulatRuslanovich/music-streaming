"use client";

import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { playlistCoverUrl } from "@/lib/media";
import { useFormat } from "@/lib/useFormat";
import { useApi } from "@/lib/useApi";
import { useCoverColor } from "@/lib/useCoverColor";
import { useEntityOpened } from "@/lib/useEntityOpened";
import { useAuth } from "@/contexts/AuthContext";
import { useToast } from "@/contexts/ToastContext";
import { Cover } from "@/components/Cover";
import { EditPlaylistDialog } from "@/components/EditPlaylistDialog";
import { EditIcon, PlaylistIcon, TrashIcon } from "@/components/Icons";
import { TrackList } from "@/components/TrackList";
import { LoadError, PlayAllButton, Skeleton } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

export default function PlaylistPage() {
  const t = useT();
  const format = useFormat();

  const params = useParams<{ id: string }>();
  const id = params.id;
  const router = useRouter();
  const { notify, notifyError } = useToast();
  const { user } = useAuth();

  const { data, error, loading, reload, patch } = useApi(() => api.playlist(id), [id], "playlist");

  useEntityOpened("playlistOpened", id);

  const tint = useCoverColor(
    data
      ? playlistCoverUrl({
          playlistId: data.id,
          hasCover: data.hasCover,
          coverTrackId: data.coverTrackId,
        })
      : null,
  );

  const [editing, setEditing] = useState(false);

  if (error) return <LoadError message={error} onRetry={reload} />;
  if (loading && !data) return <Skeleton variant="row" count={8} />;
  if (!data) return null;

  // Чужой публичный плейлист можно слушать, но не трогать: правка, удаление и порядок — владельцу.
  const isOwner = user?.id === data.ownerId;

  const remove = async () => {
    if (!window.confirm(t("playlists.confirmDelete", { name: data.name }))) return;

    try {
      await api.deletePlaylist(id);
      notify(t("playlists.deleted"), "success");
      router.push("/playlists");
    } catch (reason) {
      notifyError(reason, t("playlists.deleteFailed"));
    }
  };

  const reorder = async (trackIds: string[]) => {
    // Показываем новый порядок сразу; запрос подтверждает его в фоне.
    patch((current) => ({
      ...current,
      tracks: trackIds
        .map((trackId) => current.tracks.find((track) => track.id === trackId))
        .filter((track): track is NonNullable<typeof track> => Boolean(track)),
    }));

    try {
      await api.reorderPlaylist(id, trackIds);
    } catch (reason) {
      notifyError(reason, t("playlists.reorderFailed"));
      reload();
    }
  };

  return (
    <>
      <header className="detail-header" style={{ ["--cover-tint" as string]: tint ?? "" }}>
        <div className="detail-art">
          <Cover
            playlistId={data.id}
            hasCover={data.hasCover}
            coverTrackId={data.coverTrackId}
            name={data.name}
            variant="full"
            fallback={<PlaylistIcon size={48} />}
          />
        </div>

        <div className="detail-meta">
          <span className="detail-kind">{t("playlists.kind")}</span>

          <h1>{data.name}</h1>
          {data.description && <p className="detail-description">{data.description}</p>}
          <p className="detail-facts">
            {!isOwner && <span>{t("playlists.by", { name: data.ownerName })} · </span>}
            {t("count.tracks", { count: data.tracks.length })}
            {data.durationSeconds > 0 && <span> · {format.totalDuration(data.durationSeconds)}</span>}
            {isOwner && data.isPublic && (
              <>
                {" · "}
                <span className="badge">{t("playlists.publicBadge")}</span>
              </>
            )}
          </p>

          <div className="detail-actions">
            <PlayAllButton tracks={data.tracks} name={data.name} />
            {isOwner && (
              <>
                <button type="button" className="button" onClick={() => setEditing(true)}>
                  <EditIcon size={16} /> {t("action.edit")}
                </button>
                <button type="button" className="button button-danger" onClick={() => void remove()}>
                  <TrashIcon size={16} /> {t("action.delete")}
                </button>
              </>
            )}
          </div>
        </div>
      </header>

      <TrackList
        tracks={data.tracks}
        playlistId={isOwner ? id : undefined}
        onReorder={isOwner ? (trackIds) => void reorder(trackIds) : undefined}
        onChanged={reload}
        origin={{ source: "playlist", sourceId: id }}
      />

      {isOwner && data.tracks.length > 1 && <p className="hint">{t("playlists.dragToReorder")}</p>}

      {editing && isOwner && (
        <EditPlaylistDialog
          playlist={data}
          onClose={() => setEditing(false)}
          onSaved={reload}
        />
      )}
    </>
  );
}
