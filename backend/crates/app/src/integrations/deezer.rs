// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

use std::time::Duration;

use serde::Deserialize;

use super::{http_client, read_limited};
use crate::covers::MAX_IMAGE_BYTES;
use crate::text::normalize_key;

const SEARCH_TIMEOUT: Duration = Duration::from_secs(15);
const IMAGE_TIMEOUT: Duration = Duration::from_secs(20);

const MISSING_PICTURE_MARKER: &str = "/images/artist//";

#[derive(Clone)]
pub struct Deezer {
    base_url: String,
    search: reqwest::Client,
    images: reqwest::Client,
}

#[derive(Deserialize)]
struct SearchResponse {
    data: Option<Vec<ArtistResult>>,
    error: Option<SearchError>,
}

#[derive(Deserialize)]
struct SearchError {
    message: Option<String>,
}

#[derive(Deserialize)]
struct ArtistResult {
    name: Option<String>,
    picture_xl: Option<String>,
}

impl Deezer {
    pub fn new(base_url: &str) -> Self {
        Self {
            base_url: base_url.trim_end_matches('/').to_owned(),
            search: http_client(SEARCH_TIMEOUT),
            images: http_client(IMAGE_TIMEOUT),
        }
    }

    pub async fn artist_image(&self, artist_name: &str) -> anyhow::Result<Option<Vec<u8>>> {
        let response: SearchResponse = self
            .search
            .get(format!("{}/search/artist", self.base_url))
            .query(&[("q", artist_name)])
            .send()
            .await?
            .error_for_status()?
            .json()
            .await?;
        if let Some(error) = response.error {
            anyhow::bail!("Deezer refused the search: {}", error.message.unwrap_or_default());
        }

        let key = normalize_key(artist_name);
        let url = response
            .data
            .unwrap_or_default()
            .into_iter()
            .find(|artist| artist.name.as_deref().is_some_and(|name| normalize_key(name) == key))
            .and_then(|artist| artist.picture_xl)
            .filter(|url| !url.contains(MISSING_PICTURE_MARKER));
        let Some(url) = url else { return Ok(None) };

        let image = self.images.get(url).send().await?.error_for_status()?;
        let max = usize::try_from(MAX_IMAGE_BYTES).unwrap_or(usize::MAX);
        Ok(Some(read_limited(image, max).await?))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    #[ignore = "talks to the real Deezer"]
    async fn finds_a_well_known_artist() -> anyhow::Result<()> {
        let image = Deezer::new("https://api.deezer.com").artist_image("Daft Punk").await?;
        assert!(image.is_some_and(|bytes| bytes.len() > 10_000));
        Ok(())
    }
}
