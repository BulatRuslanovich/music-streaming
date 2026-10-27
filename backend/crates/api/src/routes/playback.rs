// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

use std::convert::Infallible;
use std::sync::Arc;
use std::time::Duration;

use app::playback::registry::{Holder, PlaybackRegistry};
use app::playback::signals::{self, RecordEventsRequest};
use app::playback::{self, PlaybackStateReport};
use axum::Router;
use axum::extract::State;
use axum::http::StatusCode;
use axum::response::sse::{Event, Sse};
use axum::response::{IntoResponse, Response};
use axum::routing::{get, post, put};
use futures_util::Stream;
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use crate::auth::CurrentUser;
use crate::extract::{Json, Query};
use crate::problem::ApiResult;
use crate::state::AppState;

const HEARTBEAT: Duration = Duration::from_secs(20);

pub fn router() -> Router<AppState> {
    Router::new()
        .route("/api/playback/state", put(report_state))
        .route("/api/playback/now", get(playing_elsewhere))
        .route("/api/playback/handoff", post(handoff))
        .route("/api/playback/session", get(session))
        .route("/api/playback/signals", post(record_signals))
}

async fn report_state(
    State(state): State<AppState>,
    CurrentUser(user): CurrentUser,
    Json(report): Json<PlaybackStateReport>,
) -> ApiResult<StatusCode> {
    playback::report(&state.playback, user.user_id, report)?;
    Ok(StatusCode::NO_CONTENT)
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct DeviceQuery {
    device_id: Option<String>,
    device_name: Option<String>,
}

async fn playing_elsewhere(
    State(state): State<AppState>,
    CurrentUser(user): CurrentUser,
    Query(query): Query<DeviceQuery>,
) -> ApiResult<Response> {
    Ok(match playback::elsewhere(&state.db, &state.playback, user.user_id, query.device_id.as_deref()).await? {
        Some(elsewhere) => Json(elsewhere).into_response(),
        None => StatusCode::NO_CONTENT.into_response(),
    })
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct HandoffRequest {
    device_id: Option<String>,
}

async fn handoff(
    State(state): State<AppState>,
    CurrentUser(user): CurrentUser,
    Json(request): Json<HandoffRequest>,
) -> ApiResult<impl IntoResponse> {
    Ok(Json(playback::handoff(&state.db, &state.playback, user.user_id, request.device_id.as_deref()).await?))
}

async fn record_signals(
    State(state): State<AppState>,
    CurrentUser(user): CurrentUser,
    Json(request): Json<RecordEventsRequest>,
) -> ApiResult<impl IntoResponse> {
    Ok((StatusCode::ACCEPTED, Json(signals::record(&state.db, user.user_id, request).await?)))
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct TakeoverDto {
    device_id: String,
    device_name: String,
}

struct Claim {
    registry: PlaybackRegistry,
    user: Uuid,
    holder: Arc<Holder>,
}

impl Drop for Claim {
    fn drop(&mut self) {
        self.registry.release(self.user, &self.holder);
    }
}

async fn session(
    State(state): State<AppState>,
    CurrentUser(user): CurrentUser,
    Query(query): Query<DeviceQuery>,
) -> ApiResult<Sse<impl Stream<Item = Result<Event, Infallible>>>> {
    let device_id = playback::require_device(query.device_id.as_deref())?.to_owned();
    let device_name = query.device_name.unwrap_or_default();
    let holder = state.playback.claim(user.user_id, &device_id, &device_name);
    let claim = Claim { registry: state.playback, user: user.user_id, holder };

    enum Step {
        Claimed,
        Watching,
        Done,
    }

    let events =
        futures_util::stream::unfold((Step::Claimed, claim, device_id), |(step, claim, device_id)| async move {
            match step {
                Step::Claimed => {
                    let event = Event::default().event("claimed").data(device_id.clone());
                    Some((Ok(event), (Step::Watching, claim, device_id)))
                }
                Step::Watching => match claim.holder.displaced_within(HEARTBEAT).await {
                    None => Some((Ok(Event::default().event("ping").data("")), (Step::Watching, claim, device_id))),
                    Some(by) => {
                        let takeover = TakeoverDto { device_id: by.device_id, device_name: by.device_name };
                        let data = serde_json::to_string(&takeover).unwrap_or_default();
                        Some((Ok(Event::default().event("displaced").data(data)), (Step::Done, claim, device_id)))
                    }
                },
                Step::Done => None,
            }
        });

    Ok(Sse::new(events))
}
