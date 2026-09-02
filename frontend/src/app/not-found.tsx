// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import { useState } from "react";
import { NoteIcon } from "@/components/Icons";
import { StatusPage } from "@/components/StatusPage";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useT } from "@/contexts/I18nContext";
import { usePlayerActions } from "@/contexts/PlayerContext";
import { useToast } from "@/contexts/ToastContext";

export default function NotFound() {
  const t = useT();
  const { playTrack } = usePlayerActions();
  const { notifyError } = useToast();
  const [starting, setStarting] = useState(false);

  // Единственная страница, куда попадают только по сломанной ссылке: подобрать здесь случайный
  // трек дешевле, чем отправлять человека обратно ни с чем.
  const playAnything = async () => {
    setStarting(true);

    try {
      const [track] = await api.shuffleTracks({ limit: 1 });
      if (track) playTrack(track, [track], { source: "tracks" });
    } catch (error) {
      notifyError(error, t("error.load"));
    } finally {
      setStarting(false);
    }
  };

  return (
    <StatusPage
      icon={<NoteIcon size={32} />}
      title={t("error.notFoundTitle")}
      description={t("error.notFoundDescription")}
      actions={
        <>
          <Button variant="primary" asChild>
            <Link href="/">{t("action.goHome")}</Link>
          </Button>
          <Button onClick={() => void playAnything()} disabled={starting}>
            {t("error.notFoundPlayAnyway")}
          </Button>
        </>
      }
    />
  );
}
