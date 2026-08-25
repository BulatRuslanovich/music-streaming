// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { queries } from "@/lib/queries";
import { useInvalidate } from "@/lib/useInvalidate";
import { Section } from "@/components/collection/Section";
import { Button } from "@/components/ui/button";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { FileList, FileRow } from "./FileRow";

// Пока идёт скан, опрашиваем сервер чаще: у эндпоинта нет своего потока событий.
const IDLE_POLL_MS = 30_000;
const ACTIVE_POLL_MS = 2_000;

export function ServerImport() {
  const t = useT();
  const { notify, notifyError } = useToast();
  const invalidate = useInvalidate();

  const status = useQuery({
    ...queries.libraryImport(),
    refetchInterval: (query) => (query.state.data?.running ? ACTIVE_POLL_MS : IDLE_POLL_MS),
  });

  const scan = useMutation({
    mutationFn: () => api.startImport(),
    onSuccess: (result) => {
      if (result.imported > 0) {
        notify(t("import.done", { count: result.imported }), "success");
        invalidate("library");
      } else if (result.failed > 0) {
        notify(t("import.allFailed", { count: result.failed }), "error");
      } else {
        notify(t("import.nothingToDo"), "info");
      }

      void status.refetch();
    },
    onError: (reason) => notifyError(reason, t("import.failed")),
  });

  const data = status.data;

  if (!data?.enabled) return null;

  const running = data.running || scan.isPending;

  return (
    <Section
      title={t("import.title")}
      actions={
        <Button variant="text" size="auto" disabled={running} onClick={() => scan.mutate()}>
          {running ? t("import.scanning") : t("import.scanNow")}
        </Button>
      }
    >
      <p className="text-sm text-muted-foreground">
        {t("import.hint")} <code className="text-foreground">{data.directory}</code>
      </p>

      <p className="text-sm text-muted-foreground">
        {running
          ? data.currentFile
            ? t("import.progress", { fileName: data.currentFile, count: data.pending })
            : t("import.starting")
          : data.waiting > 0
            ? t("import.waiting", { count: data.waiting })
            : t("import.idle")}
      </p>

      {(data.imported > 0 || data.failed > 0) && (
        <p className="text-sm font-semibold">
          {t("import.summary", { imported: data.imported, failed: data.failed })}
        </p>
      )}

      {data.recentFailures.length > 0 && (
        <FileList>
          {data.recentFailures.map((failure, index) => (
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
      )}
    </Section>
  );
}
