// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { z } from "zod";

export const limits = {
  playlistName: 200,
  playlistDescription: 1000,
  trackTitle: 400,
  artistName: 300,
  albumTitle: 300,
  genreName: 150,
  username: 100,
  displayName: 100,
  password: { min: 8, max: 72 },
  lyrics: 20_000,
} as const;

const trimmed = (max: number) => z.string().trim().max(max);
const required = (max: number) => trimmed(max).min(1);

const optionalNumber = z
  .string()
  .trim()
  .transform((value) => (value === "" ? null : Number.parseInt(value, 10)))
  .refine((value) => value === null || Number.isFinite(value), { message: "—" });

export const playlistSchema = z.object({
  name: required(limits.playlistName),
  description: trimmed(limits.playlistDescription),
  isPublic: z.boolean(),
});

export const lyricsSchema = z.object({
  text: z.string().max(limits.lyrics),
});

export type LyricsValues = z.infer<typeof lyricsSchema>;

export const trackSchema = z.object({
  title: required(limits.trackTitle),
  artist: trimmed(limits.artistName),
  album: trimmed(limits.albumTitle),
  genre: trimmed(limits.genreName),
  year: optionalNumber,
  trackNumber: optionalNumber,
  discNumber: optionalNumber,
});

export const artistSchema = z.object({
  name: required(limits.artistName),
});

export const albumSchema = z.object({
  title: required(limits.albumTitle),
  artist: required(limits.artistName),
  year: optionalNumber,
});

export const newUserSchema = z.object({
  username: required(limits.username),
  displayName: trimmed(limits.displayName),
  password: z.string().min(limits.password.min).max(limits.password.max),
  isAdmin: z.boolean(),
});

export const signInSchema = z.object({
  username: z.string().trim().min(1),
  password: z.string().min(1),
});

/** Сброс пароля админом: своего текущего пароля он не знает, поэтому только новый и повтор. */
export const passwordResetSchema = z
  .object({
    next: z.string().min(limits.password.min).max(limits.password.max),
    repeat: z.string(),
  })
  .refine((values) => values.next === values.repeat, {
    path: ["repeat"],
    message: "mismatch",
  });

export const passwordChangeSchema = z
  .object({
    current: z.string().min(1),
    next: z.string().min(limits.password.min).max(limits.password.max),
    repeat: z.string(),
  })
  .refine((values) => values.next === values.repeat, {
    path: ["repeat"],
    message: "mismatch",
  });

export type PlaylistValues = z.infer<typeof playlistSchema>;

export type TrackInput = z.input<typeof trackSchema>;
export type TrackValues = z.output<typeof trackSchema>;
export type ArtistValues = z.infer<typeof artistSchema>;
export type AlbumInput = z.input<typeof albumSchema>;
export type AlbumValues = z.output<typeof albumSchema>;
export type NewUserValues = z.infer<typeof newUserSchema>;
export type SignInValues = z.infer<typeof signInSchema>;
export type PasswordChangeValues = z.infer<typeof passwordChangeSchema>;
export type PasswordResetValues = z.infer<typeof passwordResetSchema>;
