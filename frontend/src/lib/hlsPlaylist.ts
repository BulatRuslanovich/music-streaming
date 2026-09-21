// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

/**
 * Ссылки на ресурсы из плейлиста HLS: всё, что не директива и не пустая строка. Директивы
 * начинаются с `#`, поэтому отбор такой простой; сами адреса могут быть относительными.
 */
export function playlistUris(playlist: string): string[] {
  return playlist
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0 && !line.startsWith("#"));
}
