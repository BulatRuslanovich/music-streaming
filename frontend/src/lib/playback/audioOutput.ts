// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

// Один MediaElementSource на элемент; визуализатор и обработка звука используют один граф.
class AudioOutput {
  private context: AudioContext | null = null;
  private media = new WeakMap<
    HTMLMediaElement,
    { source: MediaElementAudioSourceNode; gain: GainNode }
  >();
  private normalization = 1;
  private bus: GainNode | null = null;

  get output(): GainNode {
    if (!this.bus) {
      this.bus = this.getContext().createGain();
      this.bus.connect(this.getContext().destination);
    }
    return this.bus;
  }

  getContext(): AudioContext {
    return (this.context ??= new AudioContext());
  }

  source(audio: HTMLMediaElement): MediaElementAudioSourceNode {
    const existing = this.media.get(audio);
    if (existing) return existing.source;
    const context = this.getContext();
    const gain = context.createGain();
    gain.gain.value = this.normalization;
    gain.connect(this.output);
    const source = context.createMediaElementSource(audio);
    source.connect(gain);
    this.media.set(audio, { source, gain });
    return source;
  }

  setNormalization(audio: HTMLMediaElement, value: number) {
    this.normalization = Number.isFinite(value) ? Math.max(0, Math.min(2, value)) : 1;
    const graph = this.media.get(audio);
    if (graph) {
      const time = this.getContext().currentTime;
      graph.gain.gain.cancelScheduledValues(time);
      graph.gain.gain.setTargetAtTime(this.normalization, time, 0.15);
    }
  }

  async unlock() {
    try {
      await this.getContext().resume();
    } catch {}
  }
}

export const audioOutput = new AudioOutput();

if (typeof window !== "undefined") {
  // Жест разблокирует также удалённый запуск после открытия диалога устройств.
  window.addEventListener("pointerdown", () => void audioOutput.unlock(), { passive: true });
  window.addEventListener("keydown", () => void audioOutput.unlock(), { passive: true });
}
