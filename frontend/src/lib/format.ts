export function formatDuration(totalSeconds: number | null | undefined): string {
  if (totalSeconds == null || !Number.isFinite(totalSeconds) || totalSeconds < 0) return "0:00";

  const seconds = Math.floor(totalSeconds % 60);
  const minutes = Math.floor((totalSeconds / 60) % 60);
  const hours = Math.floor(totalSeconds / 3600);

  const paddedSeconds = String(seconds).padStart(2, "0");

  return hours > 0
    ? `${hours}:${String(minutes).padStart(2, "0")}:${paddedSeconds}`
    : `${minutes}:${paddedSeconds}`;
}

export function formatArtists(track: {
  artistName: string;
  artists?: { name: string }[] | null;
}): string {
  const names = track.artists?.map((artist) => artist.name) ?? [];
  return names.length > 0 ? names.join(", ") : track.artistName;
}

/**
 * Подпись формата: «FLAC · 16/44.1» или «MP3 · 320 kbps».
 *
 * <p>
 * Разрядность есть только у форматов без потерь, битрейт осмыслен только у сжатых — поэтому
 * показывается ровно одно из двух, а не оба поля с прочерком. У треков, залитых до того, как это
 * стали записывать, кодека нет вовсе, и подписи тогда тоже нет.
 * </p>
 */
export function formatAudioSpec(track: {
  codec?: string | null;
  sampleRateHz?: number | null;
  bitsPerSample?: number | null;
  bitrateKbps?: number | null;
}): string | null {
  if (!track.codec) return null;

  const label = track.codec.toUpperCase();
  const khz = track.sampleRateHz
    ? (track.sampleRateHz / 1000).toFixed(1).replace(/\.0$/, "")
    : null;

  if (track.bitsPerSample && khz) return `${label} · ${track.bitsPerSample}/${khz}`;
  if (track.bitrateKbps) return `${label} · ${track.bitrateKbps} kbps`;

  return label;
}

/** Форматы без потерь — единственные, чью подпись стоит показывать в общем списке. */
export function isLossless(codec: string | null | undefined): boolean {
  return codec === "flac" || codec === "alac";
}

const PLACEHOLDER_HUES = [150, 172, 196, 214, 262, 292, 336, 12, 38, 96];

export function accentFor(seed: string): string {
  let hash = 0;
  for (let index = 0; index < seed.length; index += 1) {
    hash = (hash * 31 + seed.charCodeAt(index)) % 3600;
  }

  const hue = PLACEHOLDER_HUES[hash % PLACEHOLDER_HUES.length];
  const angle = 120 + (hash % 3) * 25;

  return `linear-gradient(${angle}deg, hsl(${hue} 48% 42%), hsl(${hue + 34} 40% 16%))`;
}

export function initialsFor(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return "?";
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[1][0]).toUpperCase();
}
