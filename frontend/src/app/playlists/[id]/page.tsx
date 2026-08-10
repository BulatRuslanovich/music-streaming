"use client";

import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { formatTotalDuration } from "@/lib/format";
import { useApi } from "@/lib/useApi";
import { useToast } from "@/contexts/ToastContext";
import { EditIcon, PlaylistIcon, TrashIcon } from "@/components/Icons";
import { TrackList } from "@/components/TrackList";
import { LoadError, PlayAllButton, Skeleton } from "@/components/ui";

export default function PlaylistPage() {
  const params = useParams<{ id: string }>();
  const id = params.id;
  const router = useRouter();
  const { notify, notifyError } = useToast();

  const { data, error, loading, reload, patch } = useApi(() => api.playlist(id), [id]);

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
      notify("Playlist updated.", "success");
      setEditing(false);
      reload();
    } catch (reason) {
      notifyError(reason, "Could not rename the playlist.");
    }
  };

  const remove = async () => {
    if (!window.confirm(`Delete the playlist "${data.name}"?\n\nThe tracks themselves are kept.`)) {
      return;
    }

    try {
      await api.deletePlaylist(id);
      notify("Playlist deleted.", "success");
      router.push("/playlists");
    } catch (reason) {
      notifyError(reason, "Could not delete the playlist.");
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
      notifyError(reason, "Could not save the new order.");
      reload();
    }
  };

  return (
    <>
      <header className="detail-header">
        <div className="detail-art card-art-playlist">
          <PlaylistIcon size={48} />
        </div>

        <div className="detail-meta">
          <span className="detail-kind">Playlist</span>

          {editing ? (
            <form className="inline-form" onSubmit={save}>
              <div className="inline-form-fields">
                <label htmlFor="edit-name" className="sr-only">
                  Playlist name
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
                  Description
                </label>
                <input
                  id="edit-description"
                  type="text"
                  placeholder="Description"
                  value={description}
                  maxLength={1000}
                  onChange={(event) => setDescription(event.target.value)}
                />
              </div>
              <div className="inline-form-actions">
                <button type="submit" className="button button-primary">
                  Save
                </button>
                <button type="button" className="button" onClick={() => setEditing(false)}>
                  Cancel
                </button>
              </div>
            </form>
          ) : (
            <>
              <h1>{data.name}</h1>
              {data.description && <p className="detail-description">{data.description}</p>}
              <p className="detail-facts">
                {data.tracks.length} track{data.tracks.length === 1 ? "" : "s"}
                {data.durationSeconds > 0 && <span> · {formatTotalDuration(data.durationSeconds)}</span>}
              </p>
            </>
          )}

          {!editing && (
            <div className="detail-actions">
              <PlayAllButton tracks={data.tracks} label={`Play ${data.name}`} />
              <button type="button" className="button" onClick={startEditing}>
                <EditIcon size={16} /> Rename
              </button>
              <button type="button" className="button button-danger" onClick={() => void remove()}>
                <TrashIcon size={16} /> Delete
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
        emptyMessage="This playlist is empty — add tracks from any track's ⋮ menu."
      />

      {data.tracks.length > 1 && (
        <p className="hint">Drag a row to reorder the playlist.</p>
      )}
    </>
  );
}
