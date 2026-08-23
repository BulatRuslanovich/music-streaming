// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { fileKey, isDuplicate, type FileCheck } from "@/lib/uploadCheck";
import { useFormat } from "@/lib/useFormat";
import { useAuth } from "@/contexts/AuthContext";
import { useSettings } from "@/contexts/SettingsContext";
import { useUpload } from "@/contexts/UploadContext";
import { cn } from "@/lib/cn";
import type { Track } from "@/lib/types";
import { Section } from "@/components/collection/Section";
import { TrackList } from "@/components/TrackList";
import { PageHeader } from "@/components/PageHeader";
import { Button } from "@/components/ui/button";
import { Progress } from "@/components/ui/progress";
import { useT } from "@/contexts/I18nContext";
import { Dropzone } from "./Dropzone";
import { FileList, FileRow } from "./FileRow";

const PARTIAL_COMPARISON = {
  None: "upload.notCompared",
  Tags: "upload.tagsOnly",
  Hash: "upload.hashOnly",
} as const;

function FileCheckBadge({ check }: { check?: FileCheck }) {
  const t = useT();

  if (!check) return null;

  if (check.state === "failed") return <Note tone="warning">{t("upload.notChecked")}</Note>;

  if (check.verdict === "Duplicate")
    return (
      <Note tone="faint">
        {t("upload.duplicate")}
        <MatchedTrack track={check.match} />
      </Note>
    );

  if (check.verdict === "Similar")
    return (
      <Note tone="warning">
        {t("upload.similar")}
        <MatchedTrack track={check.match} />
      </Note>
    );

  // Nothing was found, but say so only as loudly as the comparison deserves.
  if (check.basis === "HashAndTags") return null;

  return <Note tone="warning">{t(PARTIAL_COMPARISON[check.basis])}</Note>;
}

function Note({ tone, children }: { tone: "faint" | "warning"; children: ReactNode }) {
  return (
    <span
      className={cn(
        "flex min-w-0 items-baseline gap-2 text-xs font-semibold whitespace-nowrap",
        tone === "faint" ? "text-faint" : "text-warning",
        // На узком экране заметка занимает свою строку и переносится: `nowrap` наезжал на размер
        // файла и кнопку «Убрать», когда текст длинный («сверен только по содержимому…»).
        "max-[620px]:flex-[1_0_100%] max-[620px]:whitespace-normal",
      )}
    >
      {children}
    </span>
  );
}

function MatchedTrack({ track }: { track: Track | null }) {
  if (!track) return null;

  return (
    <span className="min-w-0 truncate font-normal text-muted-foreground">
      {`${track.artistName} — ${track.title}`}
    </span>
  );
}

export default function UploadPage() {
  const t = useT();
  const format = useFormat();

  const { isAdmin } = useAuth();
  const { maxUploadBytes } = useSettings();
  const {
    queue,
    checks,
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

  const totalSize = pending.reduce((sum, file) => sum + file.size, 0);
  const uploading = progress !== null;

  return (
    <>
      <PageHeader
        title={t("nav.upload")}
        subtitle={t("upload.subtitle", { limit: format.bytes(maxUploadBytes) })}
      />

      <Dropzone onFiles={add} disabled={uploading} />

      {queue.length > 0 && (
        <Section
          title={`${t("upload.ready", { count: pending.length })} · ${format.bytes(totalSize)}${
            duplicates.length > 0 ? ` · ${t("upload.skipped", { count: duplicates.length })}` : ""
          }`}
          actions={
            <Button variant="text" size="auto" onClick={clearQueue} disabled={uploading}>
              {t("action.clear")}
            </Button>
          }
        >
          <FileList>
            {queue.map((file, index) => (
              <FileRow
                key={`${file.name}-${file.size}-${index}`}
                name={file.name}
                muted={isDuplicate(checks[fileKey(file)])}
                status={<FileCheckBadge check={checks[fileKey(file)]} />}
                meta={format.bytes(file.size)}
                action={
                  <Button
                    variant="text"
                    size="auto"
                    disabled={uploading}
                    onClick={() => remove(index)}
                    aria-label={t("upload.removeNamed", { fileName: file.name })}
                  >
                    {t("action.remove")}
                  </Button>
                }
              />
            ))}
          </FileList>

          {uploading ? (
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
              size="lg"
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
        </Section>
      )}

      {failed.length > 0 && (
        <Section
          title={t("upload.notAdded")}
          actions={
            <Button variant="text" size="auto" onClick={clearFailed}>
              {t("action.clear")}
            </Button>
          }
        >
          <FileList>
            {failed.map((failure, index) => (
              <FileRow
                key={`${failure.fileName}-${index}`}
                name={failure.fileName}
                tone="destructive"
                status={
                  <span className="min-w-0 truncate text-xs font-semibold text-destructive">
                    {failure.reason}
                  </span>
                }
              />
            ))}
          </FileList>
        </Section>
      )}

      {uploaded.length > 0 && (
        <Section
          title={t("upload.justAdded")}
          actions={
            <>
              <Button variant="text" size="auto" onClick={clearUploaded}>
                {t("action.clear")}
              </Button>
              <Button variant="text" size="auto" asChild>
                <Link href="/tracks">{t("upload.goToLibrary")}</Link>
              </Button>
            </>
          }
        >
          <p className="text-sm text-muted-foreground">
            {isAdmin ? t("upload.metadataHintAdmin") : t("upload.metadataHintUser")}
          </p>
          <TrackList tracks={uploaded} onChanged={clearUploaded} />
        </Section>
      )}
    </>
  );
}
