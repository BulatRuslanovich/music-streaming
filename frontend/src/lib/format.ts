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

export function formatTotalDuration(totalSeconds: number): string {
  if (totalSeconds < 60) return `${Math.round(totalSeconds)} sec`;

  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.round((totalSeconds % 3600) / 60);

  if (hours === 0) return `${minutes} min`;
  return minutes === 0 ? `${hours} hr` : `${hours} hr ${minutes} min`;
}


export function formatArtists(track: {
  artistName: string;
  artists?: { name: string }[] | null;
}): string {
  const names = track.artists?.map((artist) => artist.name) ?? [];
  return names.length > 0 ? names.join(", ") : track.artistName;
}

export function formatBytes(bytes: number): string {
  if (bytes <= 0) return "0 B";

  const units = ["B", "KB", "MB", "GB", "TB"];
  const exponent = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  const value = bytes / 1024 ** exponent;

  return `${value.toFixed(value >= 10 || exponent === 0 ? 0 : 1)} ${units[exponent]}`;
}

export function formatRelativeDate(isoDate: string): string {
  const date = new Date(isoDate);
  if (Number.isNaN(date.getTime())) return "";

  const startOfToday = new Date();
  startOfToday.setHours(0, 0, 0, 0);

  const dayDifference = Math.floor((startOfToday.getTime() - date.getTime()) / 86_400_000);

  if (dayDifference < 0) return "Today";
  if (dayDifference === 0) return "Today";
  if (dayDifference === 1) return "Yesterday";
  if (dayDifference < 7) return date.toLocaleDateString(undefined, { weekday: "long" });

  return date.toLocaleDateString(undefined, {
    day: "numeric",
    month: "short",
    year: date.getFullYear() === new Date().getFullYear() ? undefined : "numeric",
  });
}

export function formatTimeOfDay(isoDate: string): string {
  const date = new Date(isoDate);
  if (Number.isNaN(date.getTime())) return "";

  return date.toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit" });
}

const PLACEHOLDER_PLATES = [
  "hsl(150 30% 26%)", // green
  "hsl(172 32% 25%)", // teal
  "hsl(196 34% 26%)", // sea blue
  "hsl(214 30% 28%)", // slate blue
  "hsl(96 26% 26%)", // olive
  "hsl(38 32% 27%)", // sand
  "hsl(18 30% 28%)", // terracotta
  "hsl(266 24% 29%)", // muted violet
];

export function accentFor(seed: string): string {
  let hash = 0;
  for (let index = 0; index < seed.length; index += 1) {
    hash = (hash * 31 + seed.charCodeAt(index)) % 360;
  }
  return PLACEHOLDER_PLATES[hash % PLACEHOLDER_PLATES.length];
}

export function initialsFor(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return "?";
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[1][0]).toUpperCase();
}
