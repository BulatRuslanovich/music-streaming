// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

use reco::radio::{self, RadioRequest};
use reco::taste;
use serde::{Deserialize, Serialize};
use serde_with::skip_serializing_none;
use uuid::Uuid;

use super::{ReasonDto, Recommendations, RecommendedTrackDto, SignalsDto, reasons};
use crate::catalog::tracks;
use crate::{AppError, AppResult};

pub const MAX_BATCH: i64 = 20;
const DEFAULT_BATCH: i64 = 6;

const RECENT_HOURS: i32 = 48;
const RECENT_LIMIT: i64 = 120;

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RadioRequestDto {
    pub seed_track_id: Option<Uuid>,
    pub exclude: Option<Vec<Uuid>>,
    pub limit: Option<i64>,
    pub mood: Option<String>,
}

#[skip_serializing_none]
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RadioBatchDto {
    pub tracks: Vec<RecommendedTrackDto>,
    pub seed_track_id: Option<Uuid>,
}

impl Recommendations {
    pub async fn radio(&self, user: Uuid, request: RadioRequestDto) -> AppResult<RadioBatchDto> {
        let limit = request.limit.unwrap_or(DEFAULT_BATCH);
        if !(1..=MAX_BATCH).contains(&limit) {
            return Err(AppError::validation("Radio limit must be between 1 and {0}.").arg(MAX_BATCH));
        }
        if let Some(mood) = request.mood.as_deref()
            && self.moods.find(mood).is_none()
        {
            return Err(AppError::validation("Unknown mood '{0}'.").arg(mood));
        }

        let library = self.index.library();
        let snapshot = &library.snapshot;
        if snapshot.is_empty() {
            return Ok(RadioBatchDto { tracks: vec![], seed_track_id: None });
        }

        let listener = self.listeners.get(&self.db, user).await?;
        let recent = sqlx::query_scalar!(
            r#"SELECT track_id AS "track_id!" FROM playback_events
               WHERE user_id = $1 AND track_id IS NOT NULL AND occurred_at >= now() - make_interval(hours => $2)
               GROUP BY track_id ORDER BY max(occurred_at) DESC LIMIT $3"#,
            user,
            RECENT_HOURS,
            RECENT_LIMIT
        )
        .fetch_all(&self.db)
        .await?;

        let queue = request.exclude.unwrap_or_default();
        let batch = {
            let taste = taste::taste(snapshot, &listener);
            let mood = request.mood.as_deref().and_then(|mood| library.moods.get(mood));
            let request = RadioRequest {
                seed: request.seed_track_id,
                queue: &queue,
                recent: &recent,
                limit: usize::try_from(limit).unwrap_or(1),
                mood,
            };
            radio::next(snapshot, &listener, &taste, &request)
        };

        let anchor = batch.anchor.map(|row| snapshot.meta(row).track_id);
        let ids: Vec<Uuid> = batch.picks.iter().map(|pick| snapshot.meta(pick.row).track_id).chain(anchor).collect();
        let found = tracks::by_ids(&self.db, user, &ids).await?;
        let anchor_title = anchor.and_then(|id| found.get(&id)).map(|track| track.title.clone());

        let tracks = batch
            .picks
            .iter()
            .enumerate()
            .filter_map(|(index, pick)| {
                let id = snapshot.meta(pick.row).track_id;
                let reason = if batch.moved && index == 0 {
                    ReasonDto::new(reasons::MATCHES_YOUR_TASTE)
                } else if pick.explore {
                    ReasonDto { subject_id: anchor, ..ReasonDto::new(reasons::DISCOVERY) }
                } else if let Some(title) = &anchor_title {
                    ReasonDto { kind: reasons::SOUNDS_LIKE, subject: Some(title.clone()), subject_id: anchor }
                } else {
                    ReasonDto::new(reasons::MATCHES_YOUR_TASTE)
                };
                Some(RecommendedTrackDto {
                    track: found.get(&id)?.clone(),
                    reason,
                    score: None,
                    signals: Some(SignalsDto { explore: pick.explore }),
                })
            })
            .collect();
        Ok(RadioBatchDto { tracks, seed_track_id: anchor })
    }
}
