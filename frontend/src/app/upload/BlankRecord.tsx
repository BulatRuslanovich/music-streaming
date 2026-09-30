// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { CSSProperties } from "react";

export type BlankRecordState = "empty" | "ready" | "dragging" | "uploading";

/**
 * Пластинка страницы загрузки: тестовый пресс в крафтовом конверте с вырубкой. Обложки у
 * загружаемых файлов ещё нет, поэтому это та же пластинка, что в плеере, только без оформления:
 * белая этикетка и бумажный конверт.
 *
 * Состояние двигает диск: пустая очередь — он едва выглядывает, файлы выбраны — выехал,
 * файлы над страницей — выехал дальше, навстречу. Во время загрузки диск крутится, а игла идёт
 * по дорожкам от края к этикетке, как на настоящей стороне: `played` — доля от 0 до 100.
 */
export function BlankRecord({ state, played }: { state: BlankRecordState; played: number }) {
  return (
    <div
      className="record blank-record"
      data-out="true"
      data-state={state}
      data-spinning={state === "uploading"}
      style={{ "--played": played / 100 } as CSSProperties}
      aria-hidden="true"
    >
      <div className="record-disc">
        <div className="record-spin">
          <div className="record-label blank-record-label" />
        </div>
        <div className="blank-record-played" />
        <div className="record-sheen" />
      </div>

      <div className="blank-record-sleeve" />
    </div>
  );
}
