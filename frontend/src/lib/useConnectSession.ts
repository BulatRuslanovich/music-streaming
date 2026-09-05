// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api } from "./api";
import { deviceId } from "./events";
import {
  deviceName,
  type ConnectCommand,
  type ConnectCommandKind,
  type ConnectDevice,
  type ConnectSnapshot,
} from "./connect";
import { useAuth } from "@/contexts/AuthContext";
import { useToast } from "@/contexts/ToastContext";
import { useT } from "@/contexts/I18nContext";

export function useConnectSession(
  snapshot: () => ConnectSnapshot,
  execute: (command: ConnectCommand, signal: AbortSignal) => Promise<void>,
) {
  const { user } = useAuth();
  const { notify } = useToast();
  const t = useT();
  const [id, setId] = useState("");
  const [devices, setDevices] = useState<ConnectDevice[]>([]);
  const [connected, setConnected] = useState(false);
  const current = useRef({ snapshot, execute, notify, t });
  useEffect(() => {
    current.current = { snapshot, execute, notify, t };
  });
  const userId = user?.id;

  useEffect(() => {
    if (!userId) return;
    const controller = new AbortController();
    const localId = deviceId();
    const name = deviceName(navigator.userAgent);
    let timer: ReturnType<typeof setTimeout>;
    const processed = new Set<string>();
    let acknowledged: string[] = [];
    async function poll() {
      try {
        const sentAt = performance.now();
        const signal = AbortSignal.any([controller.signal, AbortSignal.timeout(10_000)]);
        const result = await api.connectPoll(
          localId,
          name,
          current.current.snapshot(),
          acknowledged,
          signal,
        );
        if (controller.signal.aborted) return;
        acknowledged = [];
        setId(localId);
        setDevices(result.devices);
        setConnected(true);
        for (const command of result.commands) {
          acknowledged.push(command.id);
          if (processed.has(command.id)) continue;
          processed.add(command.id);
          if (processed.size > 256) processed.delete(processed.values().next().value!);
          const remaining =
            Date.parse(command.expiresAt) -
            Date.parse(result.serverTime) -
            (performance.now() - sentAt);
          if (remaining <= 0) continue;
          const commandSignal = AbortSignal.any([
            controller.signal,
            AbortSignal.timeout(Math.floor(remaining)),
          ]);
          try {
            await current.current.execute(command, commandSignal);
          } catch {
            if (!controller.signal.aborted)
              current.current.notify(current.current.t("connect.failed"), "error");
          }
          if (controller.signal.aborted) return;
        }
      } catch {
        if (!controller.signal.aborted) {
          setConnected(false);
          setDevices([]);
        }
      } finally {
        if (!controller.signal.aborted) timer = setTimeout(() => void poll(), 2000);
      }
    }
    void poll();
    return () => {
      controller.abort();
      clearTimeout(timer);
    };
  }, [userId]);

  const send = useCallback(
    async (target: string, kind: ConnectCommandKind, value?: number, source?: string) => {
      await api.connectCommand(target, kind, value, source);
    },
    [],
  );
  return useMemo(
    () => ({ id, devices: userId ? devices : [], connected: !!userId && connected, send }),
    [id, devices, connected, send, userId],
  );
}
