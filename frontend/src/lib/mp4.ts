// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { bigEndian, latin1, readBytes } from "./fileBytes";

export type Mp4Tags = { title?: string; artist?: string };

interface Box {
  contentAt: number;
  end: number;
}

const HeaderBytes = 8;

const ExtendedHeaderBytes = 16;

const FullBoxBytes = 4;

const MaxBoxes = 128;

const MaxItemListBytes = 1024 * 1024;

const TitleAtom = "©nam";

const ArtistAtom = "©ART";

const AlbumArtistAtom = "aART";

const Utf8Payload = 1;

export async function readMp4Tags(file: File): Promise<Mp4Tags> {
  try {
    const items = await findItemList(file);
    return items ? readItems(items) : {};
  } catch {
    return {};
  }
}

async function findItemList(file: File): Promise<Uint8Array | null> {
  const header = await readBytes(file, 0, HeaderBytes);
  if (header.length < HeaderBytes || latin1(header, 4, 4) !== "ftyp") return null;

  let from = 0;
  let to = file.size;

  for (const wanted of ["moov", "udta", "meta", "ilst"]) {
    const box = await findBox(file, from, to, wanted);
    if (box === null) return null;

    from = box.contentAt + (wanted === "meta" ? FullBoxBytes : 0);
    to = box.end;
  }

  return await readBytes(file, from, Math.min(to - from, MaxItemListBytes));
}

async function findBox(file: File, from: number, to: number, type: string): Promise<Box | null> {
  let offset = from;

  for (let box = 0; box < MaxBoxes && offset + HeaderBytes <= to; box++) {
    const header = await readBytes(file, offset, ExtendedHeaderBytes);
    if (header.length < HeaderBytes) return null;

    let size = bigEndian(header, 0, 4);
    let headerBytes = HeaderBytes;

    if (size === 1) {
      if (header.length < ExtendedHeaderBytes) return null;

      size = bigEndian(header, 8, 8);
      headerBytes = ExtendedHeaderBytes;
    } else if (size === 0) {
      size = to - offset;
    }

    if (size < headerBytes || offset + size > to) return null;

    if (latin1(header, 4, 4) === type)
      return { contentAt: offset + headerBytes, end: offset + size };

    offset += size;
  }

  return null;
}

function readItems(items: Uint8Array): Mp4Tags {
  const tags: Mp4Tags = {};
  let albumArtist: string | undefined;

  let offset = 0;

  for (let box = 0; box < MaxBoxes && offset + HeaderBytes <= items.length; box++) {
    const size = bigEndian(items, offset, 4);
    if (size < HeaderBytes || offset + size > items.length) break;

    const atom = latin1(items, offset + 4, 4);
    const value = readData(items.subarray(offset + HeaderBytes, offset + size));

    if (value !== undefined) {
      if (atom === TitleAtom) tags.title ??= value;
      else if (atom === ArtistAtom) tags.artist ??= value;
      else if (atom === AlbumArtistAtom) albumArtist ??= value;
    }

    if (tags.title && tags.artist) break;

    offset += size;
  }

  tags.artist ??= albumArtist;

  return tags;
}

function readData(item: Uint8Array): string | undefined {
  const PayloadAt = HeaderBytes + 8;

  let offset = 0;

  while (offset + HeaderBytes <= item.length) {
    const size = bigEndian(item, offset, 4);
    if (size < HeaderBytes || offset + size > item.length) return undefined;

    if (latin1(item, offset + 4, 4) === "data" && size > PayloadAt) {
      if (bigEndian(item, offset + HeaderBytes, 4) !== Utf8Payload) return undefined;

      const text = new TextDecoder("utf-8")
        .decode(item.subarray(offset + PayloadAt, offset + size))
        .trim();

      return text.length > 0 ? text : undefined;
    }

    offset += size;
  }

  return undefined;
}
