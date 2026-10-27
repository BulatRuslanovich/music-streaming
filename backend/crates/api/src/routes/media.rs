// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

use app::covers::{self, MAX_IMAGE_BYTES, UploadedImage};
use app::i18n::Lang;
use app::library::settings::AudioQuality;
use app::storage::CoverSize;
use app::streaming::{self, ImageFile};
use app::upload::{self, ProbeFile};
use app::{AppError, AppResult};
use axum::Router;
use axum::body::Body;
use axum::extract::{DefaultBodyLimit, Multipart, State};
use axum::http::{HeaderMap, HeaderValue, StatusCode, header};
use axum::response::{IntoResponse, Response};
use axum::routing::{get, post};
use futures_util::TryStreamExt;
use serde::{Deserialize, Serialize};
use tokio_util::io::StreamReader;
use uuid::Uuid;

use crate::auth::{Admin, CurrentUser};
use crate::extract::{Json, Query, RoutePath};
use crate::files::FileReply;
use crate::problem::{ApiError, ApiResult, validation_problem};
use crate::state::AppState;

const IMAGE_CACHE: &str = "private, max-age=2592000, stale-while-revalidate=31536000";

pub fn router() -> Router<AppState> {
    Router::new()
        .route("/api/tracks/{id}/stream", get(stream))
        .route("/api/tracks/{id}/download", get(download))
        .route("/api/tracks/{id}/waveform", get(waveform))
        .route("/api/tracks/{id}/cover", get(track_cover))
        .route("/api/tracks/{id}/hls/master.m3u8", get(hls_master))
        .route("/api/tracks/{id}/hls/{quality}/{file_name}", get(hls_asset))
        .route("/api/tracks/upload/check", post(upload_check))
        .route("/api/tracks/upload", post(upload).layer(DefaultBodyLimit::disable()))
        .route(
            "/api/albums/{id}/cover",
            get(album_cover).post(set_album_cover).delete(remove_album_cover).layer(DefaultBodyLimit::disable()),
        )
        .route(
            "/api/artists/{id}/image",
            get(artist_image).post(set_artist_image).delete(remove_artist_image).layer(DefaultBodyLimit::disable()),
        )
        .route(
            "/api/playlists/{id}/cover",
            get(playlist_cover)
                .post(set_playlist_cover)
                .delete(remove_playlist_cover)
                .layer(DefaultBodyLimit::disable()),
        )
}

async fn stream(
    State(state): State<AppState>,
    RoutePath(id): RoutePath<Uuid>,
    headers: HeaderMap,
) -> ApiResult<Response> {
    let track = streaming::open_track(&state.db, &state.storage, id).await?;
    let reply = FileReply {
        path: track.path,
        content_type: track.content_type,
        etag: track.etag,
        cache_control: "private, max-age=604800",
        download_name: None,
        ranges: true,
    };
    Ok(reply.send(&headers).await)
}

async fn download(
    State(state): State<AppState>,
    RoutePath(id): RoutePath<Uuid>,
    headers: HeaderMap,
) -> ApiResult<Response> {
    let track = streaming::open_track(&state.db, &state.storage, id).await?;
    let reply = FileReply {
        path: track.path,
        content_type: track.content_type,
        etag: track.etag,
        cache_control: "private, no-store",
        download_name: Some(track.download_name),
        ranges: true,
    };
    Ok(reply.send(&headers).await)
}

#[derive(Serialize)]
struct Waveform {
    peaks: Vec<i32>,
}

async fn waveform(State(state): State<AppState>, RoutePath(id): RoutePath<Uuid>) -> ApiResult<impl IntoResponse> {
    let peaks = streaming::waveform(&state.db, id).await?;
    Ok(([(header::CACHE_CONTROL, "private, max-age=86400")], Json(Waveform { peaks })))
}

#[derive(Deserialize)]
struct SizeQuery {
    size: Option<String>,
}

fn cover_size(query: &SizeQuery) -> Result<CoverSize, Box<Response>> {
    match query.size.as_deref().map(str::trim).filter(|s| !s.is_empty()) {
        None => Ok(CoverSize::Full),
        Some(text) => CoverSize::parse(text)
            .ok_or_else(|| Box::new(validation_problem("size", format!("The value '{text}' is not valid.")))),
    }
}

async fn image_reply(image: AppResult<ImageFile>, headers: &HeaderMap) -> Response {
    match image {
        Ok(image) => {
            let reply = FileReply {
                path: image.path,
                content_type: image.content_type.to_owned(),
                etag: image.etag,
                cache_control: IMAGE_CACHE,
                download_name: None,
                ranges: false,
            };
            reply.send(headers).await
        }
        Err(error) => ApiError::from(error).into_response(),
    }
}

async fn track_cover(
    State(state): State<AppState>,
    RoutePath(id): RoutePath<Uuid>,
    Query(query): Query<SizeQuery>,
    headers: HeaderMap,
) -> Response {
    match cover_size(&query) {
        Ok(size) => image_reply(streaming::track_cover(&state.db, &state.storage, id, size).await, &headers).await,
        Err(rejection) => *rejection,
    }
}

async fn album_cover(
    State(state): State<AppState>,
    RoutePath(id): RoutePath<Uuid>,
    Query(query): Query<SizeQuery>,
    headers: HeaderMap,
) -> Response {
    match cover_size(&query) {
        Ok(size) => image_reply(streaming::album_cover(&state.db, &state.storage, id, size).await, &headers).await,
        Err(rejection) => *rejection,
    }
}

async fn artist_image(
    State(state): State<AppState>,
    RoutePath(id): RoutePath<Uuid>,
    Query(query): Query<SizeQuery>,
    headers: HeaderMap,
) -> Response {
    match cover_size(&query) {
        Ok(size) => image_reply(streaming::artist_image(&state.db, &state.storage, id, size).await, &headers).await,
        Err(rejection) => *rejection,
    }
}

async fn playlist_cover(
    State(state): State<AppState>,
    CurrentUser(user): CurrentUser,
    RoutePath(id): RoutePath<Uuid>,
    Query(query): Query<SizeQuery>,
    headers: HeaderMap,
) -> Response {
    match cover_size(&query) {
        Ok(size) => {
            image_reply(streaming::playlist_cover(&state.db, &state.storage, user.user_id, id, size).await, &headers)
                .await
        }
        Err(rejection) => *rejection,
    }
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct MasterQuery {
    max_quality: Option<String>,
}

fn quality(field: &str, value: Option<&str>, default: AudioQuality) -> Result<AudioQuality, Box<Response>> {
    match value.map(str::trim).filter(|v| !v.is_empty()) {
        None => Ok(default),
        Some(text) => AudioQuality::parse(text)
            .ok_or_else(|| Box::new(validation_problem(field, format!("The value '{text}' is not valid.")))),
    }
}

async fn hls_master(
    State(state): State<AppState>,
    RoutePath(id): RoutePath<Uuid>,
    Query(query): Query<MasterQuery>,
) -> Response {
    let max = match quality("maxQuality", query.max_quality.as_deref(), AudioQuality::Normal) {
        Ok(max) => max,
        Err(rejection) => return *rejection,
    };
    let master = match streaming::hls_master(&state.db, &state.storage, &state.transcodes, id, max).await {
        Ok(master) => master,
        Err(error) => return ApiError::from(error).into_response(),
    };

    let mut headers = HeaderMap::new();
    if let Ok(etag) = HeaderValue::from_str(&master.etag) {
        headers.insert(header::ETAG, etag);
    }
    match master.playlist {
        Some(playlist) => {
            headers.insert(
                header::CACHE_CONTROL,
                HeaderValue::from_static("private, max-age=3600, stale-while-revalidate=86400"),
            );
            headers.insert(header::CONTENT_TYPE, HeaderValue::from_static("application/vnd.apple.mpegurl"));
            (StatusCode::OK, headers, playlist).into_response()
        }
        None => {
            headers.insert(header::CACHE_CONTROL, HeaderValue::from_static("no-store"));
            headers.insert(header::RETRY_AFTER, HeaderValue::from_static("2"));
            (StatusCode::ACCEPTED, headers).into_response()
        }
    }
}

async fn hls_asset(
    State(state): State<AppState>,
    RoutePath((id, quality_name, file_name)): RoutePath<(Uuid, String, String)>,
    headers: HeaderMap,
) -> Response {
    let quality = match quality("quality", Some(&quality_name), AudioQuality::Normal) {
        Ok(quality) => quality,
        Err(rejection) => return *rejection,
    };
    match streaming::hls_asset(&state.db, &state.storage, &state.hashes, id, quality, &file_name).await {
        Ok(asset) => {
            let reply = FileReply {
                path: asset.path,
                content_type: asset.content_type.to_owned(),
                etag: asset.etag,
                cache_control: "private, max-age=31536000, immutable",
                download_name: None,
                ranges: true,
            };
            reply.send(&headers).await
        }
        Err(error) => ApiError::from(error).into_response(),
    }
}

#[derive(Deserialize)]
struct ProbeRequest {
    files: Vec<ProbeFile>,
}

async fn upload_check(
    State(state): State<AppState>,
    Admin(user): Admin,
    Json(request): Json<ProbeRequest>,
) -> ApiResult<impl IntoResponse> {
    Ok(Json(upload::probe(&state.db, user.user_id, request.files).await?))
}

async fn upload(State(state): State<AppState>, Admin(user): Admin, headers: HeaderMap, body: Body) -> Response {
    let Some(raw_name) = headers.get("x-file-name").and_then(|value| value.to_str().ok()).filter(|v| !v.is_empty())
    else {
        return validation_problem("fileName", "The fileName field is required.".to_owned());
    };
    let file_name = percent_decode(raw_name);
    let length = headers.get(header::CONTENT_LENGTH).and_then(|v| v.to_str().ok()).and_then(|v| v.parse().ok());
    let lang = headers
        .get(header::ACCEPT_LANGUAGE)
        .and_then(|value| value.to_str().ok())
        .map(Lang::from_accept_language)
        .unwrap_or_default();

    let reader = StreamReader::new(body.into_data_stream().map_err(std::io::Error::other));
    let result = state.uploads.upload(user.user_id, &file_name, length, reader, lang).await;
    let status = if result.uploaded.is_empty() { StatusCode::BAD_REQUEST } else { StatusCode::OK };
    (status, Json(result)).into_response()
}

fn percent_decode(value: &str) -> String {
    let bytes = value.as_bytes();
    let mut decoded = Vec::with_capacity(bytes.len());
    let mut index = 0;
    let hex = |byte: u8| char::from(byte).to_digit(16).and_then(|digit| u8::try_from(digit).ok());
    while index < bytes.len() {
        if bytes[index] == b'%'
            && index + 2 < bytes.len()
            && let (Some(high), Some(low)) = (hex(bytes[index + 1]), hex(bytes[index + 2]))
        {
            decoded.push(high * 16 + low);
            index += 3;
        } else {
            decoded.push(bytes[index]);
            index += 1;
        }
    }
    String::from_utf8_lossy(&decoded).into_owned()
}

async fn read_image(mut form: Multipart) -> AppResult<UploadedImage> {
    while let Some(mut field) = form.next_field().await.map_err(|error| AppError::Internal(error.into()))? {
        if field.name() != Some("file") {
            continue;
        }
        let file_name = field.file_name().unwrap_or_default().to_owned();
        let content_type = field.content_type().map(str::to_owned);
        let mut bytes = Vec::new();
        while let Some(chunk) = field.chunk().await.map_err(|error| AppError::Internal(error.into()))? {
            bytes.extend_from_slice(&chunk);
            if i64::try_from(bytes.len()).unwrap_or(i64::MAX) > MAX_IMAGE_BYTES {
                return Err(AppError::payload_too_large("The file exceeds the {0} MB upload limit.")
                    .arg(MAX_IMAGE_BYTES / (1024 * 1024)));
            }
        }
        if bytes.is_empty() {
            break;
        }
        return Ok(UploadedImage { bytes, content_type, file_name });
    }
    Err(AppError::validation("No image was provided."))
}

async fn set_album_cover(
    State(state): State<AppState>,
    Admin(_): Admin,
    RoutePath(id): RoutePath<Uuid>,
    form: Multipart,
) -> ApiResult<impl IntoResponse> {
    let image = read_image(form).await?;
    Ok(Json(covers::set_album_cover(&state.db, &state.storage, id, image).await?))
}

async fn remove_album_cover(
    State(state): State<AppState>,
    Admin(_): Admin,
    RoutePath(id): RoutePath<Uuid>,
) -> ApiResult<StatusCode> {
    covers::remove_album_cover(&state.db, &state.storage, id).await?;
    Ok(StatusCode::NO_CONTENT)
}

async fn set_artist_image(
    State(state): State<AppState>,
    Admin(_): Admin,
    RoutePath(id): RoutePath<Uuid>,
    form: Multipart,
) -> ApiResult<impl IntoResponse> {
    let image = read_image(form).await?;
    Ok(Json(covers::set_artist_image(&state.db, &state.storage, id, image).await?))
}

async fn remove_artist_image(
    State(state): State<AppState>,
    Admin(_): Admin,
    RoutePath(id): RoutePath<Uuid>,
) -> ApiResult<StatusCode> {
    covers::remove_artist_image(&state.db, &state.storage, id).await?;
    Ok(StatusCode::NO_CONTENT)
}

async fn set_playlist_cover(
    State(state): State<AppState>,
    CurrentUser(user): CurrentUser,
    RoutePath(id): RoutePath<Uuid>,
    form: Multipart,
) -> ApiResult<impl IntoResponse> {
    let image = read_image(form).await?;
    Ok(Json(covers::set_playlist_cover(&state.db, &state.storage, user.user_id, id, image).await?))
}

async fn remove_playlist_cover(
    State(state): State<AppState>,
    CurrentUser(user): CurrentUser,
    RoutePath(id): RoutePath<Uuid>,
) -> ApiResult<StatusCode> {
    covers::remove_playlist_cover(&state.db, &state.storage, user.user_id, id).await?;
    Ok(StatusCode::NO_CONTENT)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn file_names_are_percent_decoded() {
        assert_eq!(percent_decode("Mira%20Vale%20-%20%D0%A4.flac"), "Mira Vale - Ф.flac");
    }

    #[test]
    fn broken_escapes_stay_as_they_are() {
        assert_eq!(percent_decode("100%zz%2"), "100%zz%2");
    }
}
