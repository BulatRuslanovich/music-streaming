// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

use std::collections::{HashMap, HashSet};

use reco::shelves::{self, Reason, Shelf, ShelfItems};
use reco::taste;
use serde::Serialize;
use serde_with::skip_serializing_none;
use uuid::Uuid;

use super::{ReasonDto, Recommendations, reasons};
use crate::AppResult;
use crate::catalog::dto::{AlbumDto, ArtistDto, TrackDto};
use crate::catalog::overview::{self, LibraryStats, StatsCache};
use crate::catalog::{albums, artists, tracks};
use crate::library::history::{self, THRESHOLD_SECONDS};
use crate::library::playlists::{self, PlaylistDto};
use crate::paging::PageRequest;
use crate::storage::Storage;

const MINIMUM_BLOCK: usize = 4;
const MINIMUM_HERO: usize = 5;
const HERO_TRACKS: usize = 20;
const MOSAIC: usize = 4;
const QUICK_PLACES: usize = 5;
const QUICK_PLAYLISTS: usize = 2;
const MIX_SIZE: i64 = 20;
const DAILY_MIX_SIZE: usize = 60;
const MAX_TRACK_SHELVES: usize = 2;

const FALLBACK_WEIGHT: f64 = 0.15;
const TOP_WINDOW_DAYS: i32 = 7;
const SHELF_PRIORITY: [&str; 3] = ["forYou", "becauseYouListened", "discover"];

#[derive(Debug, Clone, Copy, Serialize)]
pub enum Layout {
    Shelf,
    Hero,
    Tile,
    QuickTiles,
    Grid,
    Chart,
    Circles,
}

#[derive(Debug, Clone, Copy, Serialize)]
pub enum Zone {
    Lead,
    Quick,
    Browse,
}

#[skip_serializing_none]
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct HomeBlockDto {
    pub key: String,
    pub base_key: String,
    pub layout: Layout,
    pub zone: Zone,
    pub reason: Option<ReasonDto>,
    pub tracks: Option<Vec<TrackDto>>,
    pub albums: Option<Vec<AlbumDto>>,
    pub artists: Option<Vec<ArtistDto>>,
    pub playlists: Option<Vec<PlaylistDto>>,
    pub total_count: Option<i64>,
}

impl HomeBlockDto {
    fn new(key: &str, layout: Layout, zone: Zone) -> Self {
        Self {
            key: key.to_owned(),
            base_key: key.to_owned(),
            layout,
            zone,
            reason: None,
            tracks: None,
            albums: None,
            artists: None,
            playlists: None,
            total_count: None,
        }
    }

    fn tracks(key: &str, layout: Layout, tracks: Vec<TrackDto>) -> Option<Self> {
        (tracks.len() >= MINIMUM_BLOCK).then(|| Self { tracks: Some(tracks), ..Self::new(key, layout, Zone::Browse) })
    }
}

#[derive(Debug, Serialize)]
pub struct HomeFeedDto {
    pub blocks: Vec<HomeBlockDto>,
    pub stats: LibraryStats,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize)]
pub enum MixKind {
    Daily,
    New,
    Top,
}

impl MixKind {
    pub fn parse(value: &str) -> Option<Self> {
        [Self::Daily, Self::New, Self::Top].into_iter().find(|kind| format!("{kind:?}").eq_ignore_ascii_case(value))
    }
}

#[derive(Debug, Serialize)]
pub struct HomeMixDto {
    pub kind: MixKind,
    pub tracks: Vec<TrackDto>,
}

struct Hydrated {
    shelf: Shelf,
    reason: ReasonDto,
    tracks: Vec<TrackDto>,
    artists: Vec<ArtistDto>,
}

impl Hydrated {
    fn count(&self) -> usize {
        self.tracks.len().max(self.artists.len())
    }

    fn block(self) -> HomeBlockDto {
        let circles = self.shelf.base_key() == "artistsForYou";
        HomeBlockDto {
            key: self.shelf.key.clone(),
            reason: Some(self.reason),
            tracks: (!circles).then_some(self.tracks),
            artists: circles.then_some(self.artists),
            ..HomeBlockDto::new(
                self.shelf.base_key(),
                if circles { Layout::Circles } else { Layout::Shelf },
                Zone::Browse,
            )
        }
    }
}

impl Recommendations {
    pub async fn feed(
        &self,
        storage: &Storage,
        stats: &StatsCache,
        user: Uuid,
        section_size: i64,
    ) -> AppResult<HomeFeedDto> {
        let size = section_size.clamp(1, 50);
        let stats = overview::stats(&self.db, storage, stats, user).await?;

        let mut query = tracks::select(user);
        query.push(" ORDER BY t.created_at DESC LIMIT ").push_bind(size);
        let recently_added = tracks::fetch(&self.db, query).await?;
        if recently_added.is_empty() {
            return Ok(HomeFeedDto { blocks: vec![], stats });
        }

        let size_i32 = i32::try_from(size).unwrap_or(50);
        let recently_played = history::recent(&self.db, user, PageRequest::new(Some(1), Some(size_i32))).await?.items;

        let mut query = tracks::select(user);
        query
            .push(" JOIN favorites fav ON fav.track_id = t.id AND fav.user_id = ")
            .push_bind(user)
            .push(" ORDER BY fav.created_at DESC LIMIT ")
            .push_bind(size);
        let favorites = tracks::fetch(&self.db, query).await?;
        let favorite_count =
            sqlx::query_scalar!(r#"SELECT count(*) AS "count!" FROM favorites WHERE user_id = $1"#, user)
                .fetch_one(&self.db)
                .await?;

        let mut query = albums::select();
        query.push(" ORDER BY a.created_at DESC LIMIT ").push_bind(size);
        let new_albums = albums::fetch(&self.db, query).await?;

        let mut query = playlists::select();
        query.push(" WHERE p.user_id = ").push_bind(user).push(" ORDER BY p.updated_at DESC LIMIT ").push_bind(size);
        let own_playlists = playlists::fetch(&self.db, query).await?;

        let shelf_size = usize::try_from(size).unwrap_or(shelves::SHELF_SIZE).min(shelves::SHELF_SIZE);
        let personal = self.personal_shelves(user, shelf_size).await?;
        let top = self.top_tracks(user, size).await?;
        let mix = self.daily_mix(user).await?;

        let mut track_shelves: Vec<Hydrated> = Vec::new();
        let mut artist_shelf = None;
        for shelf in personal {
            if shelf.count() < MINIMUM_BLOCK {
                continue;
            }
            if shelf.shelf.base_key() == "artistsForYou" {
                artist_shelf.get_or_insert(shelf);
            } else {
                track_shelves.push(shelf);
            }
        }
        track_shelves.sort_by_key(|shelf| SHELF_PRIORITY.iter().position(|key| *key == shelf.shelf.base_key()));
        let mut track_shelves = track_shelves.into_iter().take(MAX_TRACK_SHELVES);

        let mut seen_places = HashSet::new();
        let quick_places: Vec<&TrackDto> = recently_played
            .iter()
            .filter(|track| seen_places.insert(track.album_id.unwrap_or(track.id)))
            .take(QUICK_PLACES)
            .collect();
        let quick_album_ids: Vec<Uuid> = quick_places.iter().filter_map(|track| track.album_id).collect();
        let mut query = albums::select();
        query.push(" WHERE a.id = ANY(").push_bind(quick_album_ids.clone()).push(")");
        let mut quick_albums: HashMap<Uuid, AlbumDto> =
            albums::fetch(&self.db, query).await?.into_iter().map(|album| (album.id, album)).collect();
        let quick_playlists: Vec<PlaylistDto> = own_playlists.iter().take(QUICK_PLAYLISTS).cloned().collect();

        let blocks: Vec<Option<HomeBlockDto>> = vec![
            (mix.len() >= MINIMUM_HERO).then(|| HomeBlockDto {
                total_count: Some(i64::try_from(mix.len()).unwrap_or(i64::MAX)),
                tracks: Some(mix.iter().take(HERO_TRACKS).cloned().collect()),
                ..HomeBlockDto::new("dailyMix", Layout::Hero, Zone::Lead)
            }),
            (favorite_count > 0).then(|| HomeBlockDto {
                total_count: Some(favorite_count),
                tracks: Some(favorites.into_iter().take(MOSAIC).collect()),
                ..HomeBlockDto::new("favorites", Layout::Tile, Zone::Quick)
            }),
            (!quick_places.is_empty() || !quick_playlists.is_empty()).then(|| HomeBlockDto {
                tracks: Some(
                    quick_places.iter().filter(|track| track.album_id.is_none()).map(|&track| track.clone()).collect(),
                ),
                albums: Some(quick_album_ids.iter().filter_map(|id| quick_albums.remove(id)).collect()),
                playlists: Some(quick_playlists),
                ..HomeBlockDto::new("quickTiles", Layout::QuickTiles, Zone::Quick)
            }),
            (new_albums.len() >= MINIMUM_BLOCK).then(|| HomeBlockDto {
                albums: Some(new_albums),
                ..HomeBlockDto::new("newAlbums", Layout::Shelf, Zone::Browse)
            }),
            HomeBlockDto::tracks("newArrivals", Layout::Grid, recently_added),
            artist_shelf.map(Hydrated::block),
            track_shelves.next().map(Hydrated::block),
            (!own_playlists.is_empty()).then(|| HomeBlockDto {
                playlists: Some(own_playlists),
                ..HomeBlockDto::new("yourPlaylists", Layout::Shelf, Zone::Browse)
            }),
            HomeBlockDto::tracks("topTracks", Layout::Chart, top),
            track_shelves.next().map(Hydrated::block),
        ];
        Ok(HomeFeedDto { blocks: blocks.into_iter().flatten().collect(), stats })
    }

    pub async fn mix(&self, user: Uuid, kind: MixKind) -> AppResult<HomeMixDto> {
        let tracks = match kind {
            MixKind::New => {
                let mut query = tracks::select(user);
                query.push(" ORDER BY t.created_at DESC LIMIT ").push_bind(MIX_SIZE);
                tracks::fetch(&self.db, query).await?
            }
            MixKind::Top => self.top_tracks(user, MIX_SIZE).await?,
            MixKind::Daily => self.daily_mix(user).await?,
        };
        Ok(HomeMixDto { kind, tracks })
    }

    async fn personal_shelves(&self, user: Uuid, size: usize) -> AppResult<Vec<Hydrated>> {
        let library = self.index.library();
        let listener = self.listeners.get(&self.db, user).await?;
        let snapshot = &library.snapshot;
        let shelves = {
            let taste = taste::taste(snapshot, &listener);
            shelves::home(snapshot, &listener, &taste)
        };

        let mut track_ids = Vec::new();
        let mut artist_ids = Vec::new();
        for shelf in &shelves {
            match &shelf.items {
                ShelfItems::Tracks(picks) => {
                    track_ids.extend(picks.iter().take(size).map(|pick| snapshot.meta(pick.row).track_id));
                }
                ShelfItems::Artists(ids) => artist_ids.extend(ids.iter().take(size)),
            }
            if let Reason::BecauseYouListened(artist) = shelf.reason {
                artist_ids.push(artist);
            }
        }
        let found_tracks = tracks::by_ids(&self.db, user, &track_ids).await?;
        let found_artists: HashMap<Uuid, ArtistDto> = artists::fetch_where(
            &self.db,
            |query| {
                query.push(" WHERE a.id = ANY(").push_bind(artist_ids.clone()).push(")");
            },
            "a.name",
            i64::try_from(artist_ids.len()).unwrap_or(i64::MAX).max(1),
        )
        .await?
        .into_iter()
        .map(|artist| (artist.id, artist))
        .collect();

        Ok(shelves
            .into_iter()
            .map(|shelf| {
                let reason = match shelf.reason {
                    Reason::MatchesYourTaste => ReasonDto::new(reasons::MATCHES_YOUR_TASTE),
                    Reason::FreshInLibrary => ReasonDto::new(reasons::FRESH_IN_LIBRARY),
                    Reason::Discovery => ReasonDto::new(reasons::DISCOVERY),
                    Reason::BecauseYouListened(artist) => ReasonDto {
                        kind: reasons::BECAUSE_YOU_LISTENED,
                        subject: found_artists.get(&artist).map(|a| a.name.clone()),
                        subject_id: Some(artist),
                    },
                };
                let (tracks, artists) = match &shelf.items {
                    ShelfItems::Tracks(picks) => (
                        picks
                            .iter()
                            .take(size)
                            .filter_map(|pick| found_tracks.get(&snapshot.meta(pick.row).track_id).cloned())
                            .collect(),
                        vec![],
                    ),
                    ShelfItems::Artists(ids) => {
                        (vec![], ids.iter().take(size).filter_map(|id| found_artists.get(id).cloned()).collect())
                    }
                };
                Hydrated { shelf, reason, tracks, artists }
            })
            .collect())
    }

    async fn top_tracks(&self, user: Uuid, size: i64) -> AppResult<Vec<TrackDto>> {
        let ids = sqlx::query_scalar!(
            r#"SELECT track_id AS "track_id!" FROM playback_events
               WHERE user_id = $1 AND track_id IS NOT NULL AND type IN (3, 4) AND listened_seconds >= $2
                 AND occurred_at >= now() - make_interval(days => $3)
               GROUP BY track_id
               ORDER BY SUM(listened_seconds::bigint) DESC, count(*) DESC, track_id
               LIMIT $4"#,
            user,
            THRESHOLD_SECONDS,
            TOP_WINDOW_DAYS,
            size
        )
        .fetch_all(&self.db)
        .await?;
        let mut found = tracks::by_ids(&self.db, user, &ids).await?;
        Ok(ids.iter().filter_map(|id| found.remove(id)).collect())
    }

    pub async fn daily_mix(&self, user: Uuid) -> AppResult<Vec<TrackDto>> {
        let listener = self.listeners.get(&self.db, user).await?;
        let day = i32::try_from(listener.day).unwrap_or_default();
        let stored = sqlx::query_scalar!(
            r#"SELECT track_ids AS "track_ids: sqlx::types::Json<Vec<Uuid>>" FROM daily_mixes
               WHERE user_id = $1 AND local_date = DATE '2000-01-01' + $2::int"#,
            user,
            day
        )
        .fetch_optional(&self.db)
        .await?;

        let ids = match stored {
            Some(ids) => ids.0,
            None => {
                let ids = self.build_daily_mix(user, &listener).await?;
                if ids.len() < MINIMUM_HERO {
                    return Ok(vec![]);
                }
                sqlx::query!(
                    "INSERT INTO daily_mixes (user_id, local_date, track_ids) VALUES ($1, DATE '2000-01-01' + $2::int, $3)
                     ON CONFLICT DO NOTHING",
                    user,
                    day,
                    sqlx::types::Json(&ids) as _
                )
                .execute(&self.db)
                .await?;
                sqlx::query!(
                    "DELETE FROM daily_mixes WHERE user_id = $1 AND local_date < DATE '2000-01-01' + $2::int",
                    user,
                    day
                )
                .execute(&self.db)
                .await?;

                sqlx::query_scalar!(
                    r#"SELECT track_ids AS "track_ids: sqlx::types::Json<Vec<Uuid>>" FROM daily_mixes
                       WHERE user_id = $1 AND local_date = DATE '2000-01-01' + $2::int"#,
                    user,
                    day
                )
                .fetch_optional(&self.db)
                .await?
                .map_or(ids, |stored| stored.0)
            }
        };

        let mut found = tracks::by_ids(&self.db, user, &ids).await?;
        Ok(ids.iter().filter_map(|id| found.remove(id)).collect())
    }

    async fn build_daily_mix(&self, user: Uuid, listener: &reco::Listener) -> AppResult<Vec<Uuid>> {
        let library = self.index.library();
        let mut pool = {
            let taste = taste::taste(&library.snapshot, listener);
            shelves::mix_pool(&library.snapshot, listener, &taste)
        };
        if pool.len() < DAILY_MIX_SIZE {
            let fallback = sqlx::query_scalar!(
                r#"(SELECT track_id AS "id!" FROM favorites WHERE user_id = $1 ORDER BY created_at DESC LIMIT $2)
                   UNION ALL
                   (SELECT id AS "id!" FROM tracks ORDER BY created_at DESC LIMIT $2)"#,
                user,
                i64::try_from(DAILY_MIX_SIZE).unwrap_or(i64::MAX)
            )
            .fetch_all(&self.db)
            .await?;
            let mut seen: HashSet<Uuid> = pool.iter().map(|(id, _)| *id).collect();
            pool.extend(fallback.into_iter().filter(|id| seen.insert(*id)).map(|id| (id, FALLBACK_WEIGHT)));
        }
        Ok(reco::random::weighted_sample(&pool, DAILY_MIX_SIZE, reco::random::seed(user, "dailyMix", listener.day)))
    }
}
