// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import type {
  ComponentPropsWithoutRef,
  Dispatch,
  RefObject,
  SetStateAction,
  SyntheticEvent,
} from "react";
import { api } from "@/lib/api";
import { AdaptivePlayback, warmUpHls } from "@/lib/playback/adaptivePlayback";
import { Crossfade, crossfadeSeconds, crossfadeStage } from "@/lib/playback/crossfade";
import { refreshSession } from "@/lib/http";
import { mediaUrl } from "@/lib/media";
import {
  createListeningTracker,
  historyThresholdFor,
  type ListeningTracker,
} from "@/lib/playback/playbackTelemetry";
import type { RepeatMode } from "@/lib/playback/playerTypes";
import { PlaybackRecovery } from "@/lib/playback/playbackRecovery";
import { registerStreamWorker } from "@/lib/serviceWorker";
import type { AudioQuality, Track } from "@/lib/types";
import { useInvalidate } from "@/lib/useInvalidate";
import { useSettings } from "@/lib/useSettings";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/lib/useToast";

interface PlaybackEngineInput {
  currentTrack: Track | null;
  nextTrack: Track | null;
  crossfade: number;
  repeat: RepeatMode;
  isPlaying: boolean;
  setIsPlaying: Dispatch<SetStateAction<boolean>>;
  volume: number;
  muted: boolean;
  onTrackEnded: () => void;
}

interface PlaybackEngine {
  audioRefs: [RefObject<HTMLAudioElement | null>, RefObject<HTMLAudioElement | null>];
  audioProps: ComponentPropsWithoutRef<"audio">;

  position: number;
  duration: number;
  buffered: number;

  getPosition: () => number;
  getDuration: () => number;
  trackedPosition: () => number;

  seek: (seconds: number) => void;
  seekBy: (deltaSeconds: number) => void;
  scrubBy: (deltaSeconds: number) => void;
  commitScrub: () => void;

  recoverSource: () => boolean;

  startQueue: () => void;
  resetProgress: () => void;
  resumeAt: (trackId: string | undefined, seconds: number) => void;
}

interface Standby {
  trackId: string;
  sourceKey: string;
  playback: AdaptivePlayback;
  ready: boolean;
}

let scriptableVolume: boolean | null = null;

function sourceKeyOf(
  trackId: string,
  quality: AudioQuality,
  forceAdaptive: boolean,
  revision: number,
): string {
  return `${trackId}:${quality}:${forceAdaptive ? "adaptive" : "direct"}:${revision}`;
}

function silence(audio: HTMLAudioElement, playback: AdaptivePlayback | null): void {
  playback?.destroy();
  audio.pause();
  audio.removeAttribute("src");
  audio.load();

  delete audio.dataset.trackId;
  delete audio.dataset.sourceKey;
  delete audio.dataset.sourceLoading;
  delete audio.dataset.playbackMode;
}

export function usePlaybackEngine({
  currentTrack,
  nextTrack,
  crossfade,
  repeat,
  isPlaying,
  setIsPlaying,
  volume,
  muted,
  onTrackEnded,
}: PlaybackEngineInput): PlaybackEngine {
  const { notify } = useToast();
  const t = useT();
  const settings = useSettings();
  const invalidate = useInvalidate();

  const primaryRef = useRef<HTMLAudioElement | null>(null);
  const secondaryRef = useRef<HTMLAudioElement | null>(null);
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const adaptiveRef = useRef<AdaptivePlayback | null>(null);

  const standbyRef = useRef<Standby | null>(null);
  const fadePendingRef = useRef(0);
  const [crossfader] = useState(() => new Crossfade());

  useEffect(() => {
    audioRef.current ??= primaryRef.current;
  }, []);

  const spareAudio = useCallback(
    () => (audioRef.current === primaryRef.current ? secondaryRef.current : primaryRef.current),
    [],
  );

  const dropStandby = useCallback(() => {
    const standby = standbyRef.current;
    if (!standby) return;

    standbyRef.current = null;
    const audio = spareAudio();
    if (audio) silence(audio, standby.playback);
  }, [spareAudio]);

  const [position, setPosition] = useState(0);
  const [duration, setDuration] = useState(0);
  const [buffered, setBuffered] = useState(0);
  const [sourceRevision, setSourceRevision] = useState(0);

  const recordedRef = useRef<string | null>(null);

  const trackerRef = useRef<ListeningTracker | null>(null);
  const tracker = (trackerRef.current ??= createListeningTracker());

  const pendingSeekRef = useRef<number | null>(null);
  const positionRef = useRef(0);
  const retryTimerRef = useRef<number | null>(null);

  const [recovery] = useState(() => new PlaybackRecovery());

  const failSource = useCallback(
    (resume: boolean): boolean => {
      const first = recovery.fail(audioRef.current?.dataset.trackId, resume);
      setIsPlaying(false);

      return first;
    },
    [recovery, setIsPlaying],
  );

  const recoverSource = useCallback((): boolean => {
    const resumed = recovery.recover();
    if (!resumed) return false;

    setSourceRevision((revision) => revision + 1);

    return resumed.resume;
  }, [recovery]);

  useEffect(() => {
    registerStreamWorker();

    const warm = () => warmUpHls();
    const idle = window.requestIdleCallback?.(warm, { timeout: 10_000 }) ?? null;
    window.addEventListener("pointerdown", warm, { once: true, passive: true });

    const wentOnline = () => {
      if (recoverSource()) setIsPlaying(true);
    };
    window.addEventListener("online", wentOnline);

    return () => {
      if (idle !== null) window.cancelIdleCallback?.(idle);
      window.removeEventListener("pointerdown", warm);
      window.removeEventListener("online", wentOnline);
    };
  }, [recoverSource, setIsPlaying]);

  const seek = useCallback(
    (seconds: number) => {
      const audio = audioRef.current;
      if (!audio) return;

      crossfader.finish();

      const clamped = Math.max(0, Math.min(seconds, audio.duration || seconds));
      audio.currentTime = clamped;
      setPosition(clamped);
      positionRef.current = clamped;
    },
    [crossfader],
  );

  const seekBy = useCallback(
    (deltaSeconds: number) => {
      const audio = audioRef.current;
      if (!audio) return;

      seek(audio.currentTime + deltaSeconds);
    },
    [seek],
  );

  const scrubRef = useRef<number | null>(null);

  const scrubBy = useCallback(
    (deltaSeconds: number) => {
      const audio = audioRef.current;
      if (!audio) return;

      crossfader.finish();

      const from = scrubRef.current ?? audio.currentTime;
      const target = Math.max(0, Math.min(from + deltaSeconds, audio.duration || from));
      scrubRef.current = target;
      audio.muted = true;
      setPosition(target);
      positionRef.current = target;
    },
    [crossfader],
  );

  const commitScrub = useCallback(() => {
    const target = scrubRef.current;
    if (target === null) return;

    scrubRef.current = null;
    if (audioRef.current) audioRef.current.muted = muted;
    seek(target);
  }, [seek, muted]);

  const startQueue = useCallback(() => {
    tracker.finish("trackSkipped");

    setPosition(0);
    pendingSeekRef.current = null;
  }, [tracker]);

  const resetProgress = useCallback(() => setPosition(0), []);

  const resumeAt = useCallback((trackId: string | undefined, seconds: number) => {
    if (trackId && audioRef.current?.dataset.trackId !== trackId) {
      pendingSeekRef.current = seconds;
    }

    setPosition(seconds);
    positionRef.current = seconds;
  }, []);

  const quality = settings.quality;

  useEffect(() => {
    recovery.reset();
  }, [recovery, quality]);

  const prepareNext = useCallback(
    (track: Track) => {
      if (crossfader.active) return;

      const forceAdaptive = recovery.forceAdaptive(quality, track.id);
      const sourceKey = sourceKeyOf(track.id, quality, forceAdaptive, sourceRevision);
      if (standbyRef.current?.sourceKey === sourceKey) return;

      dropStandby();
      const audio = spareAudio();
      if (!audio) return;

      const playback = new AdaptivePlayback(audio, {
        onFatalError: () => {
          if (standbyRef.current === standby) dropStandby();
        },
      });
      const standby: Standby = { trackId: track.id, sourceKey, playback, ready: false };
      standbyRef.current = standby;

      audio.preload = "auto";
      audio.dataset.trackId = track.id;
      audio.dataset.sourceKey = sourceKey;

      void playback
        .load({
          trackId: track.id,
          codec: track.codec,
          quality,
          forceAdaptive,
          startAt: 0,
          play: false,
        })
        .then(() => {
          standby.ready = true;
        })
        .catch(() => {
          if (standbyRef.current === standby) dropStandby();
        });
    },
    [crossfader, recovery, quality, sourceRevision, dropStandby, spareAudio],
  );

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio) return;

    const fadeSeconds = fadePendingRef.current;
    fadePendingRef.current = 0;

    if (!currentTrack) {
      crossfader.finish();
      dropStandby();
      audio.pause();
      return;
    }

    const forceAdaptive = recovery.forceAdaptive(quality, currentTrack.id);
    const sourceKey = sourceKeyOf(currentTrack.id, quality, forceAdaptive, sourceRevision);
    if (audio.dataset.sourceKey === sourceKey) return;

    recovery.clearFailure();

    if (retryTimerRef.current !== null) {
      window.clearTimeout(retryTimerRef.current);
      retryTimerRef.current = null;
    }

    const standby = standbyRef.current;
    const incoming = spareAudio();

    if (standby?.ready && standby.sourceKey === sourceKey && incoming && !incoming.error) {
      const outgoingPlayback = adaptiveRef.current;

      standbyRef.current = null;
      audioRef.current = incoming;
      adaptiveRef.current = standby.playback;

      recordedRef.current = null;
      tracker.finish("trackSkipped");
      tracker.begin(currentTrack);

      pendingSeekRef.current = null;
      positionRef.current = incoming.currentTime;
      setDuration(
        Number.isFinite(incoming.duration) ? incoming.duration : currentTrack.durationSeconds,
      );
      const ranges = incoming.buffered;
      setBuffered(ranges.length > 0 ? ranges.end(ranges.length - 1) : 0);
      recovery.loaded(currentTrack.id);

      if (fadeSeconds > 0 && isPlaying) {
        crossfader.start(audio, incoming, fadeSeconds, () => silence(audio, outgoingPlayback));
      } else {
        silence(audio, outgoingPlayback);
        incoming.volume = crossfader.level;
      }
      return;
    }

    crossfader.finish();
    dropStandby();

    const staysOnSameTrack = audio.dataset.trackId === currentTrack.id;

    if (staysOnSameTrack) {
      pendingSeekRef.current ??= audio.currentTime || positionRef.current;
    } else {
      recordedRef.current = null;

      tracker.finish("trackSkipped");
      tracker.begin(currentTrack);
    }

    const startAt = staysOnSameTrack
      ? (pendingSeekRef.current ?? (audio.currentTime || positionRef.current))
      : (pendingSeekRef.current ?? 0);
    pendingSeekRef.current = null;

    audio.dataset.trackId = currentTrack.id;
    audio.dataset.sourceKey = sourceKey;
    positionRef.current = startAt;
    setDuration(currentTrack.durationSeconds || 0);

    const reportLoadFailure = () => {
      const offline = typeof navigator !== "undefined" && !navigator.onLine;
      if (!failSource(isPlaying)) return;

      if (offline) notify(t("player.offlineWaiting"), "info");
    };

    adaptiveRef.current?.destroy();
    const playback = new AdaptivePlayback(audio, {
      onFatalError: () => reportLoadFailure(),
    });
    adaptiveRef.current = playback;

    void playback
      .load({
        trackId: currentTrack.id,
        codec: currentTrack.codec,
        quality,
        forceAdaptive,
        startAt,
        play: isPlaying,
      })
      .then(() => {
        if (adaptiveRef.current === playback) recovery.loaded(currentTrack.id);
      })
      .catch(() => {
        if (adaptiveRef.current === playback) reportLoadFailure();
      });
  }, [
    currentTrack,
    quality,
    sourceRevision,
    isPlaying,
    notify,
    t,
    tracker,
    recovery,
    failSource,
    crossfader,
    dropStandby,
    spareAudio,
  ]);

  useEffect(() => {
    const standby = standbyRef.current;
    if (standby && (crossfade <= 0 || standby.trackId !== nextTrack?.id)) dropStandby();
  }, [crossfade, nextTrack, dropStandby]);

  useEffect(
    () => () => {
      if (retryTimerRef.current !== null) window.clearTimeout(retryTimerRef.current);
      crossfader.finish();
      standbyRef.current?.playback.destroy();
      adaptiveRef.current?.destroy();
    },
    [crossfader],
  );

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio) return;

    if (isPlaying && audio.dataset.sourceLoading !== "true") {
      crossfader.resume();
      audio
        .play()
        .catch((reason: unknown) => {
          const name = reason instanceof DOMException ? reason.name : "";
          if (name !== "AbortError") {
            setIsPlaying(false);

            if (name === "NotAllowedError") notify(t("player.autoplayBlocked"), "error");
          }
        })
        .catch(() => {});
    } else {
      audio.pause();
      crossfader.pause();
    }
  }, [isPlaying, currentTrack, notify, t, setIsPlaying, crossfader]);

  useEffect(() => {
    crossfader.setLevel(volume);

    for (const audio of [primaryRef.current, secondaryRef.current]) {
      if (audio) audio.muted = muted;
    }

    if (!crossfader.active && audioRef.current) audioRef.current.volume = volume;
  }, [volume, muted, crossfader]);

  const handleTimeUpdate = useCallback(() => {
    const audio = audioRef.current;
    if (!audio) return;

    const at = audio.currentTime;
    tracker.accumulate(at);
    crossfader.tick();
    if (scrubRef.current !== null) return;

    setPosition(at);
    positionRef.current = at;

    const track = currentTrack;
    if (!track) return;

    if (recordedRef.current !== track.id && at >= historyThresholdFor(track.durationSeconds)) {
      recordedRef.current = track.id;

      void api
        .recordPlay(track.id, Math.floor(at))
        .then(() => invalidate("history"))
        .catch(() => {});
    }

    if (crossfader.active || fadePendingRef.current > 0 || !nextTrack) return;

    const total = Number.isFinite(audio.duration) ? audio.duration : track.durationSeconds;
    const seconds = crossfadeSeconds({
      setting: crossfade,
      repeat,
      current: track,
      currentDuration: total,
      next: nextTrack,
    });
    const remaining = total - at;
    const stage = crossfadeStage(remaining, seconds);
    if (stage === "wait") return;

    scriptableVolume ??=
      Object.assign(document.createElement("audio"), { volume: 0.5 }).volume === 0.5;
    if (!scriptableVolume) return;

    prepareNext(nextTrack);

    const incoming = spareAudio();
    if (
      stage !== "fade" ||
      audio.paused ||
      !standbyRef.current?.ready ||
      !incoming ||
      incoming.readyState < HTMLMediaElement.HAVE_FUTURE_DATA
    ) {
      return;
    }

    fadePendingRef.current = remaining;
    tracker.finish("trackCompleted");
    onTrackEnded();
  }, [
    currentTrack,
    nextTrack,
    crossfade,
    repeat,
    tracker,
    invalidate,
    crossfader,
    prepareNext,
    spareAudio,
    onTrackEnded,
  ]);

  const handleProgress = useCallback(() => {
    const audio = audioRef.current;
    if (!audio) return;

    const ranges = audio.buffered;
    setBuffered(ranges.length > 0 ? ranges.end(ranges.length - 1) : 0);
  }, []);

  const handleEnded = useCallback(() => {
    tracker.finish("trackCompleted");

    if (repeat === "one") {
      seek(0);

      if (currentTrack) tracker.begin(currentTrack);

      void audioRef.current?.play().catch(() => setIsPlaying(false));
      return;
    }

    onTrackEnded();
  }, [onTrackEnded, repeat, tracker, currentTrack, seek, setIsPlaying]);

  const handleError = useCallback(() => {
    const audio = audioRef.current;
    if (!audio || !currentTrack) return;
    if (audio.dataset.sourceLoading === "true") return;
    if (audio.dataset.playbackMode !== "progressive") return;

    const resumeAt = audio.currentTime > 0 ? audio.currentTime : positionRef.current;
    const shouldResume = isPlaying || resumeAt > 0;

    const decision = recovery.decide({
      trackId: currentTrack.id,
      errorCode: audio.error?.code,
      offline: typeof navigator !== "undefined" && !navigator.onLine,
    });

    if (decision.kind === "offline") {
      if (failSource(isPlaying)) notify(t("player.offlineWaiting"), "info");
      return;
    }

    if (decision.kind === "giveUp") {
      recovery.recover();

      if (isPlaying) onTrackEnded();
      return;
    }

    if (decision.kind === "fallback") {
      pendingSeekRef.current = resumeAt;
      setSourceRevision((revision) => revision + 1);
      return;
    }

    const { attempt } = decision;

    retryTimerRef.current = window.setTimeout(() => {
      retryTimerRef.current = null;

      const element = audioRef.current;
      if (!element || element.dataset.trackId !== currentTrack.id) return;

      const retry = () => {
        if (element.dataset.playbackMode !== "progressive") return;

        element.src = mediaUrl.stream(currentTrack.id);
        element.addEventListener("loadedmetadata", () => (element.currentTime = resumeAt), {
          once: true,
        });
        element.load();

        if (shouldResume) void element.play().catch(() => {});
      };

      if (attempt === 0) void refreshSession().then(retry);
      else retry();
    }, decision.delayMs);
  }, [currentTrack, isPlaying, notify, t, recovery, failSource, onTrackEnded]);

  const handleWaiting = useCallback(() => {
    const audio = audioRef.current;

    if (
      !audio ||
      !currentTrack ||
      quality !== "Original" ||
      audio.dataset.playbackMode !== "progressive" ||
      audio.currentTime <= 0 ||
      recovery.degraded
    ) {
      return;
    }

    const ranges = audio.buffered;
    const bufferedUntil = ranges.length > 0 ? ranges.end(ranges.length - 1) : audio.currentTime;
    if (bufferedUntil - audio.currentTime > 2) return;

    pendingSeekRef.current = audio.currentTime;
    recovery.degrade();
    setSourceRevision((revision) => revision + 1);
  }, [currentTrack, quality, recovery]);

  const getPosition = useCallback(() => audioRef.current?.currentTime ?? positionRef.current, []);

  const getDuration = useCallback(() => {
    const decoded = audioRef.current?.duration;
    return decoded !== undefined && Number.isFinite(decoded) ? decoded : 0;
  }, []);

  const trackedPosition = useCallback(() => positionRef.current, []);

  const fromActive = useCallback(
    (event: SyntheticEvent<HTMLAudioElement>) => event.currentTarget === audioRef.current,
    [],
  );

  const audioProps: ComponentPropsWithoutRef<"audio"> = {
    preload: "metadata",
    onTimeUpdate: (event) => fromActive(event) && handleTimeUpdate(),
    onProgress: (event) => fromActive(event) && handleProgress(),
    onLoadedMetadata: (event) =>
      fromActive(event) && setDuration(event.currentTarget.duration || 0),
    onDurationChange: (event) =>
      fromActive(event) && setDuration(event.currentTarget.duration || 0),
    onEnded: (event) => fromActive(event) && handleEnded(),
    onError: (event) => fromActive(event) && handleError(),
    onWaiting: (event) => fromActive(event) && handleWaiting(),
    onStalled: (event) => fromActive(event) && handleWaiting(),
    onPlay: (event) => fromActive(event) && setIsPlaying(true),
    onPause: (event) => {
      if (fromActive(event) && event.currentTarget.dataset.sourceLoading !== "true") {
        setIsPlaying(false);
      }
    },
    onPlaying: (event) => fromActive(event) && recovery.playing(),
  };

  return {
    audioRefs: [primaryRef, secondaryRef],
    audioProps,
    position,
    duration,
    buffered,
    getPosition,
    getDuration,
    trackedPosition,
    seek,
    seekBy,
    scrubBy,
    commitScrub,
    recoverSource,
    startQueue,
    resetProgress,
    resumeAt,
  };
}
