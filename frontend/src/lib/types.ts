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
  createdAt: string;
  updatedAt: string;
}

export interface PlaylistDetail {
  id: string;
  name: string;
  description?: string | null;
  durationSeconds: number;
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
 * Why something was recommended, as data rather than as a sentence — the server does not know
 * which language this page is being read in, so it sends the kind and the subject and lets the
 * dictionary do the wording.
 *
 * `kind` is typed as a plain string on purpose: a server newer than this bundle may send a kind
 * this build has no phrasing for, and that must degrade to a generic heading rather than break.
 */
export interface RecommendationReason {
  kind: string;
  subject?: string | null;
  subjectId?: string | null;
}

export interface RecommendedTrack {
  track: Track;
  reason: RecommendationReason;
  /** Only ever populated for administrators asking for it; never shown to a listener. */
  score?: number | null;
}

/** One shelf. Exactly one of the three collections is populated. */
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
