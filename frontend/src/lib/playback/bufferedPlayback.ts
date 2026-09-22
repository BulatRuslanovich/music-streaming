// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { audioOutput } from "./audioOutput";

export function transitionOverlap(
  mode: string,
  seconds: number,
  remaining: number,
  nextDuration: number,
): number {
  return mode === "crossfade" ? Math.max(0, Math.min(seconds, remaining / 2, nextDuration / 2)) : 0;
}

interface Voice {
  id: string;
  buffer: AudioBuffer;
  source: AudioBufferSourceNode | null;
  gain: GainNode | null;
  normalization: number;
  start: number;
  offset: number;
}

/** Две записи планируются на часах аудиоустройства; таймер обновляет только UI. */
export class BufferedPlayback {
  private active: Voice | null = null;
  private upcoming: Voice | null = null;
  private tail: Voice | null = null;
  private playing = false;
  private volume = 1;
  private overlap = 0;

  get trackId() {
    return this.active?.id;
  }
  get duration() {
    return this.active?.buffer.duration ?? 0;
  }
  get position() {
    if (!this.active) return 0;
    return Math.min(
      this.duration,
      this.active.offset +
        (this.playing ? Math.max(0, this.context.currentTime - this.active.start) : 0),
    );
  }
  private get context() {
    return audioOutput.getContext();
  }

  load(id: string, buffer: AudioBuffer, position: number, playing: boolean, normalization: number) {
    this.stop();
    this.active = {
      id,
      buffer,
      offset: Math.max(0, Math.min(position, buffer.duration)),
      start: 0,
      source: null,
      gain: null,
      normalization,
    };
    if (playing) this.play();
  }

  prepare(id: string, buffer: AudioBuffer, normalization: number, mode: string, seconds: number) {
    this.cancelNext();
    if (!this.active) return;
    this.overlap = transitionOverlap(mode, seconds, this.duration - this.position, buffer.duration);
    this.upcoming = { id, buffer, offset: 0, start: 0, source: null, gain: null, normalization };
    this.scheduleNext();
  }

  play() {
    if (!this.active || this.playing) return;
    this.playing = true;
    this.startVoice(this.active, this.context.currentTime, this.active.offset);
    this.scheduleNext();
  }

  pause() {
    if (!this.active || !this.playing) return;
    this.active.offset = this.position;
    this.playing = false;
    this.stopVoice(this.active);
    this.stopVoice(this.upcoming);
    this.stopVoice(this.tail);
    this.tail = null;
  }

  seek(position: number) {
    if (!this.active) return;
    const playing = this.playing;
    this.pause();
    this.active.offset = Math.max(0, Math.min(position, this.duration));
    if (playing) this.play();
  }

  setVolume(volume: number) {
    this.volume = Math.max(0, Math.min(1, volume));
    // Громкость вынесена в отдельный узел источника, чтобы не отменять огибающую перехода.
    for (const voice of [this.active, this.upcoming, this.tail]) {
      if (voice?.gain)
        voice.gain.gain.setTargetAtTime(
          this.volume * voice.normalization,
          this.context.currentTime,
          0.03,
        );
    }
  }

  normalize(value: number) {
    if (this.active) this.active.normalization = value;
    this.setVolume(this.volume);
  }

  /** Возвращает смену трека после того, как звук уже переключился по расписанию. */
  tick(): "transition" | "ended" | null {
    if (!this.playing || !this.active) return null;
    if (
      this.tail &&
      this.context.currentTime >= this.tail.start + this.tail.buffer.duration - this.tail.offset
    ) {
      this.stopVoice(this.tail);
      this.tail = null;
    }
    if (this.upcoming?.source && this.context.currentTime >= this.upcoming.start) {
      this.stopVoice(this.tail);
      this.tail = this.active;
      this.active = this.upcoming;
      this.upcoming = null;
      return "transition";
    }
    if (this.position >= this.duration) {
      this.pause();
      return "ended";
    }
    return null;
  }

  cancelNext() {
    const scheduled = this.upcoming?.source != null;
    this.stopVoice(this.upcoming);
    this.upcoming = null;
    // Источник с запланированным fade-out нужно пересоздать без этой огибающей.
    if (scheduled && this.active && this.playing) {
      const position = this.position;
      this.stopVoice(this.active);
      this.startVoice(this.active, this.context.currentTime, position);
    }
  }

  stop() {
    this.stopVoice(this.active);
    this.stopVoice(this.upcoming);
    this.stopVoice(this.tail);
    this.active = this.upcoming = this.tail = null;
    this.playing = false;
  }

  private startVoice(voice: Voice, when: number, offset: number, fadeIn = 0) {
    const source = this.context.createBufferSource();
    source.buffer = voice.buffer;
    const envelope = this.context.createGain();
    envelope.gain.setValueAtTime(fadeIn > 0 ? 0 : 1, when);
    if (fadeIn > 0) envelope.gain.linearRampToValueAtTime(1, when + fadeIn);
    const gain = this.context.createGain();
    gain.gain.value = this.volume * voice.normalization;
    source.connect(envelope);
    envelope.connect(gain);
    gain.connect(audioOutput.output);
    source.onended = () => {
      source.disconnect();
      envelope.disconnect();
      gain.disconnect();
    };
    // Огибающая хранится на источнике отдельно от нормализации и пользовательской громкости.
    this.envelopes.set(voice, envelope);
    voice.source = source;
    voice.gain = gain;
    voice.start = when;
    voice.offset = offset;
    source.start(when, offset);
  }

  private readonly envelopes = new WeakMap<Voice, GainNode>();

  private scheduleNext() {
    if (!this.active || !this.upcoming || !this.playing || this.upcoming.source) return;
    const end = this.active.start + this.active.buffer.duration - this.active.offset;
    const when = Math.max(this.context.currentTime, end - this.overlap);
    const overlap = Math.max(0, end - when);
    this.startVoice(this.upcoming, when, 0, overlap);
    const envelope = this.envelopes.get(this.active);
    if (envelope && overlap > 0) {
      envelope.gain.setValueAtTime(1, when);
      envelope.gain.linearRampToValueAtTime(0, end);
    }
  }

  private stopVoice(voice: Voice | null) {
    if (!voice?.source) return;
    try {
      voice.source.stop();
    } catch {}
    voice.source.disconnect();
    voice.gain?.disconnect();
    this.envelopes.get(voice)?.disconnect();
    voice.source = null;
    voice.gain = null;
  }
}
