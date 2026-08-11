"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import { api, type UploadProgress } from "@/lib/api";
import { useFormat } from "@/lib/useFormat";
import { useAuth } from "@/contexts/AuthContext";
import { useToast } from "@/contexts/ToastContext";
import { UploadIcon } from "@/components/Icons";
import { TrackList } from "@/components/TrackList";
import { PageHeader } from "@/components/ui";
import type { ClientConfig, Track } from "@/lib/types";

import { useT } from "@/contexts/I18nContext";

export default function UploadPage() {
  const t = useT();
  const format = useFormat();

  const { notify, notifyError } = useToast();
  const { isAdmin } = useAuth();
  const inputRef = useRef<HTMLInputElement | null>(null);

  const [config, setConfig] = useState<ClientConfig | null>(null);
  const [selected, setSelected] = useState<File[]>([]);
  const [dragging, setDragging] = useState(false);
  const [progress, setProgress] = useState<UploadProgress | null>(null);
  const [uploaded, setUploaded] = useState<Track[]>([]);
  const [failed, setFailed] = useState<{ fileName: string; reason: string }[]>([]);

  useEffect(() => {
    api.config().then(setConfig).catch(() => setConfig(null));
  }, []);

  const maxBytes = config?.maxUploadBytes ?? 100 * 1024 * 1024;

  const accept = useCallback(
    (files: FileList | null) => {
      if (!files) return;

      const next: File[] = [];
      const rejected: { fileName: string; reason: string }[] = [];

      for (const file of Array.from(files)) {
        if (!file.name.toLowerCase().endsWith(".mp3")) {
          rejected.push({ fileName: file.name, reason: t("upload.onlyMp3") });
        } else if (file.size > maxBytes) {
          rejected.push({
            fileName: file.name,
            reason: t("upload.tooLarge", { limit: format.bytes(maxBytes) }),
          });
        } else {
          next.push(file);
        }
      }

      setSelected((current) => {
        const seen = new Set(current.map((file) => `${file.name}:${file.size}`));
        return [...current, ...next.filter((file) => !seen.has(`${file.name}:${file.size}`))];
      });

      if (rejected.length > 0) {
        setFailed((current) => [...current, ...rejected]);
        notify(
          rejected.length === 1
            ? `${rejected[0].fileName}: ${rejected[0].reason}`
            : t("upload.rejected", { count: rejected.length }),
          "error",
        );
      }
    },
    [maxBytes, notify, t, format],
  );

  const upload = async () => {
    if (selected.length === 0) return;

    setProgress({ percent: 0, fileIndex: 0, fileCount: selected.length, fileName: selected[0].name });
    setFailed([]);

    try {
      const result = await api.upload(selected, setProgress);

      setUploaded((current) => [...result.uploaded, ...current]);
      setFailed(result.failed);
      setSelected([]);
      if (inputRef.current) inputRef.current.value = "";

      if (result.uploaded.length > 0) {
        notify(t("upload.added", { count: result.uploaded.length }), "success");
      }
      if (result.failed.length > 0) {
        notify(t("upload.rejected", { count: result.failed.length }), "error");
      }
    } catch (reason) {
      notifyError(reason, t("upload.failed"));
    } finally {
      setProgress(null);
    }
  };

  const totalSize = selected.reduce((sum, file) => sum + file.size, 0);

  return (
    <>
      <PageHeader
        title={t("nav.upload")}
        subtitle={t("upload.subtitle", { limit: format.bytes(maxBytes) })}
      />

      <div
        className={`dropzone ${dragging ? "is-dragging" : ""}`}
        onDragOver={(event) => {
          event.preventDefault();
          setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={(event) => {
          event.preventDefault();
          setDragging(false);
          accept(event.dataTransfer.files);
        }}
      >
        <UploadIcon size={34} />
        <p>{t("upload.dropHint")}</p>
        <button type="button" className="button" onClick={() => inputRef.current?.click()}>
          {t("upload.chooseFiles")}
        </button>
        <input
          ref={inputRef}
          type="file"
          accept="audio/mpeg,.mp3"
          multiple
          hidden
          onChange={(event) => accept(event.target.files)}
        />
      </div>

      {selected.length > 0 && (
        <section className="upload-queue">
          <div className="section-header">
            <h2>
              {t("upload.ready", { count: selected.length })} · {format.bytes(totalSize)}
            </h2>
            <button
              type="button"
              className="text-button"
              onClick={() => setSelected([])}
              disabled={progress !== null}
            >
              {t("action.clear")}
            </button>
          </div>

          <ul className="file-list">
            {selected.map((file, index) => (
              <li key={`${file.name}-${file.size}-${index}`}>
                <span className="file-name">{file.name}</span>
                <span className="muted">{format.bytes(file.size)}</span>
                <button
                  type="button"
                  className="text-button"
                  disabled={progress !== null}
                  onClick={() => setSelected((current) => current.filter((_, at) => at !== index))}
                  aria-label={t("upload.removeNamed", { fileName: file.name })}
                >
                  {t("action.remove")}
                </button>
              </li>
            ))}
          </ul>

          {progress !== null ? (
            <div
              className="progress"
              role="progressbar"
              aria-valuenow={progress.percent}
              aria-valuemin={0}
              aria-valuemax={100}
            >
              <div className="progress-bar" style={{ width: `${progress.percent}%` }} />
              <span className="progress-label">
                {progress.percent >= 100
                  ? t("upload.readingTags")
                  : progress.fileCount > 1
                    ? t("upload.uploadingFile", {
                        index: progress.fileIndex + 1,
                        count: progress.fileCount,
                        progress: progress.percent,
                      })
                    : t("upload.uploading", { progress: progress.percent })}
              </span>
            </div>
          ) : (
            <button type="button" className="button button-primary" onClick={() => void upload()}>
              {t("upload.submit", { count: selected.length })}
            </button>
          )}
        </section>
      )}

      {failed.length > 0 && (
        <section>
          <h2 className="section-title">{t("upload.notAdded")}</h2>
          <ul className="failure-list">
            {failed.map((failure, index) => (
              <li key={`${failure.fileName}-${index}`}>
                <span className="file-name">{failure.fileName}</span>
                <span className="failure-reason">{failure.reason}</span>
              </li>
            ))}
          </ul>
        </section>
      )}

      {uploaded.length > 0 && (
        <section>
          <div className="section-header">
            <h2>{t("upload.justAdded")}</h2>
            <Link href="/tracks" className="text-button">
              {t("upload.goToLibrary")}
            </Link>
          </div>
          <p className="hint">
            {isAdmin ? t("upload.metadataHintAdmin") : t("upload.metadataHintUser")}
          </p>
          <TrackList
            tracks={uploaded}
            onChanged={() => setUploaded([])}
          />
        </section>
      )}
    </>
  );
}
