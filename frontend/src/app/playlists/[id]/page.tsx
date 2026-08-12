"use client";

import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { accentFor } from "@/lib/format";
import { useFormat } from "@/lib/useFormat";
import { useApi } from "@/lib/useApi";
import { useEntityOpened } from "@/lib/useEntityOpened";
import { useToast } from "@/contexts/ToastContext";
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

  const { data, error, loading, reload, patch } = useApi(() => api.playlist(id), [id], "playlist");

  useEntityOpened("playlistOpened", id);

  const [editing, setEditing] = useState(false);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");

  if (error) return <LoadError message={error} onRetry={reload} />;
  if (loading && !data) return <Skeleton variant="row" count={8} />;
  if (!data) return null;

  const startEditing = () => {
    setName(data.name);
    setDescription(data.description ?? "");
    setEditing(true);
  };

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    try {
      await api.updatePlaylist(id, name.trim(), description.trim() || null);
      notify(t("playlists.updated"), "success");
      setEditing(false);
      reload();
    } catch (reason) {
      notifyError(reason, t("playlists.updateFailed"));
    }
  };

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
    // Reflect the new order at once; the request confirms it in the background.
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
      <header className="detail-header">
        <div className="detail-art card-art-playlist" style={{ background: accentFor(data.name) }}>
          <PlaylistIcon size={48} />
        </div>

        <div className="detail-meta">
          <span className="detail-kind">{t("playlists.kind")}</span>

          {editing ? (
            <form className="inline-form" onSubmit={save}>
              <div className="inline-form-fields">
                <label htmlFor="edit-name" className="sr-only">
                  {t("playlists.name")}
                </label>
                <input
                  id="edit-name"
                  type="text"
                  value={name}
                  maxLength={200}
                  required
                  autoFocus
                  onChange={(event) => setName(event.target.value)}
                />
                <label htmlFor="edit-description" className="sr-only">
                  {t("playlists.description")}
                </label>
                <input
                  id="edit-description"
                  type="text"
                  placeholder={t("playlists.description")}
                  value={description}
                  maxLength={1000}
                  onChange={(event) => setDescription(event.target.value)}
                />
              </div>
              <div className="inline-form-actions">
                <button type="submit" className="button button-primary">
                  {t("action.save")}
                </button>
                <button type="button" className="button" onClick={() => setEditing(false)}>
                  {t("action.cancel")}
                </button>
              </div>
            </form>
          ) : (
            <>
              <h1>{data.name}</h1>
              {data.description && <p className="detail-description">{data.description}</p>}
              <p className="detail-facts">
                {t("count.tracks", { count: data.tracks.length })}
                {data.durationSeconds > 0 && <span> · {format.totalDuration(data.durationSeconds)}</span>}
              </p>
            </>
          )}

          {!editing && (
            <div className="detail-actions">
              <PlayAllButton tracks={data.tracks} name={data.name} />
              <button type="button" className="button" onClick={startEditing}>
                <EditIcon size={16} /> {t("action.rename")}
              </button>
              <button type="button" className="button button-danger" onClick={() => void remove()}>
                <TrashIcon size={16} /> {t("action.delete")}
              </button>
            </div>
          )}
        </div>
      </header>

      <TrackList
        tracks={data.tracks}
        playlistId={id}
        onReorder={(trackIds) => void reorder(trackIds)}
        onChanged={reload}
        origin={{ source: "playlist", sourceId: id }}
      />

      {data.tracks.length > 1 && (
        <p className="hint">{t("playlists.dragToReorder")}</p>
      )}
    </>
  );
}
