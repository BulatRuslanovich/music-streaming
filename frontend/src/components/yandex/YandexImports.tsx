// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { useEffect, useRef } from "react";
import { HeartIcon, XIcon } from "lucide-react";
import { api } from "@/lib/api";
import { queries } from "@/lib/queries";
import { useInvalidate } from "@/lib/useInvalidate";
import type { YandexImport } from "@/lib/types";
import { FileList, FileRow } from "@/app/upload/FileRow";
import { Section } from "@/components/PageHeader";
import { Button } from "@/components/ui/button";
import { useT } from "@/contexts/I18nContext";
import { useStartYandexImport } from "./useStartYandexImport";

function isActive(job: YandexImport): boolean {
  return job.status === "Queued" || job.status === "Running";
}

export function YandexImports() {
  const t = useT();
  const invalidate = useInvalidate();
  const imports = useQuery(queries.yandexImports());
  const start = useStartYandexImport();
  const remove = useMutation({
    mutationFn: (id: string) => api.removeYandexImport(id),
    onSuccess: () => void imports.refetch(),
  });

  const jobs = imports.data ?? [];

  const added = jobs.reduce((sum, job) => sum + job.added, 0);
  const lastAdded = useRef(added);
  useEffect(() => {
    if (added > lastAdded.current) invalidate("library");
    lastAdded.current = added;
  }, [added, invalidate]);

  const likesRunning = jobs.some((job) => job.kind === "Likes" && isActive(job));

  return (
    <Section
      title={t("yandex.title")}
      actions={
        <Button
          variant="outline"
          size="sm"
          disabled={likesRunning || start.isPending}
          onClick={() => start.mutate({ kind: "Likes" })}
        >
          <HeartIcon size={14} /> {t("yandex.importLikes")}
        </Button>
      }
    >
      {jobs.length > 0 && (
        <FileList>
          {jobs.map((job) => (
            <FileRow
              key={job.id}
              name={job.kind === "Likes" ? t("yandex.likes") : (job.title ?? t("yandex.title"))}
              tone={job.status === "Failed" ? "destructive" : "neutral"}
              status={<JobStatus job={job} />}
              meta={job.total > 0 ? `${job.done}/${job.total}` : undefined}
              action={
                <Button
                  variant="ghost"
                  size="icon-sm"
                  disabled={remove.isPending}
                  onClick={() => remove.mutate(job.id)}
                  aria-label={isActive(job) ? t("yandex.stop") : t("yandex.dismiss")}
                >
                  <XIcon size={16} />
                </Button>
              }
            />
          ))}
        </FileList>
      )}
    </Section>
  );
}

function JobStatus({ job }: { job: YandexImport }) {
  const t = useT();

  const summary =
    job.status === "Failed"
      ? job.error
      : job.status === "Queued"
        ? t("yandex.status.queued")
        : [
            job.current,
            t("yandex.added", { count: job.added }),
            job.skipped > 0 ? t("yandex.skipped", { count: job.skipped }) : null,
            job.status === "Cancelled" ? t("yandex.status.cancelled") : null,
          ]
            .filter(Boolean)
            .join(" · ");

  return (
    <span className="flex flex-col gap-0.5 text-xs">
      <span className={job.status === "Failed" ? "text-destructive" : "text-muted-foreground"}>
        {summary}
      </span>
      {job.failures.map((failure, index) => (
        <span key={`${failure.title}-${index}`} className="text-destructive">
          {failure.title}: {failure.reason}
        </span>
      ))}
    </span>
  );
}
