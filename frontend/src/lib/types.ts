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
  hasLyrics: boolean;
  createdAt: string;
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

export type HomeBlockLayout =
  "Shelf" | "Hero" | "Tile" | "QuickTiles" | "Grid" | "Chart" | "Circles";

export interface HomeBlock {
  key: string;
  baseKey: string;
  layout: HomeBlockLayout;
  reason?: RecommendationReason | null;
  tracks?: Track[] | null;
  albums?: Album[] | null;
  artists?: Artist[] | null;
  playlists?: Playlist[] | null;
  totalCount?: number | null;
}

export interface HomeFeed {
  blocks: HomeBlock[];
  stats: LibraryStats;
  isColdStart: boolean;
  generatedAt?: string | null;
}

export type HomeMixKind = "Daily" | "New" | "Top";

export type HomeMixSlug = "daily" | "new" | "top";

export interface HomeMix {
  kind: HomeMixKind;
  tracks: Track[];
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
  isActive: boolean;
  createdAt: string;
}

export interface SystemInfo {
  version: string;
  commit?: string;
  builtAt?: string;
}

export interface ClientConfig {
  historyThresholdSeconds: number;
  historyRetentionEntries: number;
  maxUploadBytes: number;
  maxImageUploadBytes: number;
  audioQualities: AudioQualityOption[];
}

export type AudioQuality = "Low" | "Normal" | "High" | "Original";

export interface AudioQualityOption {
  quality: AudioQuality;
  bitrateKbps?: number | null;
}

export interface UserSettings {
  autoplay: boolean;
  quality: AudioQuality;
  dataSaver: boolean;
  timeZone: string;
}

export interface LyricLine {
  at: number;
  text: string;
}

export interface Lyrics {
  trackId: string;
  plain: string;
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
  available: boolean;
  username?: string | null;
  connectedAt?: string | null;
  lastScrobbleAt?: string | null;
}

export interface UploadResult {
  uploaded: Track[];
  failed: { fileName: string; reason: string }[];
}

export interface BulkDeleteResult {
  deleted: number;
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
