"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import { api, type UploadProgress } from "@/lib/api";
import { ACCEPT_ATTRIBUTE, ACCEPTED_EXTENSIONS, isAcceptedAudio } from "@/lib/audioFormats";
import { checkAgainstLibrary, fileKey, type FileVerdict } from "@/lib/uploadCheck";
import { useFormat } from "@/lib/useFormat";
import { useAuth } from "@/contexts/AuthContext";
import { useToast } from "@/contexts/ToastContext";
import { cn } from "@/lib/cn";
import { useInvalidate } from "@/lib/useInvalidate";
import { UploadIcon } from "@/components/Icons";
import { TrackList } from "@/components/TrackList";
import { PageHeader, SectionHeader } from "@/components/PageHeader";
import { Button } from "@/components/ui/button";
import { Progress } from "@/components/ui/progress";
import type { ClientConfig, Track } from "@/lib/types";

import { useT } from "@/contexts/I18nContext";

function FileVerdictBadge({ verdict }: { verdict?: FileVerdict }) {
  const t = useT();

  if (!verdict || verdict.verdict === "New") return null;

  const duplicate = verdict.verdict === "Duplicate";
  const match = verdict.match ? `${verdict.match.artistName} — ${verdict.match.title}` : null;

  return (
    <span
      className={cn(
        "flex min-w-0 items-baseline gap-2 text-xs font-semibold whitespace-nowrap",
        duplicate ? "text-faint" : "text-warning",
        "max-[620px]:flex-1",
      )}
    >
      {duplicate ? t("upload.duplicate") : t("upload.similar")}
      {match && <span className="min-w-0 truncate font-normal text-muted-foreground">{match}</span>}
    </span>
  );
}

export default function UploadPage() {
  const t = useT();
  const format = useFormat();

  const { notify, notifyError } = useToast();
  const { isAdmin } = useAuth();
  const invalidate = useInvalidate();
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
        if (!isAcceptedAudio(file.name)) {
          rejected.push({
            fileName: file.name,
            reason: t("upload.unsupportedFormat", { formats: ACCEPTED_EXTENSIONS.join(", ") }),
          });
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
        invalidate("library");
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
        className={cn(
          "flex flex-col items-center gap-3 rounded-xl border-2 border-dashed px-6 py-11 text-center transition-colors",
          dragging
            ? "border-primary bg-primary-surface text-foreground"
            : "border-border-strong bg-card text-muted-foreground",
        )}
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
        <Button onClick={() => inputRef.current?.click()}>{t("upload.chooseFiles")}</Button>
        <input
          ref={inputRef}
          type="file"
          accept={ACCEPT_ATTRIBUTE}
          multiple
          hidden
          onChange={(event) => accept(event.target.files)}
        />
      </div>

      {selected.length > 0 && (
        <section className="flex flex-col gap-3.5">
          <SectionHeader
            title={`${t("upload.ready", { count: pending.length })} · ${format.bytes(totalSize)}${
              duplicates.length > 0 ? ` · ${t("upload.skipped", { count: duplicates.length })}` : ""
            }`}
          >
            <Button
              variant="text"
              size="auto"
              onClick={() => setSelected([])}
              disabled={progress !== null}
            >
              {t("action.clear")}
            </Button>
          </SectionHeader>

          <ul className="flex flex-col gap-0.5">
            {selected.map((file, index) => (
              <li
                key={`${file.name}-${file.size}-${index}`}
                className={cn(
                  "flex items-center gap-3.5 rounded-md bg-card px-3 py-2.5 text-sm",
                  "max-[620px]:flex-wrap max-[620px]:gap-y-1",
                )}
              >
                <span
                  className={cn(
                    "min-w-28 flex-1 truncate max-[620px]:flex-[1_0_100%]",
                    verdicts[fileKey(file)]?.verdict === "Duplicate" &&
                      "text-muted-foreground line-through",
                  )}
                >
                  {file.name}
                </span>
                <FileVerdictBadge verdict={verdicts[fileKey(file)]} />
                <span className="text-muted-foreground">{format.bytes(file.size)}</span>
                <Button
                  variant="text"
                  size="auto"
                  disabled={progress !== null}
                  onClick={() => setSelected((current) => current.filter((_, at) => at !== index))}
                  aria-label={t("upload.removeNamed", { fileName: file.name })}
                >
                  {t("action.remove")}
                </Button>
              </li>
            ))}
          </ul>

          {progress !== null ? (
            <Progress value={progress.percent}>
              {progress.percent >= 100
                ? t("upload.readingTags")
                : progress.fileCount > 1
                  ? t("upload.uploadingFile", {
                      index: progress.fileIndex + 1,
                      count: progress.fileCount,
                      progress: progress.percent,
                    })
                  : t("upload.uploading", { progress: progress.percent })}
            </Progress>
          ) : (
            <Button
              variant="primary"
              className="self-start"
              onClick={() => void upload()}
              disabled={checksRunning > 0 || pending.length === 0}
            >
              {checksRunning > 0
                ? t("upload.checking")
                : pending.length === 0
                  ? t("upload.nothingToUpload")
                  : t("upload.submit", { count: pending.length })}
            </Button>
          )}
        </section>
      )}

      {failed.length > 0 && (
        <section className="flex flex-col gap-3">
          <SectionHeader title={t("upload.notAdded")} />
          <ul className="flex flex-col gap-0.5">
            {failed.map((failure, index) => (
              <li
                key={`${failure.fileName}-${index}`}
                className="flex items-center gap-3.5 rounded-md border-l-[3px] border-destructive bg-card px-3 py-2.5 text-sm"
              >
                <span className="min-w-28 flex-1 truncate">{failure.fileName}</span>
                <span className="text-destructive">{failure.reason}</span>
              </li>
            ))}
          </ul>
        </section>
      )}

      {uploaded.length > 0 && (
        <section className="flex flex-col gap-3">
          <SectionHeader title={t("upload.justAdded")}>
            <Button variant="text" size="auto" asChild>
              <Link href="/tracks">{t("upload.goToLibrary")}</Link>
            </Button>
          </SectionHeader>
          <p className="text-sm text-muted-foreground">
            {isAdmin ? t("upload.metadataHintAdmin") : t("upload.metadataHintUser")}
          </p>
          <TrackList tracks={uploaded} onChanged={() => setUploaded([])} />
        </section>
      )}
    </>
  );
}
