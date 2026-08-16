import { z } from "zod";

/**
 * Правила полей рядом с запросами, а не в атрибутах разметки. Прежде maxLength стоял на
 * <input>, и о том, что сервер откажет на 201-м символе, форма узнавала только от сервера.
 */
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
} as const;

const trimmed = (max: number) => z.string().trim().max(max);
const required = (max: number) => trimmed(max).min(1);

/** Пустая строка в числовом поле — это «не задано», а не ноль и не ошибка. */
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

export const passwordChangeSchema = z
  .object({
    current: z.string().min(1),
    next: z.string().min(limits.password.min).max(limits.password.max),
    repeat: z.string(),
  })
  .refine((values) => values.next === values.repeat, {
    // Ошибка вешается на поле повтора: там её и ждут увидеть.
    path: ["repeat"],
    message: "mismatch",
  });

export type PlaylistValues = z.infer<typeof playlistSchema>;

/*
 * У формы трека вход и выход разные: числовые поля приходят из <input> строками, а уезжают
 * на сервер числами или null. Оба типа нужны явно, иначе react-hook-form примет за истину
 * выходной и начнёт требовать числа от полей ввода.
 */
export type TrackInput = z.input<typeof trackSchema>;
export type TrackValues = z.output<typeof trackSchema>;
export type ArtistValues = z.infer<typeof artistSchema>;
export type NewUserValues = z.infer<typeof newUserSchema>;
export type SignInValues = z.infer<typeof signInSchema>;
export type PasswordChangeValues = z.infer<typeof passwordChangeSchema>;
