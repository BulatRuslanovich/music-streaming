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
  /** Есть ли у трека текст — едет вместе с треком, чтобы плеер не спрашивал про каждый отдельно. */
  hasLyrics: boolean;
  createdAt: string;
  /** Кодек исходника: mp3, flac, alac, aac. null у треков, залитых до того, как это стали записывать. */
  codec?: string | null;
  bitrateKbps?: number | null;
  sampleRateHz?: number | null;
  bitsPerSample?: number | null;
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
  isPublic: boolean;
  ownerId: string;
  ownerName: string;
  trackCount: number;
  durationSeconds: number;
  hasCover: boolean;
  coverTrackId?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface PlaylistDetail {
  id: string;
  name: string;
  description?: string | null;
  isPublic: boolean;
  ownerId: string;
  ownerName: string;
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

export interface RecommendationReason {
  kind: string;
  subject?: string | null;
  subjectId?: string | null;
}

export interface RecommendedTrack {
  track: Track;
  reason: RecommendationReason;
  score?: number | null;
}

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
  /** Деактивированная запись не может войти, но все её данные на месте. */
  isActive: boolean;
  createdAt: string;
}

export interface SystemInfo {
  version: string;
  commit?: string;
  builtAt?: string;
}

export interface ClientConfig {
  /** Сколько секунд должен проиграться трек, чтобы попасть в историю прослушиваний. */
  historyThresholdSeconds: number;
  /** Максимальное число записей истории на пользователя, после чего старые удаляются. */
  historyRetentionEntries: number;
  /** Максимальный размер файла (в байтах) для загрузки одного аудиотрека. */
  maxUploadBytes: number;
  /** Максимальный размер файла (в байтах) для загрузки обложки/аватара. */
  maxImageUploadBytes: number;
  /** Ступени качества, доступные на этой установке, от самой экономной к исходнику. */
  audioQualities: AudioQualityOption[];
}

/** Ступень качества. Клиент оперирует ею как есть; кодек и битрейт знает только сервер. */
export type AudioQuality = "Low" | "Normal" | "High" | "Original";

export interface AudioQualityOption {
  quality: AudioQuality;
  /** Битрейт ступени; null у исходника, битрейт которого свой у каждого файла. */
  bitrateKbps?: number | null;
}

export interface UserSettings {
  autoplay: boolean;
  quality: AudioQuality;
  dataSaver: boolean;
  timeZone: string;
}

export interface LyricLine {
  /** Смещение строки от начала трека в миллисекундах. */
  at: number;
  text: string;
}

export interface Lyrics {
  trackId: string;
  plain: string;
  /** Заполнено, только когда текст синхронизирован. */
  lines: LyricLine[];
  source: "Embedded" | "Manual";
}

export interface RadioBatch {
  tracks: RecommendedTrack[];
  seedTrackId?: string | null;
}

export type StatisticsPeriod = "Week" | "Month" | "Quarter" | "Year" | "All";

export interface StatisticsEntry {
  id: string;
  name: string;
  listenedSeconds: number;
  plays: number;
  hasImage: boolean;
}

export interface StatisticsTrack {
  track: Track;
  listenedSeconds: number;
  plays: number;
}

export interface DailyActivity {
  date: string;
  listenedSeconds: number;
  plays: number;
}

export interface HourlyActivity {
  hour: number;
  listenedSeconds: number;
  plays: number;
}

export interface StatisticsSummary {
  listenedSeconds: number;
  plays: number;
  uniqueTracks: number;
  uniqueArtists: number;
  uniqueAlbums: number;
  activeDays: number;
  peakDay?: DailyActivity | null;
  peakHour?: HourlyActivity | null;
}

export interface Statistics {
  period: StatisticsPeriod;
  from?: string | null;
  timeZone: string;
  summary: StatisticsSummary;
  topTracks: StatisticsTrack[];
  topArtists: StatisticsEntry[];
  topAlbums: StatisticsEntry[];
  topGenres: StatisticsEntry[];
  byDay: DailyActivity[];
  byHour: HourlyActivity[];
}

export interface LastfmStatus {
  /** Настроен ли Last.fm на этом сервере; если нет, подключаться некуда. */
  available: boolean;
  username?: string | null;
  connectedAt?: string | null;
  lastScrobbleAt?: string | null;
}

export interface UploadResult {
  uploaded: Track[];
  failed: { fileName: string; reason: string }[];
}

/** Причин по каждому идентификатору нет: строка либо была, либо её уже не было. */
export interface BulkDeleteResult {
  deleted: number;
  /** Названное, чего в библиотеке уже не оказалось. Не ошибка. */
  missing: string[];
}

export interface UploadProbeFile {
  fileName: string;
  contentHash?: string;
  title?: string;
  artist?: string;
}

export type UploadProbeVerdict = "New" | "Duplicate" | "Similar";

export interface UploadProbeResult {
  files: { fileName: string; verdict: UploadProbeVerdict; match?: Track }[];
}
