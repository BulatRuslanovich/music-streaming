"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { useToast } from "@/contexts/ToastContext";
import { PlusIcon } from "@/components/Icons";
import { EmptyState, LoadError, PageHeader, PlaylistCard, Skeleton } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

export default function PlaylistsPage() {
  const t = useT();

  const { data, error, loading, reload } = useApi(() => api.playlists(), [], "playlists");
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
      notify(t("playlists.created", { name: name.trim() }), "success");
      setName("");
      setDescription("");
      setCreating(false);
      reload();
    } catch (reason) {
      notifyError(reason, t("playlists.createFailed"));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <>
      <PageHeader
        title={t("nav.playlists")}
        subtitle={data ? t("count.playlists", { count: data.length }) : undefined}
        actions={
          <button type="button" className="button button-primary" onClick={() => setCreating(true)}>
            <PlusIcon size={16} /> {t("playlists.new")}
          </button>
        }
      />

      {creating && (
        <form className="inline-form" onSubmit={create}>
          <div className="inline-form-fields">
            <label htmlFor="playlist-name" className="sr-only">
              {t("playlists.name")}
            </label>
            <input
              id="playlist-name"
              type="text"
              placeholder={t("playlists.name")}

              value={name}
              maxLength={200}
              autoFocus
              required
              onChange={(event) => setName(event.target.value)}
            />

            <label htmlFor="playlist-description" className="sr-only">
              {t("playlists.description")}
            </label>
            <input
              id="playlist-description"
              type="text"
              placeholder={t("playlists.description")}
              value={description}
              maxLength={1000}
              onChange={(event) => setDescription(event.target.value)}
            />
          </div>

          <div className="inline-form-actions">
            <button type="submit" className="button button-primary" disabled={submitting}>
              {submitting ? t("action.creating") : t("action.create")}
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
              {t("action.cancel")}
            </button>
          </div>
        </form>
      )}

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton count={6} />}

      {data && data.length === 0 && !creating && (
        <EmptyState
          title={t("playlists.emptyTitle")}
          description={t("playlists.emptyDescription")}
          action={
            <button type="button" className="button button-primary" onClick={() => setCreating(true)}>
              {t("playlists.new")}
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
