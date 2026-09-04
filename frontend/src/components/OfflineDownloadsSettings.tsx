// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useState } from "react";
import { useOfflineDownloads } from "@/contexts/OfflineDownloadsContext";
import { usePlayerActions } from "@/contexts/PlayerContext";
import { useSettings } from "@/contexts/SettingsContext";
import { useT } from "@/contexts/I18nContext";
import { useFormat } from "@/lib/useFormat";
import { Button } from "@/components/ui/button";
import { DownloadIcon, PlayIcon, TrashIcon } from "@/components/Icons";

export function OfflineDownloadsSettings() {
  const offline = useOfflineDownloads();
  const player = usePlayerActions();
  const settings = useSettings();
  const format = useFormat();
  const t = useT();
  const [removing, setRemoving] = useState(false);

  const ready = offline.tracks.filter((entry) => entry.state === "ready");
  const totalBytes = offline.tracks.reduce((sum, entry) => sum + entry.downloadedBytes, 0);

  const remove = async (trackIds: string[]) => {
    setRemoving(true);
    try {
      await offline.remove(trackIds);
    } finally {
      setRemoving(false);
    }
  };

  return (
    <fieldset className="flex flex-col gap-3 border-0 p-0">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <legend className="font-semibold">{t("settings.offline")}</legend>
          <p className="mt-0.5 text-sm text-muted-foreground">
            {settings.hlsEnabled ? t("settings.offlineHint") : t("settings.offlineUnavailable")}
          </p>
        </div>

        {offline.tracks.length > 0 && (
          <Button
            variant="ghost"
            onClick={() => void remove(offline.tracks.map((entry) => entry.track.id))}
            disabled={removing}
          >
            <TrashIcon size={16} /> {t("settings.offlineRemoveAll")}
          </Button>
        )}
      </div>

      {!offline.loaded ? (
        <p className="text-sm text-muted-foreground">{t("common.loading")}</p>
      ) : offline.tracks.length === 0 ? (
        <p className="rounded-lg bg-raised px-3 py-3 text-sm text-muted-foreground">
          <DownloadIcon size={16} className="mr-2 inline" />
          {t("settings.offlineEmpty")}
        </p>
      ) : (
        <>
          <p className="text-sm text-muted-foreground">
            {t("settings.offlineUsage", {
              count: offline.tracks.length,
              size: format.bytes(totalBytes),
            })}
          </p>

          <ul className="flex flex-col gap-1.5" aria-label={t("settings.offline")}>
            {offline.tracks.map((entry) => (
              <li
                key={entry.track.id}
                className="flex min-w-0 items-center gap-3 rounded-lg bg-raised px-3 py-2.5"
              >
                <button
                  type="button"
                  className="min-w-0 flex-1 text-left disabled:cursor-default"
                  disabled={entry.state !== "ready"}
                  onClick={() => {
                    const index = ready.findIndex(
                      (candidate) => candidate.track.id === entry.track.id,
                    );
                    if (index >= 0)
                      player.playQueue(
                        ready.map((candidate) => candidate.track),
                        index,
                      );
                  }}
                >
                  <span className="block truncate text-sm font-medium">{entry.track.title}</span>
                  <span className="block truncate text-xs text-muted-foreground">
                    {/* Тир показан рядом с состоянием: сервер отдаёт офлайн-копию в лучшем из
                        нарезанных, и это может быть не тот, что выбран в настройках. */}
                    {[
                      entry.track.artistName,
                      t(`settings.offlineState.${entry.state}`),
                      ...(entry.state === "ready" ? [t(`settings.quality.${entry.quality}`)] : []),
                      format.bytes(entry.downloadedBytes),
                    ].join(" · ")}
                  </span>
                </button>

                {entry.state === "ready" && <PlayIcon size={15} className="text-primary" />}
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={t("settings.offlineRemoveNamed", { title: entry.track.title })}
                  onClick={() => void remove([entry.track.id])}
                  disabled={removing}
                >
                  <TrashIcon size={15} />
                </Button>
              </li>
            ))}
          </ul>
        </>
      )}
    </fieldset>
  );
}
