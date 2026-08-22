// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { latin1, littleEndian, readBytes } from "./fileBytes";

export type FlacTags = { title?: string; artist?: string };

const MagicBytes = 4;

const BlockHeaderBytes = 4;

const VorbisCommentBlock = 4;

const MaxBlocks = 64;

const MaxCommentBytes = 1024 * 1024;

/** Reads the Vorbis comments FLAC keeps instead of ID3 frames. */
export async function readFlacTags(file: File): Promise<FlacTags> {
  try {
    return await parse(file);
  } catch {
    return {};
  }
}

async function parse(file: File): Promise<FlacTags> {
  const magic = await readBytes(file, 0, MagicBytes);
  if (magic.length < MagicBytes || latin1(magic, 0, MagicBytes) !== "fLaC") return {};

  let offset = MagicBytes;

  for (let block = 0; block < MaxBlocks; block++) {
    const header = await readBytes(file, offset, BlockHeaderBytes);
    if (header.length < BlockHeaderBytes) return {};

    const last = (header[0] & 0x80) !== 0;
    const kind = header[0] & 0x7f;
    const length = (header[1] << 16) | (header[2] << 8) | header[3];

    offset += BlockHeaderBytes;

    if (kind === VorbisCommentBlock)
      return readComments(await readBytes(file, offset, Math.min(length, MaxCommentBytes)));

    if (last) return {};

    offset += length;
  }

  return {};
}

function readComments(body: Uint8Array): FlacTags {
  const decoder = new TextDecoder("utf-8");

  // The vendor string comes first and is of no interest, but its length is.
  let at = 4 + littleEndian(body, 0, 4);
  if (at + 4 > body.length) return {};

  const count = littleEndian(body, at, 4);
  at += 4;

  const tags: FlacTags = {};
  let albumArtist: string | undefined;

  for (let entry = 0; entry < count && at + 4 <= body.length; entry++) {
    const size = littleEndian(body, at, 4);
    at += 4;

    if (at + size > body.length) break;

    const text = decoder.decode(body.subarray(at, at + size));
    at += size;

    const separator = text.indexOf("=");
    if (separator <= 0) continue;

    const value = text.slice(separator + 1).trim();
    if (value.length === 0) continue;

    switch (text.slice(0, separator).toUpperCase()) {
      case "TITLE":
        tags.title ??= value;
        break;
      case "ARTIST":
        tags.artist ??= value;
        break;
      case "ALBUMARTIST":
      case "ALBUM ARTIST":
        albumArtist ??= value;
        break;
    }

    if (tags.title && tags.artist) break;
  }

  tags.artist ??= albumArtist;

  return tags;
}
