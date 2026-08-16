"use client";

import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { api, type UploadProgress } from "@/lib/api";
import { ACCEPTED_EXTENSIONS, isAcceptedAudio } from "@/lib/audioFormats";
import { checkAgainstLibrary, fileKey, type FileVerdict } from "@/lib/uploadCheck";
import { useFormat } from "@/lib/useFormat";
import { useInvalidate } from "@/lib/useInvalidate";
import type { Track, UploadResult } from "@/lib/types";
import { useSettings } from "./SettingsContext";
import { useT } from "./I18nContext";
import { useToast } from "./ToastContext";

export type UploadFailure = UploadResult["failed"][number];

interface UploadState {
  /** Выбранное и ещё не отправленное, в порядке выбора. */
  queue: File[];

  /** Что библиотека думает о каждом файле очереди; ключ — `fileKey(file)`. */
  verdicts: Record<string, FileVerdict>;

  /** Из очереди — то, что действительно поедет: без уже имеющегося в библиотеке. */
  pending: File[];

  /** Из очереди — то, что пропустится как дубликат. */
  duplicates: File[];

  /** Отправка прямо сейчас, или null, если ничего не идёт. */
  progress: UploadProgress | null;

  /** Сколько сверок с библиотекой ещё не ответили: пока их больше нуля, отправлять рано. */
  checking: number;

  /** Доехавшее, от свежего к старому. */
  uploaded: Track[];

  /** Не доехавшее и причина по каждому. */
  failed: UploadFailure[];

  add: (files: FileList | File[] | null) => void;
  remove: (index: number) => void;
  clearQueue: () => void;
  start: () => void;
  clearUploaded: () => void;
  clearFailed: () => void;
}

const UploadContext = createContext<UploadState | null>(null);

const RESULTS_STORAGE_KEY = "music-streaming.upload-results";

/**
 * Загрузка живёт над страницей, а не на ней.
 *
 * Отправка идёт через XMLHttpRequest, который не привязан к жизни компонента: уйдя со страницы,
 * человек не отменяет её — файлы продолжают ехать и доезжают. Пока всё это лежало в состоянии
 * самой страницы, размонтирование выбрасывало его целиком, и возвращаться было некуда: треки в
 * библиотеке появлялись, а список «только что добавлено» встречал пустотой. Здесь то же состояние
 * переживает любую навигацию, а итог по каждому файлу вдобавок откладывается в sessionStorage —
 * чтобы и перезагрузка вкладки не стёрла память о том, что уже загрузилось.
 *
 * Сами файлы сохранить нельзя: `File` не переживает перезагрузку, а класть содержимое в IndexedDB
 * значит завести вторую копию фонотеки на диске ради редкой случайности. Поэтому очередь живёт
 * только в памяти, а от перезагрузки посреди отправки защищает вопрос браузера.
 */
export function UploadProvider({ children }: { children: React.ReactNode }) {
  const { maxUploadBytes } = useSettings();
  const { notify, notifyError } = useToast();
  const invalidate = useInvalidate();
  const format = useFormat();
  const t = useT();

  const [queue, setQueue] = useState<File[]>([]);
  const [verdicts, setVerdicts] = useState<Record<string, FileVerdict>>({});
  const [progress, setProgress] = useState<UploadProgress | null>(null);
  const [checking, setChecking] = useState(0);
  const [uploaded, setUploaded] = useState<Track[]>([]);
  const [failed, setFailed] = useState<UploadFailure[]>([]);
  const [restored, setRestored] = useState(false);

  // Обработчики зовутся из событий и замыкают состояние на момент своего создания. Ссылка на
  // свежее избавляет от гонки «добавили, пока проверялось предыдущее» без пересоздания коллбэков.
  const latest = useRef({ queue, verdicts });
  useEffect(() => {
    latest.current = { queue, verdicts };
  });

  const running = useRef(false);

  useEffect(() => {
    const saved = readResults();

    /* eslint-disable react-hooks/set-state-in-effect */
    if (saved.uploaded.length > 0) setUploaded(saved.uploaded);
    if (saved.failed.length > 0) setFailed(saved.failed);
    setRestored(true);
    /* eslint-enable react-hooks/set-state-in-effect */
  }, []);

  useEffect(() => {
    // Только после восстановления: иначе первая же запись затёрла бы сохранённое пустотой.
    if (!restored) return;

    writeResults({ uploaded, failed });
  }, [restored, uploaded, failed]);

  const uploading = progress !== null;

  useEffect(() => {
    if (!uploading) return;

    // Уйти по ссылке загрузка переживает, а перезагрузку и закрытие вкладки — нет: браузер обрывает
    // запрос на полуслове. Текст задаёт браузер, от страницы требуется лишь возражение.
    const warn = (event: BeforeUnloadEvent) => event.preventDefault();

    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [uploading]);

  const check = useCallback(async (files: File[]) => {
    if (files.length === 0) return;

    setChecking((count) => count + 1);
    try {
      const checked = await checkAgainstLibrary(files);
      setVerdicts((current) => ({ ...current, ...checked }));
    } catch {
    } finally {
      setChecking((count) => count - 1);
    }
  }, []);

  const add = useCallback(
    (incoming: FileList | File[] | null) => {
      if (!incoming) return;

      const accepted: File[] = [];
      const rejected: UploadFailure[] = [];

      for (const file of Array.from(incoming)) {
        if (!isAcceptedAudio(file.name)) {
          rejected.push({
            fileName: file.name,
            reason: t("upload.unsupportedFormat", { formats: ACCEPTED_EXTENSIONS.join(", ") }),
          });
        } else if (file.size > maxUploadBytes) {
          rejected.push({
            fileName: file.name,
            reason: t("upload.tooLarge", { limit: format.bytes(maxUploadBytes) }),
          });
        } else {
          accepted.push(file);
        }
      }

      const queued = new Set(latest.current.queue.map(fileKey));
      const added = accepted.filter((file) => !queued.has(fileKey(file)));

      if (added.length > 0) {
        setQueue((current) => [...current, ...added]);
        void check(added.filter((file) => latest.current.verdicts[fileKey(file)] === undefined));
      }

      if (rejected.length > 0) {
        setFailed((current) => [...current, ...rejected]);
        notify(
          rejected.length === 1
            ? `${rejected[0].fileName}: ${rejected[0].reason}`
            : t("upload.rejected", { count: rejected.length }),
          "error",
        );
      }
    },
    [maxUploadBytes, check, notify, t, format],
  );

  const remove = useCallback((index: number) => {
    setQueue((current) => current.filter((_, at) => at !== index));
  }, []);

  const clearQueue = useCallback(() => setQueue([]), []);

  const clearUploaded = useCallback(() => setUploaded([]), []);

  const clearFailed = useCallback(() => setFailed([]), []);

  const start = useCallback(() => {
    if (running.current) return;

    const { queue: current, verdicts: known } = latest.current;

    const isDuplicate = (file: File) => known[fileKey(file)]?.verdict === "Duplicate";
    const pending = current.filter((file) => !isDuplicate(file));

    if (pending.length === 0) return;

    const skipped = current.filter(isDuplicate).map((file) => ({
      fileName: file.name,
      reason: t("upload.alreadyInLibrary"),
    }));

    // Ровно то, что забрала эта отправка. Очередь во время неё не заперта, и подложенное по ходу
    // должно её пережить: сброс списка целиком унёс бы с собой файлы, которых никто не отправлял.
    const taken = new Set(current.map(fileKey));

    running.current = true;

    setProgress({ percent: 0, fileIndex: 0, fileCount: pending.length, fileName: pending[0].name });
    setFailed(skipped);

    void (async () => {
      try {
        // Итог каждого файла кладётся сразу, а общий ответ нужен уже только ради счётчиков в
        // уведомлении: всё, что в нём есть, к этому моменту разложено по спискам.
        const result = await api.upload(pending, setProgress, (one) => {
          if (one.uploaded.length > 0) setUploaded((shown) => [...one.uploaded, ...shown]);
          if (one.failed.length > 0) setFailed((shown) => [...shown, ...one.failed]);
        });

        setQueue((later) => later.filter((file) => !taken.has(fileKey(file))));
        setVerdicts((later) =>
          Object.fromEntries(Object.entries(later).filter(([key]) => !taken.has(key))),
        );

        if (result.uploaded.length > 0) {
          notify(t("upload.added", { count: result.uploaded.length }), "success");
          invalidate("library");
        }

        if (result.failed.length > 0) {
          notify(t("upload.rejected", { count: result.failed.length }), "error");
        }
      } catch (reason) {
        // Очередь намеренно остаётся: сессия кончилась, и после входа то же самое можно отправить
        // снова, не выбирая файлы заново.
        notifyError(reason, t("upload.failed"));
      } finally {
        running.current = false;
        setProgress(null);
      }
    })();
  }, [invalidate, notify, notifyError, t]);

  const value = useMemo<UploadState>(() => {
    const isDuplicate = (file: File) => verdicts[fileKey(file)]?.verdict === "Duplicate";

    return {
      queue,
      verdicts,
      pending: queue.filter((file) => !isDuplicate(file)),
      duplicates: queue.filter(isDuplicate),
      progress,
      checking,
      uploaded,
      failed,
      add,
      remove,
      clearQueue,
      start,
      clearUploaded,
      clearFailed,
    };
  }, [
    queue,
    verdicts,
    progress,
    checking,
    uploaded,
    failed,
    add,
    remove,
    clearQueue,
    start,
    clearUploaded,
    clearFailed,
  ]);

  return <UploadContext.Provider value={value}>{children}</UploadContext.Provider>;
}

export function useUpload(): UploadState {
  const context = useContext(UploadContext);
  if (!context) throw new Error("useUpload must be used inside <UploadProvider>");
  return context;
}

interface PersistedResults {
  uploaded: Track[];
  failed: UploadFailure[];
}

const NOTHING: PersistedResults = { uploaded: [], failed: [] };

/**
 * Итоги — в sessionStorage, а не в localStorage: «только что добавлено» описывает текущий заход,
 * и список недельной давности, встречающий на пустой странице, был бы не памятью, а мусором.
 */
function readResults(): PersistedResults {
  try {
    const raw = window.sessionStorage.getItem(RESULTS_STORAGE_KEY);
    if (!raw) return NOTHING;

    const parsed = JSON.parse(raw) as Partial<PersistedResults>;

    return {
      uploaded: Array.isArray(parsed.uploaded) ? parsed.uploaded : [],
      failed: Array.isArray(parsed.failed) ? parsed.failed : [],
    };
  } catch {
    window.sessionStorage.removeItem(RESULTS_STORAGE_KEY);
    return NOTHING;
  }
}

function writeResults(results: PersistedResults) {
  try {
    if (results.uploaded.length === 0 && results.failed.length === 0) {
      window.sessionStorage.removeItem(RESULTS_STORAGE_KEY);
      return;
    }

    window.sessionStorage.setItem(RESULTS_STORAGE_KEY, JSON.stringify(results));
  } catch {}
}
