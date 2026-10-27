// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

use std::time::{Duration, Instant};

use base64::Engine;
use base64::engine::general_purpose::URL_SAFE_NO_PAD;
use chrono::Utc;
use jsonwebtoken::jwk::JwkSet;
use jsonwebtoken::{Algorithm, DecodingKey, Validation};
use moka::sync::Cache;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use tokio::sync::RwLock;
use uuid::Uuid;

use super::journal::{self, Attempt, Method};
use super::{AuthResult, AuthService, Client, SessionStart, UserDto, password};
use crate::config::OidcConfig;
use crate::text::{normalize_username, valid_username};
use crate::{AppError, AppResult, json};

const PENDING_FOR: Duration = Duration::from_secs(10 * 60);
const METADATA_FOR: Duration = Duration::from_secs(60 * 60);
const LEEWAY_SECONDS: u64 = 60;
const ALLOWED: [Algorithm; 9] = [
    Algorithm::RS256,
    Algorithm::RS384,
    Algorithm::RS512,
    Algorithm::PS256,
    Algorithm::PS384,
    Algorithm::PS512,
    Algorithm::ES256,
    Algorithm::ES384,
    Algorithm::EdDSA,
];

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Intent {
    SignIn,
    Link(Uuid),
}

#[derive(Debug, Clone)]
struct Pending {
    nonce: String,
    verifier: String,
    redirect_uri: String,
    intent: Intent,
}

#[derive(Debug, Clone, Deserialize)]
struct Discovery {
    issuer: String,
    authorization_endpoint: String,
    token_endpoint: String,
    jwks_uri: String,
}

#[derive(Debug, Deserialize)]
struct TokenResponse {
    id_token: Option<String>,
}

#[derive(Debug, Deserialize)]
struct IdClaims {
    sub: String,
    nonce: Option<String>,
    azp: Option<String>,
    preferred_username: Option<String>,
    email: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Identity {
    pub issuer: String,
    pub subject: String,
    pub username: Option<String>,
}

pub struct Started {
    pub url: String,
    pub state: String,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LinkedIdentityDto {
    pub provider: String,
    #[serde(serialize_with = "json::time")]
    pub linked_at: chrono::DateTime<Utc>,
}

pub struct Oidc {
    config: OidcConfig,
    http: reqwest::Client,
    discovery: RwLock<Option<(Instant, Discovery)>>,
    keys: RwLock<Option<(Instant, JwkSet)>>,
    pending: Cache<String, Pending>,
}

impl Oidc {
    pub fn new(config: OidcConfig) -> Self {
        Self {
            config,
            http: reqwest::Client::new(),
            discovery: RwLock::new(None),
            keys: RwLock::new(None),
            pending: Cache::builder().max_capacity(10_000).time_to_live(PENDING_FOR).build(),
        }
    }

    pub fn name(&self) -> &str {
        &self.config.name
    }

    pub async fn start(&self, intent: Intent, redirect_uri: String) -> AppResult<Started> {
        let discovery = self.discovery().await?;
        let state = random_token();
        let nonce = random_token();
        let verifier = random_token();
        let challenge = URL_SAFE_NO_PAD.encode(Sha256::digest(verifier.as_bytes()));

        let mut url = reqwest::Url::parse(&discovery.authorization_endpoint).map_err(failed)?;
        url.query_pairs_mut()
            .append_pair("response_type", "code")
            .append_pair("client_id", &self.config.client_id)
            .append_pair("redirect_uri", &redirect_uri)
            .append_pair("scope", "openid profile email")
            .append_pair("state", &state)
            .append_pair("nonce", &nonce)
            .append_pair("code_challenge", &challenge)
            .append_pair("code_challenge_method", "S256");
        self.pending.insert(state.clone(), Pending { nonce, verifier, redirect_uri, intent });
        Ok(Started { url: url.to_string(), state })
    }

    pub fn intent(&self, state: &str) -> Option<Intent> {
        self.pending.get(state).map(|pending| pending.intent)
    }

    pub async fn finish(&self, state: &str, code: &str) -> AppResult<(Intent, Identity)> {
        let pending = self.pending.remove(state).ok_or_else(|| AppError::authentication("Single sign-on expired."))?;
        let discovery = self.discovery().await?;

        let form = [
            ("grant_type", "authorization_code"),
            ("code", code),
            ("redirect_uri", pending.redirect_uri.as_str()),
            ("code_verifier", pending.verifier.as_str()),
            ("client_id", self.config.client_id.as_str()),
        ];
        let mut request = self.http.post(&discovery.token_endpoint);
        if let Some(secret) = &self.config.client_secret {
            request = request.basic_auth(&self.config.client_id, Some(secret));
        }
        let response = request.form(&form).send().await.map_err(failed)?;
        if !response.status().is_success() {
            let status = response.status();
            let body = response.text().await.unwrap_or_default();
            return Err(failed(anyhow::anyhow!("token endpoint answered {status}: {body}")));
        }
        let tokens: TokenResponse = response.json().await.map_err(failed)?;
        let id_token = tokens.id_token.ok_or_else(|| failed(anyhow::anyhow!("no id_token in the token response")))?;

        let claims = self.verify(&discovery, &id_token).await?;
        if claims.nonce.as_deref() != Some(pending.nonce.as_str()) {
            return Err(failed(anyhow::anyhow!("nonce mismatch")));
        }
        if claims.azp.as_deref().is_some_and(|azp| azp != self.config.client_id) {
            return Err(failed(anyhow::anyhow!("azp is another client")));
        }
        let username = claims
            .preferred_username
            .or_else(|| claims.email.and_then(|email| email.split('@').next().map(str::to_owned)));
        Ok((pending.intent, Identity { issuer: self.config.issuer.clone(), subject: claims.sub, username }))
    }

    async fn verify(&self, discovery: &Discovery, id_token: &str) -> AppResult<IdClaims> {
        let header = jsonwebtoken::decode_header(id_token).map_err(failed)?;
        if !ALLOWED.contains(&header.alg) {
            return Err(failed(anyhow::anyhow!("id_token signed with {:?}", header.alg)));
        }
        let key = self.key(discovery, header.kid.as_deref()).await?;

        let mut validation = Validation::new(header.alg);
        validation.set_issuer(&[discovery.issuer.as_str()]);
        validation.set_audience(&[self.config.client_id.as_str()]);
        validation.set_required_spec_claims(&["exp", "iss", "aud", "sub"]);
        validation.leeway = LEEWAY_SECONDS;
        Ok(jsonwebtoken::decode::<IdClaims>(id_token, &key, &validation).map_err(failed)?.claims)
    }

    async fn key(&self, discovery: &Discovery, kid: Option<&str>) -> AppResult<DecodingKey> {
        for refresh in [false, true] {
            let keys = self.keys(discovery, refresh).await?;
            let jwk = match kid {
                Some(kid) => keys.find(kid),
                None => keys.keys.first(),
            };
            if let Some(jwk) = jwk {
                return DecodingKey::from_jwk(jwk).map_err(failed);
            }
        }
        Err(failed(anyhow::anyhow!("no signing key {kid:?} in the provider's JWKS")))
    }

    async fn keys(&self, discovery: &Discovery, refresh: bool) -> AppResult<JwkSet> {
        if !refresh
            && let Some((at, keys)) = self.keys.read().await.as_ref()
            && at.elapsed() < METADATA_FOR
        {
            return Ok(keys.clone());
        }
        let keys: JwkSet =
            self.http.get(&discovery.jwks_uri).send().await.map_err(failed)?.json().await.map_err(failed)?;
        *self.keys.write().await = Some((Instant::now(), keys.clone()));
        Ok(keys)
    }

    async fn discovery(&self) -> AppResult<Discovery> {
        if let Some((at, discovery)) = self.discovery.read().await.as_ref()
            && at.elapsed() < METADATA_FOR
        {
            return Ok(discovery.clone());
        }
        let url = format!("{}/.well-known/openid-configuration", self.config.issuer.trim_end_matches('/'));
        let discovery: Discovery = self.http.get(&url).send().await.map_err(failed)?.json().await.map_err(failed)?;
        if discovery.issuer.trim_end_matches('/') != self.config.issuer.trim_end_matches('/') {
            return Err(failed(anyhow::anyhow!("discovery issuer {} differs from OIDC_ISSUER_URL", discovery.issuer)));
        }
        *self.discovery.write().await = Some((Instant::now(), discovery.clone()));
        Ok(discovery)
    }
}

fn random_token() -> String {
    URL_SAFE_NO_PAD.encode(rand::random::<[u8; 32]>())
}

fn failed(error: impl std::fmt::Display) -> AppError {
    tracing::warn!("Single sign-on failed: {error}");
    AppError::authentication("Single sign-on failed.")
}

fn username_from(wanted: Option<&str>) -> String {
    let cleaned: String = wanted
        .map(normalize_username)
        .unwrap_or_default()
        .chars()
        .filter(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || matches!(c, '.' | '_' | '-'))
        .skip_while(|c| !(c.is_ascii_lowercase() || c.is_ascii_digit()))
        .take(20)
        .collect();
    if valid_username(&cleaned) { cleaned } else { format!("user{}", &Uuid::now_v7().simple().to_string()[24..]) }
}

impl AuthService {
    pub async fn sso_sign_in(&self, identity: &Identity, client: &Client) -> AppResult<AuthResult> {
        let linked = sqlx::query!(
            "SELECT u.id, u.username, u.is_admin, u.is_active FROM user_identities i JOIN users u ON u.id = i.user_id
             WHERE i.issuer = $1 AND i.subject = $2",
            identity.issuer,
            identity.subject
        )
        .fetch_optional(&self.db)
        .await?;

        let user = match linked {
            Some(user) if !user.is_active => {
                let attempt = Attempt {
                    user: Some(user.id),
                    username: &user.username,
                    method: Method::Sso,
                    failure: Some("deactivated"),
                };
                journal::record(&self.db, attempt, client).await;
                return Err(AppError::forbidden("This account has been deactivated."));
            }
            Some(user) => UserDto { id: user.id, username: user.username, is_admin: user.is_admin },
            None if self.oidc.as_ref().is_some_and(|oidc| oidc.config.allow_signup) => {
                self.sso_sign_up(identity).await?
            }
            None => {
                let username = identity.username.as_deref().unwrap_or(&identity.subject);
                let attempt = Attempt { user: None, username, method: Method::Sso, failure: Some("not_linked") };
                journal::record(&self.db, attempt, client).await;
                return Err(AppError::forbidden("No account is linked to this sign-in."));
            }
        };

        journal::record(
            &self.db,
            Attempt { user: Some(user.id), username: &user.username, method: Method::Sso, failure: None },
            client,
        )
        .await;
        let mut tx = self.db.begin().await?;
        let result = self.issue(&mut tx, user, SessionStart::new(client)).await?;
        tx.commit().await?;
        Ok(result)
    }

    async fn sso_sign_up(&self, identity: &Identity) -> AppResult<UserDto> {
        let hash = password::hash(random_token()).await?;
        let mut username = username_from(identity.username.as_deref());
        let mut tx = self.db.begin().await?;
        for attempt in 0..5 {
            if attempt > 0 {
                let suffix = &Uuid::now_v7().simple().to_string()[28..];
                username = format!("{}{suffix}", username.chars().take(16).collect::<String>());
            }
            let taken =
                sqlx::query_scalar!(r#"SELECT EXISTS (SELECT 1 FROM users WHERE username = $1) AS "taken!""#, username)
                    .fetch_one(&mut *tx)
                    .await?;
            if !taken {
                break;
            }
        }
        let id = Uuid::now_v7();
        sqlx::query!(
            "INSERT INTO users (id, username, password_hash, is_admin, is_active, created_at) VALUES ($1, $2, $3, FALSE, TRUE, $4)",
            id,
            username,
            hash,
            Utc::now()
        )
        .execute(&mut *tx)
        .await?;
        sqlx::query!(
            "INSERT INTO user_identities (issuer, subject, user_id, created_at) VALUES ($1, $2, $3, $4)",
            identity.issuer,
            identity.subject,
            id,
            Utc::now()
        )
        .execute(&mut *tx)
        .await?;
        tx.commit().await?;
        tracing::info!("User {username} signed up through single sign-on");
        Ok(UserDto { id, username, is_admin: false })
    }

    pub async fn link_identity(&self, user: Uuid, identity: &Identity) -> AppResult<()> {
        let owner = sqlx::query_scalar!(
            "SELECT user_id FROM user_identities WHERE issuer = $1 AND subject = $2",
            identity.issuer,
            identity.subject
        )
        .fetch_optional(&self.db)
        .await?;
        match owner {
            Some(owner) if owner == user => Ok(()),
            Some(_) => Err(AppError::conflict("This sign-in is already linked to another account.")),
            None => {
                sqlx::query!(
                    "INSERT INTO user_identities (issuer, subject, user_id, created_at) VALUES ($1, $2, $3, $4)",
                    identity.issuer,
                    identity.subject,
                    user,
                    Utc::now()
                )
                .execute(&self.db)
                .await?;
                tracing::info!("User {user} linked a single sign-on identity");
                Ok(())
            }
        }
    }

    pub async fn identities(&self, user: Uuid) -> AppResult<Vec<LinkedIdentityDto>> {
        let Some(oidc) = self.oidc() else { return Ok(vec![]) };
        let rows = sqlx::query!(
            "SELECT created_at FROM user_identities WHERE user_id = $1 AND issuer = $2",
            user,
            oidc.config.issuer
        )
        .fetch_all(&self.db)
        .await?;
        Ok(rows
            .into_iter()
            .map(|row| LinkedIdentityDto { provider: oidc.name().to_owned(), linked_at: row.created_at })
            .collect())
    }

    pub async fn unlink_identities(&self, user: Uuid) -> AppResult<()> {
        let Some(oidc) = self.oidc() else { return Ok(()) };
        sqlx::query!("DELETE FROM user_identities WHERE user_id = $1 AND issuer = $2", user, oidc.config.issuer)
            .execute(&self.db)
            .await?;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn usernames_from_the_provider_are_made_valid() {
        assert_eq!(username_from(Some("Anna.Petrova")), "anna.petrova");
        assert_eq!(username_from(Some("__bob_smith")), "bob_smith");
        assert!(username_from(Some("Ян")).starts_with("user"));
        assert!(username_from(None).starts_with("user"));
        assert_eq!(username_from(Some("averyveryverylongusername2026")).len(), 20);
    }
}
