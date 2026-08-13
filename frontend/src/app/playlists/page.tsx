"use client";

import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { useToast } from "@/contexts/ToastContext";
import { PlusIcon } from "@/components/Icons";
import { EmptyState, LoadError, PageHeader, PlaylistCard, Skeleton } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

type Tab = "mine" | "public";

export default function PlaylistsPage() {
  const t = useT();

  const [tab, setTab] = useState<Tab>("mine");

  const { data, error, loading, reload } = useApi(
    () => (tab === "public" ? api.publicPlaylists() : api.playlists()),
    [tab],
    tab === "public" ? "playlists-public" : "playlists",
  );
  const { notify, notifyError } = useToast();

  const [creating, setCreating] = useState(false);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [isPublic, setIsPublic] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const create = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!name.trim()) return;

    setSubmitting(true);
    try {
      await api.createPlaylist(name.trim(), description.trim() || undefined, isPublic);
      notify(t("playlists.created", { name: name.trim() }), "success");
      setName("");
      setDescription("");
      setIsPublic(false);
      setCreating(false);

      if (tab === "mine") reload();
      else setTab("mine");
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

      <nav className="tabs" aria-label={t("playlists.tabs")}>
        {(["mine", "public"] as const).map((value) => (
          <button
            key={value}
            type="button"
            className={`tab ${tab === value ? "is-active" : ""}`}
            aria-current={tab === value ? "page" : undefined}
            onClick={() => setTab(value)}
          >
            {value === "mine" ? t("playlists.mine") : t("playlists.public")}
          </button>
        ))}
      </nav>

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

          <label className="checkbox-field">
            <input
              type="checkbox"
              checked={isPublic}
              onChange={(event) => setIsPublic(event.target.checked)}
            />
            <span>{t("playlists.makePublic")}</span>
          </label>

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
                setIsPublic(false);
              }}
            >
              {t("action.cancel")}
            </button>
          </div>
        </form>
      )}

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton count={6} />}

      {data && data.length === 0 && !creating && tab === "mine" && (
        <EmptyState
          title={t("playlists.emptyTitle")}
          description={t("playlists.emptyDescription")}
          action={
            <button
              type="button"
              className="button button-primary"
              onClick={() => setCreating(true)}
            >
              {t("playlists.new")}
            </button>
          }
        />
      )}

      {data && data.length === 0 && !creating && tab === "public" && (
        <EmptyState
          title={t("playlists.publicEmptyTitle")}
          description={t("playlists.publicEmptyDescription")}
        />
      )}

      {data && data.length > 0 && (
        <div className="card-grid">
          {data.map((playlist) => (
            <PlaylistCard key={playlist.id} playlist={playlist} showOwner={tab === "public"} />
          ))}
        </div>
      )}
    </>
  );
}
