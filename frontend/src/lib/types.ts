export interface ArtistRef {
  id: string;
  name: string;
}

export interface Track {
  id: string;
  title: string;
  artistId: string;
  artistName: string;
  artists?: ArtistRef[] | null;
  albumId?: string | null;
  albumTitle?: string | null;
  genreId?: string | null;
  genreName?: string | null;
  trackNumber?: number | null;
  discNumber?: number | null;
  year?: number | null;
  durationSeconds: number;
  originalFileName: string;
  isFavorite: boolean;
  hasCover: boolean;
  createdAt: string;
}

export interface Artist {
  id: string;
  name: string;
  albumCount: number;
  trackCount: number;
  hasImage: boolean;
}

export interface ArtistDetail {
  id: string;
  name: string;
  hasImage: boolean;
  albums: Album[];
  tracks: Paged<Track>;
}

export interface Album {
  id: string;
  title: string;
  artistId: string;
  artistName: string;
  year?: number | null;
  trackCount: number;
  durationSeconds: number;
  hasCover: boolean;
  createdAt: string;
}

export interface AlbumDetail {
  id: string;
  title: string;
  artistId: string;
  artistName: string;
  year?: number | null;
  hasCover: boolean;
  durationSeconds: number;
  tracks: Track[];
}

export interface Genre {
  id: string;
  name: string;
  trackCount: number;
}

export interface Playlist {
  id: string;
  name: string;
  description?: string | null;
  trackCount: number;
  durationSeconds: number;
  /** Загружена ли для этого плейлиста картинка. */
  hasCover: boolean;
  /** Первый трек плейлиста с обложкой альбома; она подменяет картинку, когда своей нет. */
  coverTrackId?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface PlaylistDetail {
  id: string;
  name: string;
  description?: string | null;
  durationSeconds: number;
  hasCover: boolean;
  coverTrackId?: string | null;
  createdAt: string;
  updatedAt: string;
  tracks: Track[];
}

export interface SearchResults {
  artists: Artist[];
  albums: Album[];
  tracks: Track[];
  genres: Genre[];
}

export interface HistoryEntry {
  id: string;
  track: Track;
  playedAt: string;
  playbackPosition: number;
}

export interface LibraryStats {
  trackCount: number;
  albumCount: number;
  artistCount: number;
  playlistCount: number;
  totalDurationSeconds: number;
  totalBytes: number;
}

export interface HomeSummary {
  recentlyAdded: Track[];
  recentlyPlayed: Track[];
  favorites: Track[];
  albums: Album[];
  playlists: Playlist[];
  stats: LibraryStats;
}

/**
 * Почему что-то порекомендовано — данными, а не фразой: сервер не знает, на каком языке читают эту
 * страницу, поэтому присылает вид и предмет, а формулировку оставляет словарю.
 *
 * `kind` намеренно типизирован как обычная строка: сервер новее этой сборки может прислать вид, для
 * которого здесь нет формулировки, и это должно вырождаться в общий заголовок, а не ломаться.
 */
export interface RecommendationReason {
  kind: string;
  subject?: string | null;
  subjectId?: string | null;
}

export interface RecommendedTrack {
  track: Track;
  reason: RecommendationReason;
  /** Заполняется только для запросивших его администраторов; слушателю не показывается. */
  score?: number | null;
}

/** Одна полка. Заполнена ровно одна из трёх коллекций. */
export interface RecommendationSection {
  key: string;
  baseKey: string;
  reason?: RecommendationReason | null;
  tracks?: RecommendedTrack[] | null;
  artists?: Artist[] | null;
  albums?: Album[] | null;
}

export interface RecommendationHome {
  sections: RecommendationSection[];
  isColdStart: boolean;
  generatedAt?: string | null;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface User {
  id: string;
  username: string;
  displayName: string;
  isAdmin: boolean;
}

export interface AdminUser extends User {
  createdAt: string;
}

export interface ClientConfig {
  historyThresholdSeconds: number;
  maxUploadBytes: number;
  dataSaverAvailable: boolean;
}

export interface UploadResult {
  uploaded: Track[];
  failed: { fileName: string; reason: string }[];
}

/** Всё, что о файле известно до загрузки: хеш и теги, вычитанные в браузере. */
export interface UploadProbeFile {
  fileName: string;
  contentHash?: string;
  title?: string;
  artist?: string;
}

/**
 * `Duplicate` — тот же самый файл, грузить его незачем. `Similar` — та же песня другим файлом
 * (перекодированная, перетегированная): решает пользователь, поэтому из очереди не выпадает.
 */
export type UploadProbeVerdict = "New" | "Duplicate" | "Similar";

export interface UploadProbeResult {
  files: { fileName: string; verdict: UploadProbeVerdict; match?: Track }[];
}
