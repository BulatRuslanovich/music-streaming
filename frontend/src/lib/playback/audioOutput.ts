// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

// Один MediaElementSource на элемент; визуализатор и обработка звука используют один граф.
class AudioOutput {
  private context: AudioContext | null = null;
  private media = new WeakMap<HTMLMediaElement, MediaElementAudioSourceNode>();
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
    if (existing) return existing;
    const source = this.getContext().createMediaElementSource(audio);
    source.connect(this.output);
    this.media.set(audio, source);
    return source;
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
