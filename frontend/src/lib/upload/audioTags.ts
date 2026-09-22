// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { latin1, readBytes } from "./fileBytes";
import { readFlacTags } from "./flac";
import { readId3Tags } from "./id3";
import { readMp4Tags } from "./mp4";

type AudioTags = { title?: string; artist?: string };

const SniffBytes = 12;

export async function readAudioTags(file: File): Promise<AudioTags> {
  try {
    const head = await readBytes(file, 0, SniffBytes);
    if (head.length < SniffBytes) return {};

    if (latin1(head, 0, 3) === "ID3") return await readId3Tags(file);
    if (latin1(head, 0, 4) === "fLaC") return await readFlacTags(file);
    if (latin1(head, 4, 4) === "ftyp") return await readMp4Tags(file);

    return {};
  } catch {
    return {};
  }
}
