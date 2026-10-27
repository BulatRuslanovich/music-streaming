// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

use std::collections::{HashMap, HashSet};
use std::path::Path;

use chrono::Utc;
use media::formats::{self, AudioFormat};
use media::tags::AudioMetadata;
use serde::{Deserialize, Serialize};
use serde_with::skip_serializing_none;
use sqlx::{PgConnection, PgPool};
use tokio::io::AsyncRead;
use uuid::Uuid;

use crate::catalog::dto::TrackDto;
use crate::catalog::lyrics::{self, LyricsSource};
use crate::catalog::tags::{self, TagResolver, split_artist_names};
use crate::catalog::tracks;
use crate::covers;
use crate::i18n::Lang;
use crate::integrations::enrichment::{EnrichmentQueue, EnrichmentRequest};
use crate::storage::{COVER_DIRECTORY, Storage, StoredFile};
use crate::streaming::HashCache;
use crate::text::normalize_key;
use crate::transcode::{self, TranscodeQueue};
use crate::{AppError, AppResult};

pub const MAX_AUDIO_BYTES: i64 = 200 * 1024 * 1024;

const SAME_RECORDING_TOLERANCE_SECONDS: i32 = 3;
const MAX_ORIGINAL_NAME_LENGTH: usize = 260;

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UploadFailure {
    pub file_name: String,
    pub reason: String,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UploadResult {
    pub uploaded: Vec<TrackDto>,
    pub failed: Vec<UploadFailure>,
}

#[derive(Clone)]
pub struct Uploads {
    pub db: PgPool,
    pub storage: Storage,
    pub transcodes: TranscodeQueue,
    pub hashes: HashCache,
    pub enrichment: EnrichmentQueue,
}

impl Uploads {
    pub async fn upload(
        &self,
        user: Uuid,
        file_name: &str,
        content_length: Option<i64>,
        body: impl AsyncRead + Unpin,
        lang: Lang,
    ) -> UploadResult {
        match self.upload_single(user, file_name, content_length, body).await {
            Ok(track) => UploadResult { uploaded: vec![track], failed: vec![] },
            Err(error) => {
                let reason = match error.message() {
                    Some(message) => {
                        tracing::info!("Upload of {file_name} rejected: {error}");
                        message.localized(lang)
                    }
                    None => {
                        tracing::error!("Unexpected failure while uploading {file_name}: {error:#}");
                        crate::i18n::localize("The file could not be processed.", &[], lang)
                    }
                };
                UploadResult {
                    uploaded: vec![],
                    failed: vec![UploadFailure { file_name: file_name.to_owned(), reason }],
                }
            }
        }
    }

    async fn upload_single(
        &self,
        user: Uuid,
        file_name: &str,
        content_length: Option<i64>,
        body: impl AsyncRead + Unpin,
    ) -> AppResult<TrackDto> {
        let started = std::time::Instant::now();
        let format = formats::for_file_name(file_name)
            .ok_or_else(|| AppError::validation("Only {0} files are supported.").arg(formats::ACCEPTED))?;
        if content_length.is_some_and(|length| length > MAX_AUDIO_BYTES) {
            return Err(AppError::validation("The file exceeds the {0} MB limit.").arg(MAX_AUDIO_BYTES / (1024 * 1024)));
        }

        let stored = self.storage.save_track(body, format.extension, MAX_AUDIO_BYTES).await?;
        let mut written_covers = Vec::new();

        let saved = self.check_and_save(file_name, &stored, format, &mut written_covers).await;
        let saved = match saved {
            Ok(saved) => saved,
            Err(error) => {
                self.storage.delete(&stored.relative_path);
                for cover in written_covers {
                    self.storage.delete_cover(&cover);
                }
                return Err(error);
            }
        };

        if let Some((old_path, old_hash)) = &saved.replaced {
            self.storage.delete(old_path);
            self.storage.delete_transcodes(old_hash);
            self.hashes.forget(saved.id);
        }
        for request in
            transcode::warmup_for(&stored.content_hash, &stored.relative_path, Some(saved.codec), saved.bitrate_kbps)
        {
            self.transcodes.try_enqueue_warmup(request);
        }
        self.enrichment.try_enqueue(EnrichmentRequest { track: Some(saved.id), artists: saved.new_artists });

        let track = tracks::get(&self.db, user, saved.id).await?;
        tracing::info!(
            "Uploaded {file_name} as track {}: {}, {:.1} MB in {:.1} s",
            saved.id,
            saved.codec,
            stored.size_bytes as f64 / (1024.0 * 1024.0),
            started.elapsed().as_secs_f64()
        );
        Ok(track)
    }

    async fn check_and_save(
        &self,
        file_name: &str,
        stored: &StoredFile,
        format: AudioFormat,
        written_covers: &mut Vec<String>,
    ) -> AppResult<Saved> {
        if stored.size_bytes == 0 {
            return Err(AppError::validation("The file is empty."));
        }
        let path = self
            .storage
            .existing(&stored.relative_path)
            .ok_or_else(|| AppError::validation("The uploaded file could not be read back."))?;

        let sniff_path = path.clone();
        let actual = tokio::task::spawn_blocking(move || formats::sniff_container(&sniff_path))
            .await
            .map_err(|error| AppError::Internal(error.into()))?;
        if actual.is_some_and(|actual| actual != format.extension) {
            return Err(AppError::validation("The file is not a {0} file despite its name.").arg(format.label()));
        }

        let metadata = tokio::task::spawn_blocking(move || media::tags::read(&path))
            .await
            .map_err(|error| AppError::Internal(error.into()))?
            .ok_or_else(|| AppError::validation("The file is not a readable {0} file.").arg(format.label()))?;
        if metadata.duration_seconds <= 0 {
            return Err(AppError::validation("The file contains no audio stream."));
        }

        let mut tx = self.db.begin().await?;
        let saved = assemble(&mut tx, &self.storage, file_name, stored, metadata, format, written_covers).await?;
        tx.commit().await?;
        Ok(saved)
    }
}

struct Saved {
    id: Uuid,
    codec: &'static str,
    bitrate_kbps: Option<i32>,

    replaced: Option<(String, String)>,

    new_artists: Vec<Uuid>,
}

fn names_from_file_name(file_name: &str) -> Option<(String, String)> {
    let stem = Path::new(file_name).file_stem()?.to_str()?.replace('_', " ");
    let mut parts: Vec<&str> = Vec::new();
    for piece in stem.split(" - ").flat_map(|p| p.split(" – ")).flat_map(|p| p.split(" — ")) {
        let piece = piece.trim();
        if !piece.is_empty() {
            parts.push(piece);
        }
    }
    let parts: Vec<&str> = parts.into_iter().skip_while(|part| part.chars().all(|c| c.is_ascii_digit())).collect();
    (parts.len() >= 2).then(|| (parts[0].to_owned(), parts[1..].join(" - ")))
}

async fn assemble(
    conn: &mut PgConnection,
    storage: &Storage,
    file_name: &str,
    stored: &StoredFile,
    mut metadata: AudioMetadata,
    format: AudioFormat,
    written_covers: &mut Vec<String>,
) -> AppResult<Saved> {
    if metadata.title.is_none()
        && metadata.artists.is_empty()
        && metadata.album_artists.is_empty()
        && let Some((artist, title)) = names_from_file_name(file_name)
    {
        metadata.artists = vec![artist];
        metadata.title = Some(title);
    }

    let duplicate = sqlx::query_scalar!("SELECT id FROM tracks WHERE content_hash = $1", stored.content_hash)
        .fetch_optional(&mut *conn)
        .await?;
    if duplicate.is_some() {
        return Err(AppError::conflict("This file is already in the library."));
    }

    let codec: &'static str = metadata.codec.unwrap_or_else(|| format.codec());
    let stem = Path::new(file_name).file_stem().and_then(|s| s.to_str()).unwrap_or(file_name);
    let title = metadata.title.clone().unwrap_or_else(|| stem.to_owned());
    let leaf = file_name.replace('\\', "/").rsplit('/').next().unwrap_or(file_name).trim().to_owned();
    let original_name: String = {
        let count = leaf.chars().count();
        leaf.chars().skip(count.saturating_sub(MAX_ORIGINAL_NAME_LENGTH)).collect()
    };
    let title_key = normalize_key(&title);
    let names = if metadata.artists.is_empty() { &metadata.album_artists } else { &metadata.artists };
    let artist_keys: Vec<String> = names
        .iter()
        .cloned()
        .chain(names.iter().flat_map(|name| split_artist_names(name)))
        .map(|name| normalize_key(&name))
        .collect::<HashSet<_>>()
        .into_iter()
        .collect();

    let same_recording = sqlx::query!(
        "SELECT t.id, t.codec, t.file_path, t.content_hash FROM tracks t
         WHERE t.normalized_title = $1 AND t.duration_seconds BETWEEN $2 AND $3
           AND EXISTS (SELECT 1 FROM track_artists ta JOIN artists a ON a.id = ta.artist_id
                       WHERE ta.track_id = t.id AND a.normalized_name = ANY($4))",
        title_key,
        metadata.duration_seconds - SAME_RECORDING_TOLERANCE_SECONDS,
        metadata.duration_seconds + SAME_RECORDING_TOLERANCE_SECONDS,
        &artist_keys
    )
    .fetch_all(&mut *conn)
    .await?;

    let lossless = formats::is_lossless(Some(codec));
    if !lossless && same_recording.iter().any(|t| formats::is_lossless(t.codec.as_deref())) {
        return Err(AppError::conflict("A lossless version of this track is already in the library."));
    }

    if lossless && let Some(lossy) = same_recording.iter().find(|t| !formats::is_lossless(t.codec.as_deref())) {
        sqlx::query!(
            "UPDATE tracks SET file_path = $2, original_file_name = $3, mime_type = $4, file_size = $5, content_hash = $6,
                               duration_seconds = $7, codec = $8, bitrate_kbps = $9, sample_rate_hz = $10, bits_per_sample = $11
             WHERE id = $1",
            lossy.id,
            stored.relative_path,
            original_name,
            format.mime_type,
            stored.size_bytes,
            stored.content_hash,
            metadata.duration_seconds,
            codec,
            metadata.bitrate_kbps,
            metadata.sample_rate_hz,
            metadata.bits_per_sample
        )
        .execute(&mut *conn)
        .await?;
        tracing::info!("Track {} ({title}) upgraded to {codec} from {file_name}", lossy.id);
        return Ok(Saved {
            id: lossy.id,
            codec,
            bitrate_kbps: metadata.bitrate_kbps,
            replaced: Some((lossy.file_path.clone(), lossy.content_hash.clone())),
            new_artists: vec![],
        });
    }

    let mut resolver = TagResolver::default();
    let name_refs: Vec<&str> = names.iter().map(String::as_str).collect();
    let credits = resolver.resolve_artists(conn, &name_refs).await?;
    let track_artist = credits[0];

    let mut album_id = None;
    if let Some(album_title) = metadata.album.as_deref().map(str::trim).filter(|a| !a.is_empty()) {
        let album_artist = if metadata.album_artists.is_empty() {
            track_artist
        } else {
            let refs: Vec<&str> = metadata.album_artists.iter().map(String::as_str).collect();
            resolver.resolve_artists(conn, &refs).await?[0]
        };
        let id = tags::album(conn, album_title, album_artist, metadata.year).await?;
        album_id = Some(id);
        attach_cover(conn, storage, id, metadata.cover.take(), written_covers).await?;
    }

    let genre_id = match metadata.genre.as_deref().map(str::trim).filter(|g| !g.is_empty()) {
        Some(genre) => Some(tags::genre(conn, genre).await?),
        None => None,
    };

    let id = Uuid::now_v7();
    sqlx::query!(
        "INSERT INTO tracks (id, title, normalized_title, artist_id, album_id, genre_id, track_number, disc_number, year,
                             duration_seconds, file_path, original_file_name, mime_type, file_size, content_hash, codec,
                             bitrate_kbps, sample_rate_hz, bits_per_sample, created_at)
         VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19, $20)",
        id,
        title,
        title_key,
        track_artist,
        album_id,
        genre_id,
        metadata.track_number,
        metadata.disc_number,
        metadata.year,
        metadata.duration_seconds,
        stored.relative_path,
        original_name,
        format.mime_type,
        stored.size_bytes,
        stored.content_hash,
        codec,
        metadata.bitrate_kbps,
        metadata.sample_rate_hz,
        metadata.bits_per_sample,
        Utc::now()
    )
    .execute(&mut *conn)
    .await?;

    for (position, artist) in credits.iter().enumerate() {
        sqlx::query!(
            "INSERT INTO track_artists (track_id, artist_id, position) VALUES ($1, $2, $3)",
            id,
            artist,
            i32::try_from(position).unwrap_or(i32::MAX)
        )
        .execute(&mut *conn)
        .await?;
    }

    let parsed = lyrics::parse(metadata.lyrics.as_deref());
    if !parsed.is_empty() {
        sqlx::query!(
            "INSERT INTO track_lyrics (track_id, plain, synced, source, updated_at) VALUES ($1, $2, $3, $4, $5)",
            id,
            parsed.plain,
            sqlx::types::Json(&parsed.lines) as _,
            LyricsSource::Embedded as i32,
            Utc::now()
        )
        .execute(&mut *conn)
        .await?;
    }

    Ok(Saved {
        id,
        codec,
        bitrate_kbps: metadata.bitrate_kbps,
        replaced: None,
        new_artists: resolver.created().to_vec(),
    })
}

async fn attach_cover(
    conn: &mut PgConnection,
    storage: &Storage,
    album: Uuid,
    cover: Option<Vec<u8>>,
    written_covers: &mut Vec<String>,
) -> AppResult<()> {
    let Some(cover) = cover else { return Ok(()) };
    let has_cover = sqlx::query_scalar!("SELECT cover_path IS NOT NULL AS \"has!\" FROM albums WHERE id = $1", album)
        .fetch_one(&mut *conn)
        .await?;
    if has_cover {
        return Ok(());
    }

    match covers::renditions(cover).await {
        Ok(set) => {
            let path = storage.save_renditions(COVER_DIRECTORY, album, &set).await?;
            written_covers.push(path.clone());
            sqlx::query!("UPDATE albums SET cover_path = $2 WHERE id = $1", album, path).execute(&mut *conn).await?;
        }
        Err(error) => {
            tracing::warn!("Album {album} stays coverless: the embedded art could not be processed ({error})")
        }
    }
    Ok(())
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ProbeFile {
    pub file_name: String,
    pub content_hash: Option<String>,
    pub title: Option<String>,
    pub artist: Option<String>,
}

#[derive(Debug, Clone, Copy, Serialize, PartialEq, Eq)]
pub enum ProbeVerdict {
    New,
    Duplicate,
    Upgrade,
}

#[derive(Debug, Clone, Copy, Serialize, PartialEq, Eq)]
pub enum ProbeBasis {
    None,
    Tags,
    Hash,
    HashAndTags,
}

#[skip_serializing_none]
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ProbeMatch {
    pub file_name: String,
    pub verdict: ProbeVerdict,
    pub basis: ProbeBasis,
    #[serde(rename = "match")]
    pub matched: Option<TrackDto>,
}

#[derive(Debug, Serialize)]
pub struct ProbeResult {
    pub files: Vec<ProbeMatch>,
}

pub async fn probe(db: &PgPool, user: Uuid, files: Vec<ProbeFile>) -> AppResult<ProbeResult> {
    if files.is_empty() {
        return Ok(ProbeResult { files: vec![] });
    }

    let mut hashes: HashMap<usize, String> = HashMap::new();
    let mut candidates: HashMap<usize, (String, HashSet<String>)> = HashMap::new();
    for (index, file) in files.iter().enumerate() {
        if let Some(hash) = file.content_hash.as_deref().map(str::to_ascii_lowercase)
            && hash.len() == 64
            && hash.chars().all(|c| c.is_ascii_digit() || ('a'..='f').contains(&c))
        {
            hashes.insert(index, hash);
        }
        let title = file.title.as_deref().map(str::trim).filter(|t| !t.is_empty());
        let artist = file.artist.as_deref().map(str::trim).filter(|a| !a.is_empty());
        if let (Some(title), Some(artist)) = (title, artist) {
            let keys: HashSet<String> = split_artist_names(artist).iter().map(|name| normalize_key(name)).collect();
            if !keys.is_empty() {
                candidates.insert(index, (normalize_key(title), keys));
            }
        }
    }

    let distinct: Vec<String> = hashes.values().cloned().collect::<HashSet<_>>().into_iter().collect();
    let known: HashMap<String, Uuid> = if distinct.is_empty() {
        HashMap::new()
    } else {
        sqlx::query!("SELECT content_hash, id FROM tracks WHERE content_hash = ANY($1)", &distinct)
            .fetch_all(db)
            .await?
            .into_iter()
            .map(|row| (row.content_hash, row.id))
            .collect()
    };
    let by_hash: HashMap<usize, Uuid> =
        hashes.iter().filter_map(|(index, hash)| known.get(hash).map(|id| (*index, *id))).collect();

    let unmatched: HashMap<usize, (String, HashSet<String>)> =
        candidates.iter().filter(|(index, _)| !by_hash.contains_key(index)).map(|(i, c)| (*i, c.clone())).collect();
    let by_tags = match_by_tags(db, &unmatched).await?;

    let ids: Vec<Uuid> = by_hash.values().copied().chain(by_tags.values().map(|(id, _)| *id)).collect();
    let matched = tracks::by_ids(db, user, &ids).await?;

    let results = files
        .into_iter()
        .enumerate()
        .map(|(index, file)| {
            let (verdict, track) = if let Some(id) = by_hash.get(&index) {
                (ProbeVerdict::Duplicate, Some(*id))
            } else if let Some((id, lossless)) = by_tags.get(&index) {
                let upgrade = !lossless && formats::for_file_name(&file.file_name) == Some(formats::FLAC);
                (if upgrade { ProbeVerdict::Upgrade } else { ProbeVerdict::Duplicate }, Some(*id))
            } else {
                (ProbeVerdict::New, None)
            };
            let basis = match (hashes.contains_key(&index), candidates.contains_key(&index)) {
                (true, true) => ProbeBasis::HashAndTags,
                (true, false) => ProbeBasis::Hash,
                (false, true) => ProbeBasis::Tags,
                (false, false) => ProbeBasis::None,
            };
            let matched = track.and_then(|id| matched.get(&id).cloned());
            ProbeMatch { file_name: file.file_name, verdict, basis, matched }
        })
        .collect();
    Ok(ProbeResult { files: results })
}

async fn match_by_tags(
    db: &PgPool,
    candidates: &HashMap<usize, (String, HashSet<String>)>,
) -> AppResult<HashMap<usize, (Uuid, bool)>> {
    if candidates.is_empty() {
        return Ok(HashMap::new());
    }
    let titles: Vec<String> = candidates.values().map(|(title, _)| title.clone()).collect();
    let rows = sqlx::query!(
        r#"SELECT t.id, t.normalized_title, t.codec,
                  ARRAY(SELECT a.normalized_name FROM track_artists ta JOIN artists a ON a.id = ta.artist_id
                        WHERE ta.track_id = t.id) AS "artist_keys!"
           FROM tracks t WHERE t.normalized_title = ANY($1)"#,
        &titles
    )
    .fetch_all(db)
    .await?;

    let mut matches = HashMap::new();
    for (index, (title, keys)) in candidates {
        let same: Vec<_> = rows
            .iter()
            .filter(|row| &row.normalized_title == title && row.artist_keys.iter().any(|k| keys.contains(k)))
            .collect();
        let best = same.iter().find(|row| formats::is_lossless(row.codec.as_deref())).or_else(|| same.first());
        if let Some(best) = best {
            matches.insert(*index, (best.id, formats::is_lossless(best.codec.as_deref())));
        }
    }
    Ok(matches)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn file_name_gives_artist_and_title() {
        assert_eq!(
            names_from_file_name("03 - Loose Thread Collective - Loose_Thread - Live.mp3"),
            Some(("Loose Thread Collective".to_owned(), "Loose Thread - Live".to_owned()))
        );
    }

    #[test]
    fn single_part_name_gives_nothing() {
        assert_eq!(names_from_file_name("recording.mp3"), None);
    }
}
