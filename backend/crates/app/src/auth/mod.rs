// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

pub mod password;
pub mod sessions;
pub mod tokens;

use std::sync::Arc;

use chrono::{DateTime, Duration, Utc};
use serde::Serialize;
use sqlx::{PgConnection, PgPool};
use uuid::Uuid;

use crate::text::normalize_username;
use crate::{AppError, AppResult};
use sessions::LiveSessions;
use tokens::{Principal, TokenService, hash_refresh_token};

const REUSE_GRACE: Duration = Duration::seconds(20);

const STALE_TOKEN_AGE: Duration = Duration::days(1);

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UserDto {
    pub id: Uuid,
    pub username: String,
    pub is_admin: bool,
}

pub struct AuthResult {
    pub user: UserDto,
    pub access_token: String,
    pub access_token_expires_at: DateTime<Utc>,
    pub refresh_token: String,
    pub refresh_token_expires_at: DateTime<Utc>,
}

#[derive(Clone)]
pub struct AuthService {
    db: PgPool,
    tokens: Arc<TokenService>,
    sessions: LiveSessions,
}

impl AuthService {
    pub fn new(db: PgPool, tokens: Arc<TokenService>, sessions: LiveSessions) -> Self {
        Self { db, tokens, sessions }
    }

    pub fn tokens(&self) -> &TokenService {
        &self.tokens
    }

    pub async fn login(&self, username: &str, password: &str) -> AppResult<AuthResult> {
        let username = normalize_username(username);

        let user = sqlx::query!(
            "SELECT id, username, password_hash, is_admin, is_active FROM users WHERE username = $1",
            username
        )
        .fetch_optional(&self.db)
        .await?;

        let hash = user.as_ref().map(|u| u.password_hash.clone());
        let password_ok = password::verify(password.to_owned(), hash).await;

        let Some(user) = user.filter(|_| password_ok) else {
            tracing::warn!("Failed login attempt for username {username}");
            return Err(AppError::authentication("Invalid username or password."));
        };

        if !user.is_active {
            tracing::warn!("Deactivated user {} tried to sign in", user.username);
            return Err(AppError::forbidden("This account has been deactivated."));
        }

        tracing::info!("User {} signed in", user.username);

        let mut tx = self.db.begin().await?;
        let result =
            self.issue(&mut tx, UserDto { id: user.id, username: user.username, is_admin: user.is_admin }).await?;
        tx.commit().await?;
        Ok(result)
    }

    pub async fn refresh(&self, raw_refresh_token: Option<&str>) -> AppResult<AuthResult> {
        let raw = raw_refresh_token.map(str::trim).filter(|raw| !raw.is_empty());
        let Some(raw) = raw else {
            return Err(AppError::authentication("Missing refresh token."));
        };

        let now = Utc::now();
        let mut tx = self.db.begin().await?;

        let stored = sqlx::query!(
            r#"SELECT t.id, t.user_id, t.expires_at, t.revoked_at, u.username, u.is_admin, u.is_active
               FROM refresh_tokens t JOIN users u ON u.id = t.user_id
               WHERE t.token_hash = $1
               FOR UPDATE OF t"#,
            hash_refresh_token(raw)
        )
        .fetch_optional(&mut *tx)
        .await?;

        let Some(stored) = stored else {
            tracing::info!("Rejected an unknown refresh token");
            return Err(AppError::authentication("Refresh token is invalid or expired."));
        };

        if let Some(revoked_at) = stored.revoked_at {
            let session_lives_on = sqlx::query_scalar!(
                r#"SELECT EXISTS (
                       SELECT 1 FROM refresh_tokens
                       WHERE user_id = $1 AND revoked_at IS NULL AND expires_at > $2
                   ) AS "exists!""#,
                stored.user_id,
                now
            )
            .fetch_one(&mut *tx)
            .await?;

            if !session_lives_on || now - revoked_at > REUSE_GRACE {
                tracing::warn!("Refresh token reuse detected for user {}; all sessions revoked", stored.user_id);

                sqlx::query!("DELETE FROM refresh_tokens WHERE user_id = $1", stored.user_id).execute(&mut *tx).await?;
                tx.commit().await?;
                self.sessions.forget_user(stored.user_id);

                return Err(AppError::authentication("Refresh token is invalid or expired."));
            }

            tracing::debug!(
                "Refresh token of user {} was rotated moments ago; treating as a concurrent refresh",
                stored.user_id
            );
        }

        if stored.expires_at <= now || !stored.is_active {
            tracing::info!("Rejected an expired refresh token or a deactivated user");
            return Err(AppError::authentication("Refresh token is invalid or expired."));
        }

        if stored.revoked_at.is_none() {
            sqlx::query!("UPDATE refresh_tokens SET revoked_at = $2 WHERE id = $1", stored.id, now)
                .execute(&mut *tx)
                .await?;
        }

        let user = UserDto { id: stored.user_id, username: stored.username, is_admin: stored.is_admin };
        let result = self.issue(&mut tx, user).await?;
        tx.commit().await?;
        Ok(result)
    }

    pub async fn logout(&self, raw_refresh_token: Option<&str>) -> AppResult<()> {
        let Some(raw) = raw_refresh_token.map(str::trim).filter(|raw| !raw.is_empty()) else {
            return Ok(());
        };

        let removed = sqlx::query!(
            "DELETE FROM refresh_tokens WHERE token_hash = $1 RETURNING id, user_id",
            hash_refresh_token(raw)
        )
        .fetch_optional(&self.db)
        .await?;

        if let Some(removed) = removed {
            self.sessions.forget(removed.id);
            tracing::info!("User {} signed out", removed.user_id);
        }
        Ok(())
    }

    pub async fn change_password(&self, user_id: Uuid, current: &str, new: &str) -> AppResult<AuthResult> {
        let user = sqlx::query!("SELECT id, username, password_hash, is_admin FROM users WHERE id = $1", user_id)
            .fetch_optional(&self.db)
            .await?
            .ok_or_else(|| AppError::not_found("User not found."))?;

        if !password::verify(current.to_owned(), Some(user.password_hash)).await {
            return Err(AppError::forbidden("The current password is not correct."));
        }

        let new_hash = password::hash(new.to_owned()).await?;

        let mut tx = self.db.begin().await?;
        sqlx::query!("UPDATE users SET password_hash = $2 WHERE id = $1", user_id, new_hash).execute(&mut *tx).await?;
        sqlx::query!("DELETE FROM refresh_tokens WHERE user_id = $1", user_id).execute(&mut *tx).await?;
        let result =
            self.issue(&mut tx, UserDto { id: user.id, username: user.username, is_admin: user.is_admin }).await?;
        tx.commit().await?;

        self.sessions.forget_user(user_id);
        tracing::info!("User {user_id} changed their password");
        Ok(result)
    }

    pub async fn user(&self, user_id: Uuid) -> AppResult<UserDto> {
        sqlx::query_as!(UserDto, "SELECT id, username, is_admin FROM users WHERE id = $1", user_id)
            .fetch_optional(&self.db)
            .await?
            .ok_or_else(|| AppError::not_found("User not found."))
    }

    pub async fn session_alive(&self, principal: &Principal) -> AppResult<bool> {
        if self.sessions.is_known_alive(principal.session_id) {
            return Ok(true);
        }

        let owner = sqlx::query_scalar!(
            "SELECT t.user_id FROM refresh_tokens t JOIN users u ON u.id = t.user_id WHERE t.id = $1 AND u.is_active",
            principal.session_id
        )
        .fetch_optional(&self.db)
        .await?;

        Ok(match owner {
            Some(user_id) => {
                self.sessions.remember_alive(principal.session_id, user_id);
                true
            }
            None => false,
        })
    }

    pub fn forget_user_sessions(&self, user_id: Uuid) {
        self.sessions.forget_user(user_id);
    }

    async fn issue(&self, conn: &mut PgConnection, user: UserDto) -> AppResult<AuthResult> {
        let now = Utc::now();
        let refresh = self.tokens.create_refresh_token(now);
        let access = self.tokens.create_access_token(user.id, &user.username, user.is_admin, refresh.id, now)?;

        sqlx::query!(
            "INSERT INTO refresh_tokens (id, user_id, token_hash, expires_at, created_at) VALUES ($1, $2, $3, $4, $5)",
            refresh.id,
            user.id,
            refresh.hash,
            refresh.expires_at,
            refresh.created_at
        )
        .execute(&mut *conn)
        .await?;

        let cutoff = now - STALE_TOKEN_AGE;
        sqlx::query!(
            "DELETE FROM refresh_tokens WHERE user_id = $1 AND (expires_at < $2 OR revoked_at < $2)",
            user.id,
            cutoff
        )
        .execute(&mut *conn)
        .await?;

        Ok(AuthResult {
            user,
            access_token: access.value,
            access_token_expires_at: access.expires_at,
            refresh_token: refresh.raw,
            refresh_token_expires_at: refresh.expires_at,
        })
    }
}
