// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

use app::AppError;
use app::auth::UserDto;
use axum::extract::State;
use axum::http::{HeaderMap, StatusCode};
use axum::response::{IntoResponse, Response};
use axum::routing::{get, post};
use axum::{Extension, Router};
use serde::Deserialize;

use crate::auth::{self, CurrentUser};
use crate::client::ClientInfo;
use crate::extract::Json;
use crate::problem::{ApiError, ApiResult};
use crate::state::AppState;

pub fn public() -> Router<AppState> {
    Router::new()
        .route("/api/auth/login", post(login))
        .route("/api/auth/refresh", post(refresh))
        .route("/api/auth/logout", post(logout))
}

pub fn protected() -> Router<AppState> {
    Router::new().route("/api/auth/me", get(me))
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct LoginRequest {
    username: String,
    password: String,
}

async fn login(
    State(state): State<AppState>,
    client: Option<Extension<ClientInfo>>,
    Json(request): Json<LoginRequest>,
) -> ApiResult<impl IntoResponse> {
    let result = state.auth.login(&request.username, &request.password).await?;
    let secure = auth::require_secure(&state, client.as_deref());
    Ok((auth::write_session(&result, secure), Json(result.user)))
}

async fn refresh(State(state): State<AppState>, client: Option<Extension<ClientInfo>>, headers: HeaderMap) -> Response {
    let secure = auth::require_secure(&state, client.as_deref());

    match state.auth.refresh(auth::refresh_token(&headers).as_deref()).await {
        Ok(result) => (auth::write_session(&result, secure), Json(result.user)).into_response(),

        Err(error @ AppError::Authentication(_)) => {
            (auth::clear_session(secure), ApiError::http(error)).into_response()
        }
        Err(error) => ApiError::from(error).into_response(),
    }
}

async fn logout(
    State(state): State<AppState>,
    client: Option<Extension<ClientInfo>>,
    headers: HeaderMap,
) -> ApiResult<impl IntoResponse> {
    state.auth.logout(auth::refresh_token(&headers).as_deref()).await?;
    let secure = auth::require_secure(&state, client.as_deref());
    Ok((auth::clear_session(secure), StatusCode::NO_CONTENT))
}

async fn me(State(state): State<AppState>, CurrentUser(user): CurrentUser) -> ApiResult<Json<UserDto>> {
    Ok(Json(state.auth.user(user.user_id).await?))
}
