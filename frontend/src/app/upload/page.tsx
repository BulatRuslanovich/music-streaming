"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import { formatBytes } from "@/lib/format";
import { useAuth } from "@/contexts/AuthContext";
import { useToast } from "@/contexts/ToastContext";
import { UploadIcon } from "@/components/Icons";
import { TrackList } from "@/components/TrackList";
import { PageHeader } from "@/components/ui";
import type { ClientConfig, Track } from "@/lib/types";

export default function UploadPage() {
  const { notify, notifyError } = useToast();
  const { isAdmin } = useAuth();
  const inputRef = useRef<HTMLInputElement | null>(null);

  const [config, setConfig] = useState<ClientConfig | null>(null);
  const [selected, setSelected] = useState<File[]>([]);
  const [dragging, setDragging] = useState(false);
  const [progress, setProgress] = useState<number | null>(null);
  const [uploaded, setUploaded] = useState<Track[]>([]);
  const [failed, setFailed] = useState<{ fileName: string; reason: string }[]>([]);

  useEffect(() => {
    api.config().then(setConfig).catch(() => setConfig(null));
  }, []);

  const maxBytes = config?.maxUploadBytes ?? 100 * 1024 * 1024;

  /** Keeps only .mp3 files and rejects oversized ones before any bytes leave the browser. */
  const accept = useCallback(
    (files: FileList | null) => {
      if (!files) return;

      const next: File[] = [];
      const rejected: { fileName: string; reason: string }[] = [];

      for (const file of Array.from(files)) {
        if (!file.name.toLowerCase().endsWith(".mp3")) {
          rejected.push({ fileName: file.name, reason: "Only .mp3 files are supported." });
        } else if (file.size > maxBytes) {
          rejected.push({
            fileName: file.name,
            reason: `Larger than the ${formatBytes(maxBytes)} limit.`,
          });
        } else {
          next.push(file);
        }
      }

      setSelected((current) => {
        // De-duplicate by name and size so dropping the same batch twice is harmless.
        const seen = new Set(current.map((file) => `${file.name}:${file.size}`));
        return [...current, ...next.filter((file) => !seen.has(`${file.name}:${file.size}`))];
      });

      if (rejected.length > 0) setFailed((current) => [...current, ...rejected]);
    },
    [maxBytes],
  );

  const upload = async () => {
    if (selected.length === 0) return;

    setProgress(0);
    setFailed([]);

    try {
      const result = await api.upload(selected, setProgress);

      setUploaded((current) => [...result.uploaded, ...current]);
      setFailed(result.failed);
      setSelected([]);
      if (inputRef.current) inputRef.current.value = "";

      if (result.uploaded.length > 0) {
        notify(
          `Added ${result.uploaded.length} track${result.uploaded.length === 1 ? "" : "s"} to your library.`,
          "success",
        );
      }
      if (result.failed.length > 0) {
        notify(`${result.failed.length} file${result.failed.length === 1 ? "" : "s"} could not be added.`, "error");
      }
    } catch (reason) {
      notifyError(reason, "The upload failed.");
    } finally {
      setProgress(null);
    }
  };

  const totalSize = selected.reduce((sum, file) => sum + file.size, 0);

  return (
    <>
      <PageHeader
        title="Upload music"
        subtitle={`MP3 files only, up to ${formatBytes(maxBytes)} each. Artist, album, genre and cover art are read from the file's tags.`}
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
        <p>Drag MP3 files here</p>
        <button type="button" className="button" onClick={() => inputRef.current?.click()}>
          Choose files
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
              {selected.length} file{selected.length === 1 ? "" : "s"} ready · {formatBytes(totalSize)}
            </h2>
            <button
              type="button"
              className="text-button"
              onClick={() => setSelected([])}
              disabled={progress !== null}
            >
              Clear
            </button>
          </div>

          <ul className="file-list">
            {selected.map((file, index) => (
              <li key={`${file.name}-${file.size}-${index}`}>
                <span className="file-name">{file.name}</span>
                <span className="muted">{formatBytes(file.size)}</span>
                <button
                  type="button"
                  className="text-button"
                  disabled={progress !== null}
                  onClick={() => setSelected((current) => current.filter((_, at) => at !== index))}
                  aria-label={`Remove ${file.name}`}
                >
                  Remove
                </button>
              </li>
            ))}
          </ul>

          {progress !== null ? (
            <div className="progress" role="progressbar" aria-valuenow={progress} aria-valuemin={0} aria-valuemax={100}>
              <div className="progress-bar" style={{ width: `${progress}%` }} />
              <span className="progress-label">
                {progress < 100 ? `Uploading… ${progress}%` : "Reading tags…"}
              </span>
            </div>
          ) : (
            <button type="button" className="button button-primary" onClick={() => void upload()}>
              Upload {selected.length} file{selected.length === 1 ? "" : "s"}
            </button>
          )}
        </section>
      )}

      {failed.length > 0 && (
        <section>
          <h2 className="section-title">Not added</h2>
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
            <h2>Just added</h2>
            <Link href="/tracks" className="text-button">
              Go to library
            </Link>
          </div>
          <p className="hint">
            Metadata came from each file&apos;s ID3 tags.{" "}
            {isAdmin
              ? "Anything missing can be corrected from the track's ⋮ menu."
              : "Ask an administrator to correct anything that is missing."}
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
