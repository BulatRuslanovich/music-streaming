"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { useToast } from "@/contexts/ToastContext";
import { PlusIcon } from "@/components/Icons";
import { EmptyState, LoadError, PageHeader, PlaylistCard, Skeleton } from "@/components/ui";

export default function PlaylistsPage() {
  const { data, error, loading, reload } = useApi(() => api.playlists(), []);
  const { notify, notifyError } = useToast();

  const [creating, setCreating] = useState(false);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const create = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!name.trim()) return;

    setSubmitting(true);
    try {
      await api.createPlaylist(name.trim(), description.trim() || undefined);
      notify(`Created "${name.trim()}".`, "success");
      setName("");
      setDescription("");
      setCreating(false);
      reload();
    } catch (reason) {
      notifyError(reason, "Could not create the playlist.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Playlists"
        subtitle={data ? `${data.length} playlist${data.length === 1 ? "" : "s"}` : undefined}
        actions={
          <button type="button" className="button button-primary" onClick={() => setCreating(true)}>
            <PlusIcon size={16} /> New playlist
          </button>
        }
      />

      {creating && (
        <form className="inline-form" onSubmit={create}>
          <div className="inline-form-fields">
            <label htmlFor="playlist-name" className="sr-only">
              Playlist name
            </label>
            <input
              id="playlist-name"
              type="text"
              placeholder="Playlist name"
              value={name}
              maxLength={200}
              autoFocus
              required
              onChange={(event) => setName(event.target.value)}
            />

            <label htmlFor="playlist-description" className="sr-only">
              Description
            </label>
            <input
              id="playlist-description"
              type="text"
              placeholder="Description (optional)"
              value={description}
              maxLength={1000}
              onChange={(event) => setDescription(event.target.value)}
            />
          </div>

          <div className="inline-form-actions">
            <button type="submit" className="button button-primary" disabled={submitting}>
              {submitting ? "Creating…" : "Create"}
            </button>
            <button
              type="button"
              className="button"
              onClick={() => {
                setCreating(false);
                setName("");
                setDescription("");
              }}
            >
              Cancel
            </button>
          </div>
        </form>
      )}

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton count={6} />}

      {data && data.length === 0 && !creating && (
        <EmptyState
          title="No playlists yet"
          description="Create one, then add tracks to it from any track's menu."
          action={
            <button type="button" className="button button-primary" onClick={() => setCreating(true)}>
              New playlist
            </button>
          }
        />
      )}

      {data && data.length > 0 && (
        <div className="card-grid">
          {data.map((playlist) => (
            <PlaylistCard key={playlist.id} playlist={playlist} />
          ))}
        </div>
      )}
    </>
  );
}
