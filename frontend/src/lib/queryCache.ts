const TTL_MS = 5 * 60 * 1000;
const MAX_STORED_BYTES = 256 * 1024;
const STORAGE_PREFIX = "music-streaming.query.";

interface CacheEntry {
  data: unknown;
  storedAt: number;
}

const memory = new Map<string, CacheEntry>();

function isFresh(entry: CacheEntry): boolean {
  return Date.now() - entry.storedAt < TTL_MS;
}

function readFromSession(key: string): CacheEntry | null {
  if (typeof window === "undefined") return null;

  try {
    const raw = window.sessionStorage.getItem(STORAGE_PREFIX + key);
    if (!raw) return null;

    const entry = JSON.parse(raw) as CacheEntry;
    return typeof entry?.storedAt === "number" ? entry : null;
  } catch {
    return null;
  }
}

export function readQuery<T>(key: string): T | null {
  const entry = memory.get(key) ?? readFromSession(key);
  if (!entry) return null;

  if (!isFresh(entry)) {
    dropQuery(key);
    return null;
  }

  memory.set(key, entry);
  return entry.data as T;
}

export function writeQuery(key: string, data: unknown): void {
  const entry: CacheEntry = { data, storedAt: Date.now() };
  memory.set(key, entry);

  if (typeof window === "undefined") return;

  try {
    const serialized = JSON.stringify(entry);
    if (serialized.length > MAX_STORED_BYTES) return;

    window.sessionStorage.setItem(STORAGE_PREFIX + key, serialized);
  } catch {}
}

export function dropQuery(key: string): void {
  memory.delete(key);

  if (typeof window === "undefined") return;

  try {
    window.sessionStorage.removeItem(STORAGE_PREFIX + key);
  } catch {}
}
