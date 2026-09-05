// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { RepeatMode } from "./playerTypes";

export interface ConnectSnapshot {
  queue: string[];
  order: number[];
  index: number;
  position: number;
  isPlaying: boolean;
  volume: number;
  muted: boolean;
  shuffle: boolean;
  repeat: RepeatMode;
  title: string | null;
}
export interface ConnectDevice {
  id: string;
  name: string;
  position: number;
  isPlaying: boolean;
  volume: number;
  muted: boolean;
  title: string | null;
  updatedAt: string;
}
export type ConnectCommandKind =
  "play" | "pause" | "next" | "previous" | "seek" | "volume" | "transfer";
export interface ConnectCommand {
  id: string;
  kind: ConnectCommandKind;
  value: number | null;
  state: ConnectSnapshot | null;
  expiresAt: string;
}
export interface ConnectPoll {
  devices: ConnectDevice[];
  commands: ConnectCommand[];
  serverTime: string;
}

export function deviceName(agent: string): string {
  const platform = /iPad/.test(agent)
    ? "iPad"
    : /iPhone/.test(agent)
      ? "iPhone"
      : /Android/.test(agent)
        ? "Android"
        : /Windows/.test(agent)
          ? "Windows"
          : /Macintosh/.test(agent)
            ? "Mac"
            : /Linux/.test(agent)
              ? "Linux"
              : "Browser";
  const browser = /Edg\//.test(agent)
    ? "Edge"
    : /Firefox\//.test(agent)
      ? "Firefox"
      : /Chrome\//.test(agent)
        ? "Chrome"
        : /Safari\//.test(agent)
          ? "Safari"
          : "";
  return [platform, browser].filter(Boolean).join(" · ");
}
