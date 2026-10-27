// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

use app::AppError;
use app::auth::UserDto;
use app::auth::invitations::{self, RegisterRequest};
use app::auth::oidc::Intent;
use axum::extract::{Query, State};
use axum::http::{HeaderMap, StatusCode};
use axum::response::{IntoResponse, Redirect, Response};
use axum::routing::{get, post};
use axum::{Extension, Router};
use serde::{Deserialize, Serialize};

use crate::auth::{self, CurrentUser};
use crate::client::ClientInfo;
use crate::extract::{Json, RoutePath};
use crate::problem::{ApiError, ApiResult};
use crate::state::AppState;

pub fn public() -> Router<AppState> {
    Router::new()
        .route("/api/auth/login", post(login))
        .route("/api/auth/refresh", post(refresh))
        .route("/api/auth/logout", post(logout))
        .route("/api/auth/register", post(register))
        .route("/api/auth/invitations/{token}", get(invitation))
        .route("/api/auth/providers", get(providers))
        .route("/api/auth/oidc/start", get(sso_start))
        .route("/api/auth/oidc/callback", get(sso_callback))
}

pub fn protected() -> Router<AppState> {
    Router::new().route("/api/auth/me", get(me))
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct LoginRequest {
    username: String,
    password: String,
    device_name: Option<String>,
    #[serde(default)]
    long_lived: bool,
}

async fn login(
    State(state): State<AppState>,
    info: Option<Extension<ClientInfo>>,
    headers: HeaderMap,
    Json(request): Json<LoginRequest>,
) -> ApiResult<impl IntoResponse> {
    let client = auth::client(info.as_deref(), &headers, request.device_name, request.long_lived);
    let result = state.auth.login(&request.username, &request.password, &client).await?;
    let secure = auth::require_secure(&state, info.as_deref());
    Ok((auth::write_session(&result, secure), Json(result.user)))
}

async fn refresh(State(state): State<AppState>, info: Option<Extension<ClientInfo>>, headers: HeaderMap) -> Response {
    let secure = auth::require_secure(&state, info.as_deref());
    let client = auth::client(info.as_deref(), &headers, None, false);

    match state.auth.refresh(auth::refresh_token(&headers).as_deref(), &client).await {
        Ok(result) => (auth::write_session(&result, secure), Json(result.user)).into_response(),

        Err(error @ AppError::Authentication(_)) => {
            (auth::clear_session(secure), ApiError::http(error)).into_response()
        }
        Err(error) => ApiError::from(error).into_response(),
    }
}

async fn logout(
    State(state): State<AppState>,
    info: Option<Extension<ClientInfo>>,
    headers: HeaderMap,
) -> ApiResult<impl IntoResponse> {
    state.auth.logout(auth::refresh_token(&headers).as_deref()).await?;
    let secure = auth::require_secure(&state, info.as_deref());
    Ok((auth::clear_session(secure), StatusCode::NO_CONTENT))
}

async fn me(State(state): State<AppState>, CurrentUser(user): CurrentUser) -> ApiResult<Json<UserDto>> {
    Ok(Json(state.auth.user(user.user_id).await?))
}

async fn register(
    State(state): State<AppState>,
    info: Option<Extension<ClientInfo>>,
    headers: HeaderMap,
    Json(request): Json<RegisterRequest>,
) -> ApiResult<impl IntoResponse> {
    let client = auth::client(info.as_deref(), &headers, None, false);
    let result = state.auth.register(request, &client).await?;
    let secure = auth::require_secure(&state, info.as_deref());
    Ok((StatusCode::CREATED, auth::write_session(&result, secure), Json(result.user)))
}

async fn invitation(
    State(state): State<AppState>,
    RoutePath(token): RoutePath<String>,
) -> ApiResult<impl IntoResponse> {
    Ok(Json(invitations::check(&state.db, &token).await?))
}

#[derive(Serialize)]
struct SsoProvider {
    name: String,
}

#[derive(Serialize)]
struct Providers {
    sso: Option<SsoProvider>,
}

async fn providers(State(state): State<AppState>) -> Json<Providers> {
    Json(Providers { sso: state.auth.oidc().map(|oidc| SsoProvider { name: oidc.name().to_owned() }) })
}

#[derive(Deserialize)]
struct StartQuery {
    intent: Option<String>,
}

async fn sso_start(
    State(state): State<AppState>,
    info: Option<Extension<ClientInfo>>,
    headers: HeaderMap,
    Query(query): Query<StartQuery>,
) -> Response {
    let Some(oidc) = state.auth.oidc() else { return StatusCode::NOT_FOUND.into_response() };
    let intent = match query.intent.as_deref() {
        Some("link") => match auth::principal_from_cookie(&state, &headers) {
            Some(principal) => Intent::Link(principal.user_id),
            None => return Redirect::to("/login").into_response(),
        },
        _ => Intent::SignIn,
    };
    let redirect_uri = format!("{}/api/auth/oidc/callback", auth::public_origin(&state, info.as_deref(), &headers));
    let secure = auth::require_secure(&state, info.as_deref());
    match oidc.start(intent, redirect_uri).await {
        Ok(started) => (auth::sso_state(started.state, secure), Redirect::to(&started.url)).into_response(),
        Err(_) => Redirect::to(&landing(intent, "failed")).into_response(),
    }
}

#[derive(Deserialize)]
struct CallbackQuery {
    state: Option<String>,
    code: Option<String>,
    error: Option<String>,
}

async fn sso_callback(
    State(state): State<AppState>,
    info: Option<Extension<ClientInfo>>,
    headers: HeaderMap,
    Query(query): Query<CallbackQuery>,
) -> Response {
    let secure = auth::require_secure(&state, info.as_deref());
    let Some(oidc) = state.auth.oidc() else { return StatusCode::NOT_FOUND.into_response() };
    let cleared = auth::clear_sso_state(secure);
    let Some(state_value) = query.state.filter(|value| Some(value) == auth::sso_state_of(&headers).as_ref()) else {
        return (cleared, Redirect::to("/login?sso=expired")).into_response();
    };
    let intent = oidc.intent(&state_value).unwrap_or(Intent::SignIn);
    if let Some(error) = query.error {
        tracing::info!("The identity provider declined single sign-on: {error}");
        return (cleared, Redirect::to(&landing(intent, "cancelled"))).into_response();
    }
    let Some(code) = query.code else { return (cleared, Redirect::to(&landing(intent, "failed"))).into_response() };

    let (intent, identity) = match oidc.finish(&state_value, &code).await {
        Ok(done) => done,
        Err(error) => return (cleared, Redirect::to(&landing(intent, outcome(&error)))).into_response(),
    };
    match intent {
        Intent::SignIn => {
            let client = auth::client(info.as_deref(), &headers, None, false);
            match state.auth.sso_sign_in(&identity, &client).await {
                Ok(result) => (cleared, auth::write_session(&result, secure), Redirect::to("/")).into_response(),
                Err(error) => (cleared, Redirect::to(&landing(intent, outcome(&error)))).into_response(),
            }
        }
        Intent::Link(user) => match state.auth.link_identity(user, &identity).await {
            Ok(()) => (cleared, Redirect::to(&landing(intent, "linked"))).into_response(),
            Err(error) => (cleared, Redirect::to(&landing(intent, outcome(&error)))).into_response(),
        },
    }
}

fn landing(intent: Intent, outcome: &str) -> String {
    match intent {
        Intent::SignIn => format!("/login?sso={outcome}"),
        Intent::Link(_) => format!("/settings?sso={outcome}"),
    }
}

fn outcome(error: &AppError) -> &'static str {
    match error.message().map(|message| message.template) {
        Some("No account is linked to this sign-in.") => "not_linked",
        Some("This account has been deactivated.") => "deactivated",
        Some("Single sign-on expired.") => "expired",
        Some("This sign-in is already linked to another account.") => "taken",
        _ => "failed",
    }
}
