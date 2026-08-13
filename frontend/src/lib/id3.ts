export type Id3Tags = { title?: string; artist?: string };

const HeaderBytes = 10;

const MaxTagBytes = 1024 * 1024;

export async function readId3Tags(file: File): Promise<Id3Tags> {
  try {
    return await parse(file);
  } catch {
    return {};
  }
}

async function parse(file: File): Promise<Id3Tags> {
  const header = await readBytes(file, 0, HeaderBytes);
  if (header.length < HeaderBytes) return {};

  if (header[0] !== 0x49 || header[1] !== 0x44 || header[2] !== 0x33) return {};

  const major = header[3];
  if (major < 2 || major > 4) return {};

  const unsynchronised = (header[5] & 0x80) !== 0;
  if (unsynchronised) return {};

  const tagSize = synchsafe(header, 6);
  if (tagSize <= 0) return {};

  const body = await readBytes(file, HeaderBytes, Math.min(tagSize, MaxTagBytes));
  const start = (header[5] & 0x40) !== 0 ? extendedHeaderSize(body, major) : 0;

  return readFrames(body, major, start);
}

function readFrames(body: Uint8Array, major: number, start: number): Id3Tags {
  const idLength = major === 2 ? 3 : 4;
  const frameHeader = major === 2 ? 6 : 10;

  const titleId = major === 2 ? "TT2" : "TIT2";
  const artistId = major === 2 ? "TP1" : "TPE1";

  const tags: Id3Tags = {};

  let offset = start;
  while (offset + frameHeader <= body.length) {
    const id = ascii(body, offset, idLength);

    if (id.charCodeAt(0) === 0) break;

    const size = frameSize(body, offset + idLength, major);
    const contentAt = offset + frameHeader;
    if (size <= 0 || contentAt + size > body.length) break;

    if (id === titleId) tags.title ??= <string>decodeText(body.subarray(contentAt, contentAt + size));
    else if (id === artistId) tags.artist ??= <string>decodeText(body.subarray(contentAt, contentAt + size));

    if (tags.title && tags.artist) break;

    offset = contentAt + size;
  }

  return tags;
}

function extendedHeaderSize(body: Uint8Array, major: number): number {
  if (major === 4) return synchsafe(body, 0);
  if (major === 3) return 4 + bigEndian(body, 0, 4);
  return 0;
}

function frameSize(body: Uint8Array, at: number, major: number): number {
  if (major === 2) return bigEndian(body, at, 3);
  if (major === 3) return bigEndian(body, at, 4);

  const plain = body[at] >= 0x80 || body[at + 1] >= 0x80 || body[at + 2] >= 0x80 || body[at + 3] >= 0x80;
  return plain ? bigEndian(body, at, 4) : synchsafe(body, at);
}

function decodeText(frame: Uint8Array): string | undefined {
  if (frame.length < 2) return undefined;

  const encoding = frame[0];
  let content = frame.subarray(1);
  let label = encoding === 2 ? "utf-16be" : encoding === 3 ? "utf-8" : "latin1";

  if (encoding === 1) {
    const bigEndianMark = content[0] === 0xfe && content[1] === 0xff;
    const littleEndianMark = content[0] === 0xff && content[1] === 0xfe;

    label = bigEndianMark ? "utf-16be" : "utf-16le";
    if (bigEndianMark || littleEndianMark) content = content.subarray(2);
  }

  const text = new TextDecoder(label).decode(content);

  const end = text.indexOf("\0");
  const value = (end === -1 ? text : text.slice(0, end)).trim();

  return value.length > 0 ? value : undefined;
}

async function readBytes(file: File, from: number, length: number): Promise<Uint8Array> {
  return new Uint8Array(await file.slice(from, from + length).arrayBuffer());
}

function ascii(bytes: Uint8Array, at: number, length: number): string {
  let value = "";
  for (let index = 0; index < length; index++) value += String.fromCharCode(bytes[at + index]);
  return value;
}

function bigEndian(bytes: Uint8Array, at: number, length: number): number {
  let value = 0;
  for (let index = 0; index < length; index++) value = (value << 8) | bytes[at + index];
  return value;
}

function synchsafe(bytes: Uint8Array, at: number): number {
  return (
    ((bytes[at] & 0x7f) << 21) |
    ((bytes[at + 1] & 0x7f) << 14) |
    ((bytes[at + 2] & 0x7f) << 7) |
    (bytes[at + 3] & 0x7f)
  );
}
