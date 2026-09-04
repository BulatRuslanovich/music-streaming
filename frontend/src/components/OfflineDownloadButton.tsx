// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useState } from "react";
import type { Track } from "@/lib/types";
import { useOfflineDownloads } from "@/contexts/OfflineDownloadsContext";
import { useSettings } from "@/contexts/SettingsContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { DownloadIcon } from "@/components/Icons";
import { Button } from "@/components/ui/button";

export function OfflineDownloadButton({ tracks }: { tracks: Track[] }) {
  const t = useT();
  const offline = useOfflineDownloads();
  const settings = useSettings();
  const { notify, notifyError } = useToast();
  const [submitting, setSubmitting] = useState(false);

  const selected = new Set(tracks.map((track) => track.id));
  const records = offline.tracks.filter((entry) => selected.has(entry.track.id));
  const allReady =
    tracks.length > 0 &&
    tracks.every((track) =>
      records.some((entry) => entry.track.id === track.id && entry.state === "ready"),
    );
  const active = records.some(
    (entry) => entry.state === "preparing" || entry.state === "downloading",
  );

  if (!settings.hlsEnabled && records.length === 0) return null;

  const download = async () => {
    setSubmitting(true);

    try {
      const quality =
        settings.effectiveQuality === "Original" ? "Normal" : settings.effectiveQuality;
      await offline.download(tracks, quality);
      notify(t("offline.collectionReady"), "success");
    } catch (error) {
      notifyError(error, t("menu.offlineFailed"));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Button
      variant="secondary"
      disabled={!offline.loaded || tracks.length === 0 || allReady || active || submitting}
      onClick={() => void download()}
    >
      <DownloadIcon size={16} />
      {submitting || active
        ? t("offline.collectionDownloading")
        : allReady
          ? t("offline.collectionReadyLabel")
          : t("offline.collectionDownload")}
    </Button>
  );
}
