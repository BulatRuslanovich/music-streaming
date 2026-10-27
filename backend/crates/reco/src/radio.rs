// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

use std::collections::{HashMap, HashSet};

use uuid::Uuid;

use crate::moods::MoodScores;
use crate::random::{self, Rng};
use crate::snapshot::Snapshot;
use crate::taste::{Listener, Taste};

const ANCHOR_CANDIDATES: usize = 20;
const ANCHOR_TEMPERATURE: f64 = 0.08;
const ANCHOR_RECENCY_PENALTY: f64 = 0.35;

const STUCK_WINDOW: usize = 5;
const STUCK_REPEATS: usize = 3;
const RECENT_ARTIST_WINDOW: usize = 10;
const RECENT_ARTIST_PENALTY: f64 = 0.5;
const EXPLORE_COLD: f64 = 0.35;
const EXPLORE_WARM: f64 = 0.15;

const WARM_TRACKS: usize = 10;

pub struct RadioRequest<'a> {
    pub seed: Option<Uuid>,

    pub queue: &'a [Uuid],

    pub recent: &'a [Uuid],
    pub limit: usize,
    pub mood: Option<&'a MoodScores>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct RadioPick {
    pub row: usize,
    pub explore: bool,
}

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct RadioBatch {
    pub anchor: Option<usize>,

    pub moved: bool,
    pub picks: Vec<RadioPick>,
}

pub fn next(snapshot: &Snapshot, listener: &Listener, taste: &Taste, request: &RadioRequest<'_>) -> RadioBatch {
    if snapshot.is_empty() || request.mood.is_some_and(|mood| !mood.playable()) {
        return RadioBatch::default();
    }
    let mut rng =
        Rng::new(random::seed(listener.user, "radio", listener.now) ^ random::mix(request.queue.len() as u64));

    let mut excluded: HashSet<usize> = HashSet::new();
    let rejected = listener.tracks.iter().filter(|(_, signal)| signal.rejected()).map(|(id, _)| *id);
    for track in request.queue.iter().chain(request.recent).copied().chain(rejected) {
        if let Some(row) = snapshot.row_of(track) {
            excluded.extend(snapshot.clones(row));
        }
    }

    let tail: Vec<usize> =
        request.queue.iter().filter(|&&id| Some(id) != request.seed).filter_map(|&id| snapshot.row_of(id)).collect();
    let tail = &tail[tail.len().saturating_sub(RECENT_ARTIST_WINDOW)..];

    let (anchor, moved) = match (request.mood, request.seed.and_then(|seed| snapshot.row_of(seed))) {
        (Some(mood), _) => (mood_anchor(snapshot, mood, taste, &excluded, &mut rng), false),
        (None, Some(seeded)) => match stuck(snapshot, seeded, tail) {
            Some(seed_artists) if !taste.is_empty() => {
                let other = other_mode(taste, snapshot.vector(seeded), &mut rng);
                let skip = |row: usize| snapshot.meta(row).artists.iter().any(|a| seed_artists.contains(a));
                match taste_anchor(snapshot, &taste.modes[other].centre, &excluded, &mut rng, skip) {
                    Some(fresh) => (Some(fresh), true),
                    None => (Some(seeded), false),
                }
            }
            _ => (Some(seeded), false),
        },
        (None, None) if !taste.is_empty() => {
            let mode = pick_mode(taste, &mut rng);
            (taste_anchor(snapshot, &taste.modes[mode].centre, &excluded, &mut rng, |_| false), false)
        }
        (None, None) => {
            let open: Vec<usize> = (0..snapshot.len()).filter(|row| !excluded.contains(row)).collect();
            let pool = if open.is_empty() { (0..snapshot.len()).collect() } else { open };
            (Some(pool[rng.below(pool.len())]), false)
        }
    };
    let Some(anchor) = anchor else { return RadioBatch::default() };
    excluded.extend(snapshot.clones(anchor));

    let anchor_similarity = snapshot.similarities(snapshot.vector(anchor));
    let mut recent_artists: HashSet<Uuid> = tail.iter().flat_map(|&row| snapshot.meta(row).artists.clone()).collect();
    if moved && let Some(seeded) = request.seed.and_then(|seed| snapshot.row_of(seed)) {
        recent_artists.extend(snapshot.meta(seeded).artists.iter().copied());
    }

    let mut ranked: Vec<(usize, f64)> = (0..snapshot.len())
        .filter(|row| !excluded.contains(row))
        .filter(|&row| request.mood.is_none_or(|mood| mood.members[row]))
        .map(|row| {
            let similarity = f64::from(anchor_similarity[row]);
            let fit = f64::from(taste.fit.get(row).copied().unwrap_or(0.0));
            let mut score = match request.mood {
                Some(mood) => 0.5 * similarity + 0.3 * f64::from(mood.ranks[row]) + 0.2 * fit,
                None if taste.is_empty() => similarity,
                None => 0.7 * similarity + 0.3 * fit,
            };
            if snapshot.meta(row).artists.iter().any(|artist| recent_artists.contains(artist)) {
                score *= RECENT_ARTIST_PENALTY;
            }
            (row, score)
        })
        .collect();
    ranked.sort_by(|a, b| b.1.total_cmp(&a.1).then(a.0.cmp(&b.0)));

    let positive = listener.tracks.values().filter(|signal| signal.score > 0.0).count();
    let explore_ratio = if positive < WARM_TRACKS { EXPLORE_COLD } else { EXPLORE_WARM };
    let size = request.limit.saturating_sub(usize::from(moved));
    #[expect(
        clippy::cast_possible_truncation,
        clippy::cast_sign_loss,
        clippy::cast_precision_loss,
        reason = "a small share"
    )]
    let explore_slots = (size as f64 * explore_ratio).floor() as usize;
    let per_artist = if request.limit <= 6 { 1 } else { 2 };

    let mut picks = Vec::with_capacity(request.limit);
    if moved {
        picks.push(RadioPick { row: anchor, explore: false });
    }
    let mut taken: HashSet<usize> = HashSet::new();
    let mut artists: HashMap<Uuid, usize> = HashMap::new();
    let mut accept = |row: usize, explore: bool, picks: &mut Vec<RadioPick>| -> bool {
        let artist = snapshot.meta(row).artist();
        if taken.contains(&row) || artists.get(&artist).copied().unwrap_or(0) >= per_artist {
            return false;
        }
        taken.extend(snapshot.clones(row));
        *artists.entry(artist).or_default() += 1;
        picks.push(RadioPick { row, explore });
        true
    };

    for &(row, _) in &ranked {
        if picks.len() >= request.limit - explore_slots {
            break;
        }
        accept(row, false, &mut picks);
    }
    let further: Vec<usize> = ranked.iter().skip(request.limit * 2).take(80).map(|&(row, _)| row).collect();
    let mut attempts = 0;
    while picks.len() < request.limit && !further.is_empty() && attempts < 200 {
        accept(further[rng.below(further.len())], true, &mut picks);
        attempts += 1;
    }

    for &(row, _) in &ranked {
        if picks.len() >= request.limit {
            break;
        }
        accept(row, false, &mut picks);
    }

    RadioBatch { anchor: Some(anchor), moved, picks }
}

fn mood_anchor(
    snapshot: &Snapshot,
    mood: &MoodScores,
    taste: &Taste,
    excluded: &HashSet<usize>,
    rng: &mut Rng,
) -> Option<usize> {
    let mut candidates: Vec<(usize, f32)> = (0..snapshot.len())
        .filter(|&row| mood.members[row] && !excluded.contains(&row))
        .map(|row| (row, mood.ranks[row] + taste.fit.get(row).copied().unwrap_or(0.0)))
        .collect();
    candidates.sort_by(|a, b| b.1.total_cmp(&a.1).then(a.0.cmp(&b.0)));
    candidates.truncate(ANCHOR_CANDIDATES);
    (!candidates.is_empty()).then(|| candidates[rng.below(candidates.len())].0)
}

fn taste_anchor(
    snapshot: &Snapshot,
    centre: &[f32],
    excluded: &HashSet<usize>,
    rng: &mut Rng,
    skip: impl Fn(usize) -> bool,
) -> Option<usize> {
    let nearest = snapshot.top_k(centre, ANCHOR_CANDIDATES, skip);
    let all_excluded = nearest.iter().all(|hit| excluded.contains(&hit.row));
    let scores: Vec<f64> = nearest
        .iter()
        .map(|hit| {
            let penalty = if !all_excluded && excluded.contains(&hit.row) { ANCHOR_RECENCY_PENALTY } else { 0.0 };
            f64::from(hit.score) - penalty
        })
        .collect();
    let best = scores.iter().copied().fold(f64::MIN, f64::max);
    let weights: Vec<f64> = scores.iter().map(|score| ((score - best) / ANCHOR_TEMPERATURE).exp()).collect();
    let mut threshold = rng.next_f64() * weights.iter().sum::<f64>();
    for (hit, weight) in nearest.iter().zip(&weights) {
        threshold -= weight;
        if threshold <= 0.0 {
            return Some(hit.row);
        }
    }
    nearest.first().map(|hit| hit.row)
}

fn stuck(snapshot: &Snapshot, seed: usize, tail: &[usize]) -> Option<Vec<Uuid>> {
    let artists = &snapshot.meta(seed).artists;
    let window = &tail[tail.len().saturating_sub(STUCK_WINDOW)..];
    let repeats = window.iter().filter(|&&row| snapshot.meta(row).artists.iter().any(|a| artists.contains(a))).count();
    (repeats >= STUCK_REPEATS).then(|| artists.clone())
}

fn other_mode(taste: &Taste, seed: &[f32], rng: &mut Rng) -> usize {
    let nearest = taste.nearest_mode(seed).unwrap_or(0);
    let others: Vec<usize> = (0..taste.modes.len()).filter(|&index| index != nearest).collect();
    if others.is_empty() {
        return nearest;
    }
    let total: f64 = others.iter().map(|&index| taste.modes[index].share).sum();
    let mut roll = rng.next_f64() * total;
    for &index in &others {
        roll -= taste.modes[index].share;
        if roll < 0.0 {
            return index;
        }
    }
    others[others.len() - 1]
}

fn pick_mode(taste: &Taste, rng: &mut Rng) -> usize {
    let mut roll = rng.next_f64();
    for (index, mode) in taste.modes.iter().enumerate() {
        roll -= mode.share;
        if roll < 0.0 {
            return index;
        }
    }
    0
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::snapshot::tests::meta;
    use crate::taste::{TrackSignal, taste};

    fn library() -> Snapshot {
        let mut rng = Rng::new(9);
        let rows = (0..60u32)
            .map(|i| {
                #[expect(clippy::cast_possible_truncation, reason = "test noise")]
                let v: Vec<f32> =
                    (0..6).map(|d| if d == i as usize % 3 { 1.0 } else { rng.next_f64() as f32 * 0.4 }).collect();
                (meta(u128::from(i), u128::from(i % 12), &format!("t{i}")), v)
            })
            .collect();
        Snapshot::new(6, rows)
    }

    fn listener(signals: &[(u128, f64)]) -> Listener {
        Listener {
            user: Uuid::from_u128(5),
            now: 1_000,
            day: 1,
            tracks: signals
                .iter()
                .map(|&(id, score)| (Uuid::from_u128(id), TrackSignal { score, plays: 1, ..TrackSignal::default() }))
                .collect(),
        }
    }

    #[test]
    fn a_seeded_radio_follows_the_seed_and_skips_the_queue() {
        let snapshot = library();
        let listener = listener(&[]);
        let taste = taste(&snapshot, &listener);
        let queue = [Uuid::from_u128(3)];
        let request =
            RadioRequest { seed: Some(Uuid::from_u128(0)), queue: &queue, recent: &[], limit: 10, mood: None };
        let batch = next(&snapshot, &listener, &taste, &request);

        assert_eq!(batch.anchor, Some(0));
        assert_eq!(batch.picks.len(), 10);
        let rows: HashSet<usize> = batch.picks.iter().map(|p| p.row).collect();
        assert!(!rows.contains(&0) && !rows.contains(&3));
        let close = batch.picks.iter().filter(|p| !p.explore && p.row % 3 == 0).count();
        assert!(close >= 5, "{batch:?}");
    }

    #[test]
    fn without_a_seed_the_radio_starts_from_taste_or_anywhere() {
        let snapshot = library();
        for listener in [listener(&[]), listener(&[(1, 2.0), (4, 1.0)])] {
            let taste = taste(&snapshot, &listener);
            let request = RadioRequest { seed: None, queue: &[], recent: &[], limit: 3, mood: None };
            let batch = next(&snapshot, &listener, &taste, &request);
            assert!(batch.anchor.is_some());
            assert_eq!(batch.picks.len(), 3);
        }
    }

    #[test]
    fn a_stuck_radio_moves_to_another_artist() {
        let snapshot = library();
        let liked: Vec<(u128, f64)> = (0..30).map(|i| (i, 1.0)).collect();
        let listener = listener(&liked);
        let taste = taste(&snapshot, &listener);

        let queue = [Uuid::from_u128(12), Uuid::from_u128(24), Uuid::from_u128(36), Uuid::from_u128(0)];
        let request = RadioRequest { seed: Some(Uuid::from_u128(0)), queue: &queue, recent: &[], limit: 5, mood: None };
        let batch = next(&snapshot, &listener, &taste, &request);
        assert!(batch.moved);
        let anchor = batch.anchor.unwrap_or_default();
        assert_ne!(snapshot.meta(anchor).artist(), Uuid::from_u128(0));
        assert_eq!(batch.picks[0].row, anchor);
    }
}
