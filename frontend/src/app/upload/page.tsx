"use client";

import Link from "next/link";
import { useRef, useState } from "react";
import { ACCEPT_ATTRIBUTE } from "@/lib/audioFormats";
import { fileKey, type FileVerdict } from "@/lib/uploadCheck";
import { useFormat } from "@/lib/useFormat";
import { useAuth } from "@/contexts/AuthContext";
import { useSettings } from "@/contexts/SettingsContext";
import { useUpload } from "@/contexts/UploadContext";
import { cn } from "@/lib/cn";
import { UploadIcon } from "@/components/Icons";
import { TrackList } from "@/components/TrackList";
import { PageHeader, SectionHeader } from "@/components/PageHeader";
import { Button } from "@/components/ui/button";
import { Progress } from "@/components/ui/progress";

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

/**
 * Страница — только вид: очередь, ход отправки и итоги живут в <UploadProvider>, потому что уход
 * отсюда не останавливает загрузку и не должен стирать её след.
 */
export default function UploadPage() {
  const t = useT();
  const format = useFormat();

  const { isAdmin } = useAuth();
  const { maxUploadBytes } = useSettings();
  const {
    queue,
    verdicts,
    pending,
    duplicates,
    progress,
    checking,
    uploaded,
    failed,
    add,
    remove,
    clearQueue,
    start,
    clearUploaded,
    clearFailed,
  } = useUpload();

  const inputRef = useRef<HTMLInputElement | null>(null);
  const [dragging, setDragging] = useState(false);

  const totalSize = pending.reduce((sum, file) => sum + file.size, 0);

  return (
    <>
      <PageHeader
        title={t("nav.upload")}
        subtitle={t("upload.subtitle", { limit: format.bytes(maxUploadBytes) })}
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
          add(event.dataTransfer.files);
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
          onChange={(event) => {
            add(event.target.files);

            // Иначе повторный выбор того же файла не считается изменением и не приходит вовсе.
            event.target.value = "";
          }}
        />
      </div>

      {queue.length > 0 && (
        <section className="flex flex-col gap-3.5">
          <SectionHeader
            title={`${t("upload.ready", { count: pending.length })} · ${format.bytes(totalSize)}${
              duplicates.length > 0 ? ` · ${t("upload.skipped", { count: duplicates.length })}` : ""
            }`}
          >
            <Button variant="text" size="auto" onClick={clearQueue} disabled={progress !== null}>
              {t("action.clear")}
            </Button>
          </SectionHeader>

          <ul className="flex flex-col gap-0.5">
            {queue.map((file, index) => (
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
                  onClick={() => remove(index)}
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
              onClick={start}
              disabled={checking > 0 || pending.length === 0}
            >
              {checking > 0
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
          <SectionHeader title={t("upload.notAdded")}>
            <Button variant="text" size="auto" onClick={clearFailed}>
              {t("action.clear")}
            </Button>
          </SectionHeader>
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
            <Button variant="text" size="auto" onClick={clearUploaded}>
              {t("action.clear")}
            </Button>
            <Button variant="text" size="auto" asChild>
              <Link href="/tracks">{t("upload.goToLibrary")}</Link>
            </Button>
          </SectionHeader>
          <p className="text-sm text-muted-foreground">
            {isAdmin ? t("upload.metadataHintAdmin") : t("upload.metadataHintUser")}
          </p>
          <TrackList tracks={uploaded} onChanged={clearUploaded} />
        </section>
      )}
    </>
  );
}
