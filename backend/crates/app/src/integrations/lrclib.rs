// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

use std::time::Duration;

use serde::Deserialize;

use super::http_client;
use crate::text::{normalize_key, to_latin};

const TIMEOUT: Duration = Duration::from_secs(15);
const DURATION_TOLERANCE_SECONDS: f64 = 2.0;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct LyricsQuery {
    pub title: String,
    pub artist: String,
    pub album: Option<String>,
    pub duration_seconds: i32,
}

#[derive(Debug, PartialEq, Eq)]
pub enum LyricsLookup {
    Found(String),
    NotFound,
    Instrumental,
}

#[derive(Clone)]
pub struct Lrclib {
    base_url: String,
    http: reqwest::Client,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct Record {
    track_name: Option<String>,
    artist_name: Option<String>,
    #[serde(default)]
    duration: f64,
    #[serde(default)]
    instrumental: bool,
    plain_lyrics: Option<String>,
    synced_lyrics: Option<String>,
}

fn has_text(value: Option<&str>) -> bool {
    value.is_some_and(|text| !text.trim().is_empty())
}

fn key(value: &str) -> String {
    normalize_key(value).replace(['\'', '\u{2019}'], "")
}

fn variants(query: &LyricsQuery) -> Vec<LyricsQuery> {
    let artist = to_latin(&query.artist);
    let title = to_latin(&query.title);
    let album = if title == query.title { query.album.clone() } else { query.album.as_deref().map(to_latin) };
    let all = [
        query.clone(),
        LyricsQuery { artist: artist.clone(), ..query.clone() },
        LyricsQuery { artist, title, album, duration_seconds: query.duration_seconds },
    ];

    let mut distinct: Vec<LyricsQuery> = Vec::new();
    for variant in all {
        if !distinct.iter().any(|seen| seen.artist == variant.artist && seen.title == variant.title) {
            distinct.push(variant);
        }
    }
    distinct
}

impl Lrclib {
    pub fn new(base_url: &str) -> Self {
        Self { base_url: base_url.trim_end_matches('/').to_owned(), http: http_client(TIMEOUT) }
    }

    pub async fn lookup(&self, query: &LyricsQuery) -> anyhow::Result<LyricsLookup> {
        for variant in variants(query) {
            let candidate = match self.exact(&variant).await? {
                Some(record) => Some(record),
                None => self.search(&variant).await?,
            };
            let result = match candidate {
                Some(record) if record.instrumental => LyricsLookup::Instrumental,
                Some(Record { synced_lyrics: Some(text), .. }) if has_text(Some(&text)) => LyricsLookup::Found(text),
                Some(Record { plain_lyrics: Some(text), .. }) if has_text(Some(&text)) => LyricsLookup::Found(text),
                _ => LyricsLookup::NotFound,
            };
            if result != LyricsLookup::NotFound {
                return Ok(result);
            }
        }
        Ok(LyricsLookup::NotFound)
    }

    async fn exact(&self, variant: &LyricsQuery) -> anyhow::Result<Option<Record>> {
        let duration = variant.duration_seconds.to_string();
        let mut parameters = vec![
            ("artist_name", variant.artist.as_str()),
            ("track_name", variant.title.as_str()),
            ("duration", duration.as_str()),
        ];
        if let Some(album) = variant.album.as_deref().filter(|a| !a.trim().is_empty()) {
            parameters.push(("album_name", album));
        }
        let response = self.http.get(format!("{}/api/get", self.base_url)).query(&parameters).send().await?;
        if response.status() == reqwest::StatusCode::NOT_FOUND {
            return Ok(None);
        }
        Ok(Some(response.error_for_status()?.json().await?))
    }

    async fn search(&self, variant: &LyricsQuery) -> anyhow::Result<Option<Record>> {
        let found: Vec<Record> = self
            .http
            .get(format!("{}/api/search", self.base_url))
            .query(&[("artist_name", variant.artist.as_str()), ("track_name", variant.title.as_str())])
            .send()
            .await?
            .error_for_status()?
            .json()
            .await?;

        let (title, artist) = (key(&variant.title), key(&variant.artist));
        let wanted = f64::from(variant.duration_seconds);
        let mut matches: Vec<Record> = found
            .into_iter()
            .filter(|r| r.track_name.as_deref().is_some_and(|t| key(t) == title))
            .filter(|r| r.artist_name.as_deref().is_some_and(|a| key(a) == artist))
            .filter(|r| (r.duration - wanted).abs() <= DURATION_TOLERANCE_SECONDS)
            .filter(|r| r.instrumental || has_text(r.synced_lyrics.as_deref()) || has_text(r.plain_lyrics.as_deref()))
            .collect();

        matches.sort_by(|a, b| {
            has_text(b.synced_lyrics.as_deref())
                .cmp(&has_text(a.synced_lyrics.as_deref()))
                .then((a.duration - wanted).abs().total_cmp(&(b.duration - wanted).abs()))
        });
        Ok(matches.into_iter().next())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cyrillic_queries_also_try_transliteration() {
        let query =
            LyricsQuery {
                title: "Кино".to_owned(), artist: "Цой".to_owned(), album: None, duration_seconds: 200
            };
        let tried: Vec<(String, String)> = variants(&query).into_iter().map(|v| (v.artist, v.title)).collect();
        assert_eq!(
            tried,
            [("Цой".into(), "Кино".into()), ("Tsoy".into(), "Кино".into()), ("Tsoy".into(), "Kino".into())]
        );
    }

    #[tokio::test]
    #[ignore = "talks to the real LRCLIB"]
    async fn finds_synced_lyrics_for_a_cyrillic_song() -> anyhow::Result<()> {
        let query = LyricsQuery {
            title: "Группа крови".to_owned(),
            artist: "Кино".to_owned(),
            album: None,
            duration_seconds: 286,
        };
        let found = Lrclib::new("https://lrclib.net").lookup(&query).await?;
        assert!(matches!(found, LyricsLookup::Found(ref text) if text.contains('[')), "{found:?}");
        Ok(())
    }

    #[test]
    fn latin_queries_are_tried_once() {
        let query =
            LyricsQuery { title: "Song".to_owned(), artist: "Band".to_owned(), album: None, duration_seconds: 200 };
        assert_eq!(variants(&query).len(), 1);
    }
}
