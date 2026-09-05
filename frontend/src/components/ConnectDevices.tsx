// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useState } from "react";
import { MonitorSmartphone } from "lucide-react";
import { useConnect } from "@/contexts/ConnectContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import type { ConnectCommandKind, ConnectDevice } from "@/lib/connect";
import { Seekbar } from "./Seekbar";
import { Badge } from "./ui/badge";
import { Button } from "./ui/button";
import { Dialog, DialogContent } from "./ui/dialog";
import { NextIcon, PauseIcon, PlayIcon, PreviousIcon, VolumeIcon } from "./Icons";

export function ConnectDevices() {
  const t = useT();
  const connect = useConnect();
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button
        variant="ghost"
        size="icon"
        onClick={() => setOpen(true)}
        title={t("connect.title")}
        aria-label={t("connect.title")}
      >
        <MonitorSmartphone size={20} />
      </Button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent title={t("connect.title")} description={t("connect.hint")}>
          {!connect.connected && <p role="status">{t("connect.unavailable")}</p>}
          <div className="space-y-4">
            {connect.devices.map((device) => (
              <Device key={device.id} device={device} />
            ))}
          </div>
        </DialogContent>
      </Dialog>
    </>
  );
}

function Device({ device }: { device: ConnectDevice }) {
  const connect = useConnect();
  const { notify } = useToast();
  const t = useT();
  const [busy, setBusy] = useState(false);
  const here = device.id === connect.id;
  const local = connect.devices.find((entry) => entry.id === connect.id);
  async function send(
    kind: ConnectCommandKind,
    value?: number,
    target = device.id,
    source?: string,
  ) {
    setBusy(true);
    try {
      await connect.send(target, kind, value, source);
    } catch {
      notify(t("connect.failed"), "error");
    } finally {
      setBusy(false);
    }
  }
  return (
    <section className="flex flex-col gap-3 rounded-lg border border-border p-4">
      <div className="flex items-center gap-2">
        <h3 className="min-w-0 flex-1 truncate font-semibold">{device.name}</h3>
        {here && <Badge>{t("connect.thisDevice")}</Badge>}
      </div>

      <p className="truncate text-sm text-muted-foreground">{device.title ?? t("player.idle")}</p>

      {/* Пульт повторяет транспорт плеера иконками, а не подписями: тремя кнопками с полными
          названиями действий ряд разъезжался на две строки, и «+15 с» оставалось висеть
          отдельной строкой под управлением. */}
      {device.title && (
        <div className="flex flex-wrap items-center gap-1">
          <Button
            variant="ghost"
            size="icon"
            disabled={busy}
            onClick={() => void send("previous")}
            aria-label={t("player.previousTrack")}
            title={t("player.previousTrack")}
          >
            <PreviousIcon size={20} />
          </Button>

          <Button
            variant="play"
            size="icon"
            disabled={busy}
            onClick={() => void send(device.isPlaying ? "pause" : "play")}
            aria-label={t(device.isPlaying ? "action.pause" : "action.play")}
            title={t(device.isPlaying ? "action.pause" : "action.play")}
          >
            {device.isPlaying ? <PauseIcon size={18} /> : <PlayIcon size={18} />}
          </Button>

          <Button
            variant="ghost"
            size="icon"
            disabled={busy}
            onClick={() => void send("next")}
            aria-label={t("player.nextTrack")}
            title={t("player.nextTrack")}
          >
            <NextIcon size={20} />
          </Button>

          <div className="ml-auto flex items-center gap-1">
            <Button
              size="sm"
              variant="ghost"
              disabled={busy}
              onClick={() => void send("seek", Math.max(0, device.position - 15))}
            >
              −15 {t("connect.seconds")}
            </Button>
            <Button
              size="sm"
              variant="ghost"
              disabled={busy}
              onClick={() => void send("seek", device.position + 15)}
            >
              +15 {t("connect.seconds")}
            </Button>
          </div>
        </div>
      )}

      <div className="flex items-center gap-3">
        <VolumeIcon size={18} className="shrink-0 text-muted-foreground" />
        <Seekbar
          key={`${device.volume}:${device.muted}`}
          value={device.muted ? 0 : device.volume}
          max={1}
          step={0.05}
          commitOnRelease
          onSeek={(value) => void send("volume", value)}
          ariaLabel={t("connect.volume")}
          className="volume-seek max-w-56 flex-1"
        />
      </div>

      {!here && (
        <div className="flex flex-wrap gap-2">
          {device.title && (
            <Button
              size="sm"
              disabled={busy}
              onClick={() => void send("transfer", undefined, connect.id, device.id)}
            >
              {t("connect.continueHere")}
            </Button>
          )}
          {local?.title && (
            <Button
              size="sm"
              variant="outline"
              disabled={busy}
              onClick={() => void send("transfer", undefined, device.id, connect.id)}
            >
              {t("connect.playThere")}
            </Button>
          )}
        </div>
      )}
    </section>
  );
}
