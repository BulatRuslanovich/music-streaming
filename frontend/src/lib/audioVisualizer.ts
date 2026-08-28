// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

/**
 * Спектр играющего звука для плеера.
 *
 * Главное правило этого модуля: **он не имеет права молчать звук**. Как только элемент
 * попадает в Web Audio через `createMediaElementSource`, весь его выход идёт через граф,
 * и любая ошибка внутри графа — это тишина, а не пропавшая картинка. Поэтому:
 *
 *   source ──► destination            звук, эту ветку никто никогда не трогает
 *        └───► analyser ──► gain(0) ──► destination   отвод: слышимого вклада ноль
 *
 * Анализатор стоит ответвлением, а не в разрыве. Ветка с нулевым усилением нужна, чтобы
 * узел гарантированно обсчитывался: узел без пути к destination движок вправе не тянуть.
 *
 * Почему это безопасно именно здесь:
 * - `<audio>` в приложении один и живёт весь его срок (PlayerContext), а AdaptivePlayback
 *   его переиспользует, а не подменяет. `createMediaElementSource` для одного элемента
 *   можно звать только раз — второй бросает InvalidStateError;
 * - поток всегда идёт с того же origin (`API_BASE = "/api"`), поэтому граф не «отравлен»
 *   CORS и не выдаёт тишину.
 */

/** Готовые полосы, 0..1. Потребителю остаётся только нарисовать их. */
export type SpectrumFrame = Float32Array;

type Listener = (frame: SpectrumFrame) => void;

/**
 * Большое БПФ обязательно. При fftSize 128 бин выходит шириной ~375 Гц: весь бас и вся
 * середина умещаются в первые три-четыре бина, а остальные — это 4–18 кГц, где у музыки
 * почти ничего нет. Картинка из такого разбора — ровная низкая стена. Здесь бин ~23 Гц.
 */
const FFT_SIZE = 2048;

/** Сколько полос отдаём наружу. Зеркальная полоса плеера показывает их дважды. */
const BAND_COUNT = 32;

/** Диапазон полос. Ниже — инфраниз, выше — воздух; ни там, ни там смотреть не на что. */
const MIN_HZ = 40;

const MAX_HZ = 16_000;

/**
 * Падение полосы за кадр. Быстрый подъём и медленное падение — то, что делает картинку
 * живой: без него сглаживание анализатора одинаково тормозит и атаку, и затухание.
 */
const FALL_PER_FRAME = 0.055;

/**
 * Сколько кадров подряд можно получить абсолютную тишину при играющем звуке, прежде чем
 * считать отвод сломанным. Секунда с небольшим на 60 Гц — переживает паузы между треками.
 */
const SILENCE_LIMIT = 90;

type State = "idle" | "ready" | "unavailable";

class AudioVisualizer {
  private audio: HTMLAudioElement | null = null;
  private context: AudioContext | null = null;
  private analyser: AnalyserNode | null = null;
  private bins: Uint8Array<ArrayBuffer> | null = null;
  private state: State = "idle";

  /** Границы полос в бинах, посчитанные один раз под частоту дискретизации контекста. */
  private edges: number[] = [];
  private readonly levels = new Float32Array(BAND_COUNT);

  private readonly listeners = new Set<Listener>();
  private frame: number | null = null;
  private playing = false;
  private silent = 0;

  /** Вызывается один раз из PlayerContext, до всякого воспроизведения. */
  attach(audio: HTMLAudioElement): void {
    this.audio = audio;
  }

  get available(): boolean {
    return this.state !== "unavailable";
  }

  subscribe(listener: Listener): () => void {
    this.listeners.add(listener);
    this.tick();

    return () => {
      this.listeners.delete(listener);
      if (this.listeners.size === 0) this.stop();
    };
  }

  setPlaying(playing: boolean): void {
    this.playing = playing;
    if (playing) this.silent = 0;
    this.tick();
  }

  /** Пересмотреть условия, не трогая состояние воспроизведения (видимость вкладки). */
  refresh(): void {
    this.tick();
  }

  /**
   * Создаёт граф. Зовётся только из обработчика воспроизведения: до жеста пользователя
   * AudioContext остаётся `suspended`, а подключать к нему элемент в этом состоянии —
   * ровно тот случай, когда звук пропадает.
   */
  private connect(): boolean {
    if (this.state === "ready") return true;
    if (this.state === "unavailable" || !this.audio) return false;

    try {
      const context = new AudioContext();
      const source = context.createMediaElementSource(this.audio);

      // Сначала прямой путь: что бы ни случилось дальше, звук уже дошёл до выхода.
      source.connect(context.destination);

      const analyser = context.createAnalyser();
      analyser.fftSize = FFT_SIZE;

      // Сглаживание оставляем небольшим: инерцию картинке даёт собственный спад полос,
      // а сглаживание анализатора тормозило бы и атаку тоже — вот от чего «залипает».
      analyser.smoothingTimeConstant = 0.55;

      // Дефолтный потолок в -30 дБ громкая музыка перекрывает басом постоянно, и нижняя
      // треть спектра стоит упёртой в 255 — вместо картинки получается ровная стена.
      analyser.minDecibels = -80;
      analyser.maxDecibels = -20;

      const silent = context.createGain();
      silent.gain.value = 0;

      source.connect(analyser);
      analyser.connect(silent);
      silent.connect(context.destination);

      this.context = context;
      this.analyser = analyser;
      this.bins = new Uint8Array(new ArrayBuffer(analyser.frequencyBinCount));
      this.edges = bandEdges(context.sampleRate, analyser.frequencyBinCount);
      this.state = "ready";

      return true;
    } catch {
      // Единственный выход из неудачи — навсегда остаться без картинки. Звук при этом
      // не пострадал: до `createMediaElementSource` элемент играет сам, а после неё
      // прямая ветка подключается первой.
      this.state = "unavailable";
      return false;
    }
  }

  private tick(): void {
    const canRun = this.listeners.size > 0 && !reduceMotion() && !hidden();

    if (!canRun) {
      this.stop();
      return;
    }

    // На паузе цикл не обрываем: иначе столбики замирают на последнем значении, как будто
    // музыка всё ещё идёт. Пусть догаснут до нуля — render остановится сам.
    if (!this.playing && this.frame === null) return;

    if (!this.connect()) return;

    void this.context?.resume().catch(() => {});

    if (this.frame === null) this.frame = requestAnimationFrame(this.render);
  }

  private readonly render = (): void => {
    const analyser = this.analyser;
    const bins = this.bins;

    if (!analyser || !bins) {
      this.frame = null;
      return;
    }

    analyser.getByteFrequencyData(bins);

    if (this.looksBroken(bins)) {
      this.state = "unavailable";
      this.stop();
      this.levels.fill(0);
      this.listeners.forEach((listener) => listener(this.levels));
      return;
    }

    const levels = this.levels;
    const settling = !this.playing;
    let alive = false;

    for (let band = 0; band < BAND_COUNT; band += 1) {
      let target = 0;

      if (!settling) {
        const from = this.edges[band];
        const to = this.edges[band + 1];

        // Максимум, а не среднее: усреднение по полосе срезает ровно те пики, ради
        // которых на спектр и смотрят, и оставляет вялую рябь.
        let peak = 0;
        for (let bin = from; bin < to; bin += 1) if (bins[bin] > peak) peak = bins[bin];

        target = peak / 255;
      }

      // Вверх — сразу, вниз — с постоянной скоростью.
      levels[band] =
        target >= levels[band] ? target : Math.max(target, levels[band] - FALL_PER_FRAME);

      if (levels[band] > 0.001) alive = true;
    }

    this.listeners.forEach((listener) => listener(levels));

    // Догорели после паузы — дальше крутить кадры не за чем.
    if (settling && !alive) {
      this.stop();
      return;
    }

    this.frame = requestAnimationFrame(this.render);
  };

  /**
   * Абсолютный ноль по всем бинам при играющем звуке — это не тихий трек, а отвод, до
   * которого не доходит сигнал. Тихая музыка всё равно даёт ненулевые младшие бины.
   */
  private looksBroken(bins: Uint8Array): boolean {
    const audio = this.audio;
    const soundExpected =
      audio !== null && !audio.paused && !audio.muted && audio.volume > 0 && audio.currentTime > 0;

    if (!soundExpected) {
      this.silent = 0;
      return false;
    }

    let peak = 0;
    for (const value of bins) if (value > peak) peak = value;

    this.silent = peak > 0 ? 0 : this.silent + 1;

    return this.silent >= SILENCE_LIMIT;
  }

  private stop(): void {
    if (this.frame !== null) {
      cancelAnimationFrame(this.frame);
      this.frame = null;
    }
  }
}

/**
 * Границы полос, разложенные логарифмически: слух так и устроен, а бины БПФ линейны.
 * При линейном делении первая октава занимала бы один бин, а последняя — половину всех,
 * из-за чего вся музыка оказывалась в паре крайних столбиков.
 */
function bandEdges(sampleRate: number, binCount: number): number[] {
  const perBin = sampleRate / 2 / binCount;
  const edges: number[] = [];

  for (let band = 0; band <= BAND_COUNT; band += 1) {
    const hz = MIN_HZ * (MAX_HZ / MIN_HZ) ** (band / BAND_COUNT);
    edges.push(Math.min(binCount, Math.round(hz / perBin)));
  }

  // В нижних полосах шаг лога тоньше одного бина; без этого они схлопываются в пустые.
  for (let band = 1; band <= BAND_COUNT; band += 1) {
    if (edges[band] <= edges[band - 1]) edges[band] = Math.min(binCount, edges[band - 1] + 1);
  }

  return edges;
}

function reduceMotion(): boolean {
  return (
    typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches
  );
}

function hidden(): boolean {
  return typeof document !== "undefined" && document.hidden;
}

export const visualizer = new AudioVisualizer();

if (typeof document !== "undefined") {
  // Во вкладке в фоне рисовать нечего, а rAF там всё равно душат до одного кадра в секунду.
  // Именно `refresh`, а не `setPlaying`: видимость вкладки не должна подменять собой то,
  // играет ли музыка, — иначе возврат на вкладку «запускал» бы спектр на паузе.
  document.addEventListener("visibilitychange", () => visualizer.refresh());
}

export const SPECTRUM_BANDS = BAND_COUNT;
