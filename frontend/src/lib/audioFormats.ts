/**
 * Форматы, которые принимает библиотека, и вопрос «а сыграет ли это здесь».
 *
 * Список повторяет серверный (`AudioUpload` в слое приложения) — не потому, что клиент решает, а
 * потому, что отсеять неподходящий файл до отправки дешевле и понятнее, чем после.
 */

import type { AudioQuality, AudioQualityOption } from "@/lib/types";

export const ACCEPTED_EXTENSIONS = [".mp3", ".flac", ".m4a"] as const;

/**
 * Расширения перечислены первыми: незнакомый тип содержимого часть браузеров молча игнорирует, а
 * расширение honours каждый.
 */
export const ACCEPT_ATTRIBUTE = ".mp3,.flac,.m4a,audio/mpeg,audio/flac,audio/mp4";

export function extensionOf(fileName: string): string {
  return /\.[a-z0-9]+$/i.exec(fileName)?.[0].toLowerCase() ?? "";
}

export function isAcceptedAudio(fileName: string): boolean {
  return (ACCEPTED_EXTENSIONS as readonly string[]).includes(extensionOf(fileName));
}

/** Ступени от лучшей к худшей — на случай, если перекодирование настроено не полностью. */
const FALLBACK_TIERS = ["High", "Normal", "Low"] as const;

/**
 * Лучшая перекодированная ступень, которую эта установка вообще умеет отдать; <c>null</c> — ffmpeg
 * недоступен, и отступать от исходника некуда.
 */
export function bestFallbackTier(available: AudioQualityOption[]): AudioQuality | null {
  return FALLBACK_TIERS.find((tier) => available.some((option) => option.quality === tier)) ?? null;
}

/**
 * Ступень, которую стоит просить для трека с таким кодеком.
 *
 * <p>
 * Отличается от запрошенной только в одном случае: просят исходник, а браузер этого кодека не
 * знает. Тогда единственный способ услышать трек — перекодированная ступень, и качеством тут не
 * жертвуют, а получают хоть что-то вместо ошибки.
 * </p>
 */
export function playableTier(
  codec: string | null | undefined,
  wanted: AudioQuality,
  available: AudioQualityOption[],
): AudioQuality {
  if (wanted !== "Original" || canDecodeOriginal(codec)) return wanted;

  return bestFallbackTier(available) ?? wanted;
}

const MIME_FOR_CODEC: Record<string, string> = {
  mp3: "audio/mpeg",
  flac: "audio/flac",
  aac: 'audio/mp4; codecs="mp4a.40.2"',
  alac: 'audio/mp4; codecs="alac"',
};

const answers = new Map<string, boolean>();

/**
 * Возьмётся ли этот браузер играть исходник.
 *
 * <p>
 * Спрашивается у самого браузера, а не угадывается по его названию: `alac` в `audio/mp4` даёт
 * пустую строку в Chrome и Firefox и «maybe» в Safari — ровно та граница, которая нужна. Ответы
 * запоминаются: их всего четыре, а элемент под каждый вопрос создавать незачем.
 * </p>
 *
 * <p>
 * Неизвестный или отсутствующий кодек считается играющим. Так ведут себя треки, залитые до того,
 * как кодек стали записывать, и отнимать у них исходник из-за незнания было бы хуже, чем изредка
 * упереться в ошибку, которую разберёт откат.
 * </p>
 */
export function canDecodeOriginal(codec: string | null | undefined): boolean {
  if (!codec) return true;

  const mime = MIME_FOR_CODEC[codec];
  if (!mime || typeof document === "undefined") return true;

  const remembered = answers.get(codec);
  if (remembered !== undefined) return remembered;

  const answer = document.createElement("audio").canPlayType(mime) !== "";
  answers.set(codec, answer);

  return answer;
}
