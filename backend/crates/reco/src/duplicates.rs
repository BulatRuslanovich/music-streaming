// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

use std::collections::{HashMap, HashSet};

use uuid::Uuid;

use crate::snapshot::{Snapshot, TrackMeta};

pub const SOUND_ALIKE: f32 = 0.92;

pub const SOUND_ALIKE_SAME_SONG: f32 = 0.88;

pub const SOUND_ALIKE_UNRELATED: f32 = 0.94;
const UNRELATED_DURATION_TOLERANCE_SECONDS: i32 = 1;

const DURATION_TOLERANCE_SECONDS: f64 = 3.0;
const DURATION_TOLERANCE_SHARE: f64 = 0.03;

#[derive(Debug, Clone, PartialEq)]
pub struct DuplicateGroup {
    pub tracks: Vec<Uuid>,

    pub similarity: f32,
}

pub fn find(snapshot: &Snapshot, dismissed: &HashSet<(Uuid, Uuid)>) -> Vec<DuplicateGroup> {
    let mut rows: Vec<usize> = (0..snapshot.len()).collect();
    rows.sort_by_key(|&row| (snapshot.meta(row).duration_seconds, row));

    let mut parent: HashMap<usize, usize> = HashMap::new();
    let mut weakest: HashMap<usize, f32> = HashMap::new();

    fn root(parent: &mut HashMap<usize, usize>, mut row: usize) -> usize {
        while let Some(&up) = parent.get(&row) {
            if up == row {
                break;
            }
            let grand = parent.get(&up).copied().unwrap_or(up);
            parent.insert(row, grand);
            row = grand;
        }
        row
    }

    for (i, &row_a) in rows.iter().enumerate() {
        let a = snapshot.meta(row_a);
        let tolerance = DURATION_TOLERANCE_SECONDS.max(f64::from(a.duration_seconds) * DURATION_TOLERANCE_SHARE);

        for &row_b in &rows[i + 1..] {
            let b = snapshot.meta(row_b);
            if f64::from(b.duration_seconds - a.duration_seconds) > tolerance {
                break;
            }
            if different_songs_of_one_artist(a, b) {
                continue;
            }
            let similarity = snapshot.between(row_a, row_b);
            let threshold = if !a.song_key.is_empty() && a.song_key == b.song_key {
                SOUND_ALIKE_SAME_SONG
            } else if related(a, b) {
                SOUND_ALIKE
            } else if b.duration_seconds - a.duration_seconds <= UNRELATED_DURATION_TOLERANCE_SECONDS {
                SOUND_ALIKE_UNRELATED
            } else {
                continue;
            };
            if similarity < threshold {
                continue;
            }
            if dismissed.contains(&(a.track_id, b.track_id)) || dismissed.contains(&(b.track_id, a.track_id)) {
                continue;
            }

            let (root_a, root_b) = (root(&mut parent, row_a), root(&mut parent, row_b));
            let floor = similarity
                .min(weakest.get(&root_a).copied().unwrap_or(1.0))
                .min(weakest.get(&root_b).copied().unwrap_or(1.0));
            parent.insert(root_a, root_a);
            parent.insert(root_b, root_a);
            weakest.insert(root_a, floor);
        }
    }

    let mut groups: HashMap<usize, Vec<usize>> = HashMap::new();
    let members: Vec<usize> = parent.keys().copied().collect();
    for row in members {
        let top = root(&mut parent, row);
        groups.entry(top).or_default().push(row);
    }

    let mut result: Vec<DuplicateGroup> = groups
        .into_iter()
        .filter(|(_, rows)| rows.len() > 1)
        .map(|(top, mut rows)| {
            rows.sort_unstable();
            DuplicateGroup {
                tracks: rows.iter().map(|&row| snapshot.meta(row).track_id).collect(),
                similarity: weakest.get(&top).copied().unwrap_or(1.0),
            }
        })
        .collect();
    result.sort_by(|a, b| b.similarity.total_cmp(&a.similarity).then(a.tracks.cmp(&b.tracks)));
    result
}

fn related(a: &TrackMeta, b: &TrackMeta) -> bool {
    a.artists.iter().any(|artist| b.is_by(*artist))
        || matches!((a.title_key(), b.title_key()), (Some(x), Some(y)) if x.contains(y) || y.contains(x))
}

fn different_songs_of_one_artist(a: &TrackMeta, b: &TrackMeta) -> bool {
    if !a.artists.iter().any(|artist| b.is_by(*artist)) {
        return false;
    }
    match (a.title_key(), b.title_key()) {
        (Some(first), Some(second)) => !first.contains(second) && !second.contains(first),
        _ => false,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::snapshot::tests::meta;

    fn build(rows: Vec<(TrackMeta, [f32; 2])>) -> Snapshot {
        Snapshot::new(2, rows.into_iter().map(|(meta, v)| (meta, v.to_vec())).collect())
    }

    fn with_duration(mut meta: TrackMeta, seconds: i32) -> TrackMeta {
        meta.duration_seconds = seconds;
        meta
    }

    #[test]
    fn the_same_recording_twice_is_a_group() {
        let snapshot = build(vec![
            (with_duration(meta(1, 1, "song"), 200), [1.0, 0.0]),
            (with_duration(meta(2, 2, "song (remastered)"), 201), [0.99, 0.05]),
            (with_duration(meta(4, 4, "unrelated"), 203), [1.0, 0.01]),
            (with_duration(meta(5, 5, "junk tags"), 200), [1.0, 0.0]),
            (with_duration(meta(3, 3, "far"), 400), [1.0, 0.0]),
        ]);
        let groups = find(&snapshot, &HashSet::new());
        assert_eq!(groups.len(), 1);

        assert_eq!(groups[0].tracks, [Uuid::from_u128(1), Uuid::from_u128(2), Uuid::from_u128(5)]);
    }

    #[test]
    fn songs_of_one_artist_and_dismissed_pairs_are_not_duplicates() {
        let snapshot = build(vec![
            (meta(1, 1, "first song"), [1.0, 0.0]),
            (meta(2, 1, "second song"), [1.0, 0.0]),
            (meta(3, 1, "first song (live)"), [1.0, 0.0]),
        ]);
        let groups = find(&snapshot, &HashSet::new());
        assert_eq!(groups[0].tracks, [Uuid::from_u128(1), Uuid::from_u128(3)]);

        let dismissed = HashSet::from([(Uuid::from_u128(3), Uuid::from_u128(1))]);
        assert!(find(&snapshot, &dismissed).is_empty());
    }
}
