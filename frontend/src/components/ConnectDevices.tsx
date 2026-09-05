// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useState } from "react";
import { MonitorSmartphone } from "lucide-react";
import { useConnect } from "@/contexts/ConnectContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import type { ConnectCommandKind, ConnectDevice } from "@/lib/connect";
import { Button } from "./ui/button";
import { Dialog, DialogContent } from "./ui/dialog";

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
    <section className="space-y-3 rounded-lg border p-4">
      <h3 className="font-semibold">
        {device.name} {here && <span className="text-primary">· {t("connect.thisDevice")}</span>}
      </h3>
      <p className="truncate text-sm text-muted-foreground">{device.title ?? t("player.idle")}</p>
      {device.title && (
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="outline" disabled={busy} onClick={() => void send("previous")}>
            {t("player.previousTrack")}
          </Button>
          <Button
            size="sm"
            disabled={busy}
            onClick={() => void send(device.isPlaying ? "pause" : "play")}
          >
            {t(device.isPlaying ? "action.pause" : "action.play")}
          </Button>
          <Button size="sm" variant="outline" disabled={busy} onClick={() => void send("next")}>
            {t("player.nextTrack")}
          </Button>
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
      )}
      <label className="flex items-center gap-3 text-sm">
        {t("connect.volume")}
        <input
          key={`${device.volume}:${device.muted}`}
          type="range"
          min="0"
          max="1"
          step="0.05"
          defaultValue={device.muted ? 0 : device.volume}
          disabled={busy}
          onPointerUp={(event) => void send("volume", Number(event.currentTarget.value))}
          onKeyUp={(event) => void send("volume", Number(event.currentTarget.value))}
        />
      </label>
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
