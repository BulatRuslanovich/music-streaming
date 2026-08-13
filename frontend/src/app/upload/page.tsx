"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import { api, type UploadProgress } from "@/lib/api";
import { checkAgainstLibrary, fileKey, type FileVerdict } from "@/lib/uploadCheck";
import { useFormat } from "@/lib/useFormat";
import { useAuth } from "@/contexts/AuthContext";
import { useToast } from "@/contexts/ToastContext";
import { UploadIcon } from "@/components/Icons";
import { TrackList } from "@/components/TrackList";
import { PageHeader } from "@/components/ui";
import type { ClientConfig, Track } from "@/lib/types";

import { useT } from "@/contexts/I18nContext";

function FileVerdictBadge({ verdict }: { verdict?: FileVerdict }) {
  const t = useT();

  if (!verdict || verdict.verdict === "New") return null;

  const duplicate = verdict.verdict === "Duplicate";
  const match = verdict.match ? `${verdict.match.artistName} — ${verdict.match.title}` : null;

  return (
    <span className={`file-flag ${duplicate ? "is-duplicate" : "is-similar"}`}>
      {duplicate ? t("upload.duplicate") : t("upload.similar")}
      {match && <span className="file-flag-match">{match}</span>}
    </span>
  );
}

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
  const [verdicts, setVerdicts] = useState<Record<string, FileVerdict>>({});
  const [checksRunning, setChecksRunning] = useState(0);

  useEffect(() => {
    api
      .config()
      .then(setConfig)
      .catch(() => setConfig(null));
  }, []);

  const maxBytes = config?.maxUploadBytes ?? 100 * 1024 * 1024;

  const check = useCallback(async (files: File[]) => {
    if (files.length === 0) return;

    setChecksRunning((running) => running + 1);
    try {
      const checked = await checkAgainstLibrary(files);
      setVerdicts((current) => ({ ...current, ...checked }));
    } catch {
    } finally {
      setChecksRunning((running) => running - 1);
    }
  }, []);

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

      const queued = new Set(selected.map(fileKey));
      const added = next.filter((file) => !queued.has(fileKey(file)));

      setSelected((current) => [...current, ...added]);
      void check(added.filter((file) => verdicts[fileKey(file)] === undefined));

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
    [selected, verdicts, check, maxBytes, notify, t, format],
  );

  const duplicates = selected.filter((file) => verdicts[fileKey(file)]?.verdict === "Duplicate");
  const pending = selected.filter((file) => verdicts[fileKey(file)]?.verdict !== "Duplicate");

  const upload = async () => {
    if (pending.length === 0) return;

    const skipped = duplicates.map((file) => ({
      fileName: file.name,
      reason: t("upload.alreadyInLibrary"),
    }));

    setProgress({ percent: 0, fileIndex: 0, fileCount: pending.length, fileName: pending[0].name });
    setFailed(skipped);

    try {
      const result = await api.upload(pending, setProgress);

      setUploaded((current) => [...result.uploaded, ...current]);
      setFailed([...skipped, ...result.failed]);
      setSelected([]);
      setVerdicts({});
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

  const totalSize = pending.reduce((sum, file) => sum + file.size, 0);

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
              {t("upload.ready", { count: pending.length })} · {format.bytes(totalSize)}
              {duplicates.length > 0 && (
                <span className="muted">
                  {" "}
                  · {t("upload.skipped", { count: duplicates.length })}
                </span>
              )}
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
                <FileVerdictBadge verdict={verdicts[fileKey(file)]} />
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
            <button
              type="button"
              className="button button-primary"
              onClick={() => void upload()}
              disabled={checksRunning > 0 || pending.length === 0}
            >
              {checksRunning > 0
                ? t("upload.checking")
                : pending.length === 0
                  ? t("upload.nothingToUpload")
                  : t("upload.submit", { count: pending.length })}
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
          <TrackList tracks={uploaded} onChanged={() => setUploaded([])} />
        </section>
      )}
    </>
  );
}
