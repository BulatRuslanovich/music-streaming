// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

use std::sync::Arc;
use std::time::{Duration, Instant};

use chrono::Utc;
use media::analysis::Analysis;
use media::clap::{self, ClapEmbedder};
use media::cues;
use sqlx::PgPool;
use tokio_util::sync::CancellationToken;
use uuid::Uuid;

use crate::recommendations::index::EmbeddingIndex;
use crate::storage::Storage;

const STARTUP_DELAY: Duration = Duration::from_secs(20);
const POLL: Duration = Duration::from_secs(30);
const BATCH: i64 = 16;
const RETRY_FAILED_AFTER: chrono::Duration = chrono::Duration::days(7);

pub fn spawn(
    db: PgPool,
    storage: Storage,
    embedder: Arc<ClapEmbedder>,
    index: EmbeddingIndex,
    cancel: CancellationToken,
) -> tokio::task::JoinHandle<()> {
    tokio::spawn(async move {
        tokio::select! {
            () = cancel.cancelled() => return,
            () = tokio::time::sleep(STARTUP_DELAY) => {}
        }
        loop {
            match run_pass(&db, &storage, &embedder, &cancel).await {
                Ok(analysed) if analysed > 0 => index.request_reload(),
                Ok(_) => {}
                Err(error) => tracing::error!("Track analysis pass failed: {error:#}"),
            }
            tokio::select! {
                () = cancel.cancelled() => return,
                () = tokio::time::sleep(POLL) => {}
            }
        }
    })
}

async fn run_pass(
    db: &PgPool,
    storage: &Storage,
    embedder: &Arc<ClapEmbedder>,
    cancel: &CancellationToken,
) -> anyhow::Result<usize> {
    let mut analysed = 0;
    loop {
        let retry_before = Utc::now() - RETRY_FAILED_AFTER;
        let batch = sqlx::query!(
            "SELECT t.id, t.file_path, t.content_hash, t.duration_seconds FROM tracks t
             LEFT JOIN track_cues c ON c.track_id = t.id
             LEFT JOIN track_embeddings e ON e.track_id = t.id
             WHERE c.track_id IS NULL OR c.version <> $1 OR c.source_hash <> t.content_hash
                OR (NOT c.succeeded AND c.analyzed_at <= $4)
                OR e.track_id IS NULL OR e.model_id <> $2 OR e.strategy <> $3 OR e.source_hash <> t.content_hash
                OR (NOT e.succeeded AND e.analyzed_at <= $4)
             ORDER BY t.created_at DESC
             LIMIT $5",
            cues::VERSION,
            clap::MODEL_ID,
            clap::windows::STRATEGY,
            retry_before,
            BATCH
        )
        .fetch_all(db)
        .await?;
        if batch.is_empty() {
            return Ok(analysed);
        }

        for track in batch {
            if cancel.is_cancelled() {
                return Ok(analysed);
            }
            let started = Instant::now();
            let outcome = analyze(storage, embedder, &track.file_path, f64::from(track.duration_seconds)).await;
            match save(db, track.id, &track.content_hash, &outcome).await {
                Err(sqlx::Error::Database(error)) if error.is_foreign_key_violation() => {
                    tracing::debug!("Track {} was deleted during analysis", track.id);
                    continue;
                }
                other => other?,
            }
            analysed += 1;
            tracing::debug!(
                "Analysed track {} in {:.1} s: cues {}, embedding {}",
                track.id,
                started.elapsed().as_secs_f64(),
                if outcome.cues.is_some() { "found" } else { "failed" },
                if outcome.vector.is_ok() { "succeeded" } else { "failed" }
            );
        }
    }
}

struct Outcome {
    cues: Option<cues::TrackCuePoints>,

    vector: Result<Vec<f32>, &'static str>,
}

async fn analyze(storage: &Storage, embedder: &Arc<ClapEmbedder>, file_path: &str, duration: f64) -> Outcome {
    let Some(source) = storage.existing(file_path) else {
        tracing::warn!("Track at {file_path} cannot be analysed: the file is missing from storage");
        return Outcome { cues: None, vector: Err("source_missing") };
    };

    let embedder = Arc::clone(embedder);
    let work = tokio::task::spawn_blocking(move || -> anyhow::Result<(Analysis, anyhow::Result<Vec<f32>>)> {
        let analysis = media::analysis::analyze(&source, duration)?;
        let vector = embedder.embed(&analysis.windows);
        Ok((analysis, vector))
    })
    .await;

    match work {
        Ok(Ok((analysis, vector))) => Outcome {
            cues: analysis.cues,
            vector: vector.map_err(|error| {
                tracing::warn!("Embedding of {file_path} failed: {error:#}");
                "embedding_failed"
            }),
        },
        Ok(Err(error)) => {
            tracing::warn!("Analysis of {file_path} failed: {error:#}");
            Outcome { cues: None, vector: Err("embedding_failed") }
        }
        Err(error) => {
            tracing::error!("Analysis of {file_path} crashed: {error}");
            Outcome { cues: None, vector: Err("embedding_failed") }
        }
    }
}

fn milliseconds(seconds: f64) -> f64 {
    (seconds * 1000.0).round_ties_even() / 1000.0
}

async fn save(db: &PgPool, track: Uuid, content_hash: &str, outcome: &Outcome) -> Result<(), sqlx::Error> {
    let now = Utc::now();
    let cues = outcome.cues.as_ref();
    let mut tx = db.begin().await?;

    sqlx::query!(
        "INSERT INTO track_cues (track_id, source_hash, version, succeeded, audio_start_seconds, audio_end_seconds,
                                 outro_start_seconds, tempo_bpm, waveform, analyzed_at)
         VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
         ON CONFLICT (track_id) DO UPDATE SET
             source_hash = EXCLUDED.source_hash, version = EXCLUDED.version, succeeded = EXCLUDED.succeeded,
             audio_start_seconds = EXCLUDED.audio_start_seconds, audio_end_seconds = EXCLUDED.audio_end_seconds,
             outro_start_seconds = EXCLUDED.outro_start_seconds, tempo_bpm = EXCLUDED.tempo_bpm,
             waveform = EXCLUDED.waveform, analyzed_at = EXCLUDED.analyzed_at",
        track,
        content_hash,
        cues::VERSION,
        cues.is_some(),
        cues.map(|c| milliseconds(c.audio_start_seconds)),
        cues.map(|c| milliseconds(c.audio_end_seconds)),
        cues.map(|c| milliseconds(c.outro_start_seconds)),
        cues.and_then(|c| c.tempo_bpm),
        cues.map(|c| c.waveform.clone()),
        now
    )
    .execute(&mut *tx)
    .await?;

    let (vector, error) = match &outcome.vector {
        Ok(vector) => (vector.clone(), None),
        Err(code) => (Vec::new(), Some(*code)),
    };
    sqlx::query!(
        "INSERT INTO track_embeddings (track_id, vector, dimension, model_id, strategy, source_hash, succeeded, error, analyzed_at)
         VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
         ON CONFLICT (track_id) DO UPDATE SET
             vector = EXCLUDED.vector, dimension = EXCLUDED.dimension, model_id = EXCLUDED.model_id,
             strategy = EXCLUDED.strategy, source_hash = EXCLUDED.source_hash, succeeded = EXCLUDED.succeeded,
             error = EXCLUDED.error, analyzed_at = EXCLUDED.analyzed_at",
        track,
        &vector,
        i32::try_from(vector.len()).unwrap_or(0),
        clap::MODEL_ID,
        clap::windows::STRATEGY,
        content_hash,
        error.is_none(),
        error,
        now
    )
    .execute(&mut *tx)
    .await?;

    tx.commit().await?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cue_seconds_keep_three_decimals() {
        assert_eq!(milliseconds(36.014_149_659_863_946), 36.014);
    }
}
