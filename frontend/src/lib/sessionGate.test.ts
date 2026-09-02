// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { sessionGate } from "@/lib/sessionGate";

const alive = { renewal: "unavailable", hasRefreshCookie: true, hasSessionHint: true } as const;

describe("sessionGate", () => {
  it("пускает того, кому только что обновили сессию", () => {
    expect(sessionGate({ ...alive, renewal: "renewed" })).toBe("signedIn");

    // Даже если браузер пришёл вообще без кук: обновление их и выставит.
    expect(
      sessionGate({ renewal: "renewed", hasRefreshCookie: false, hasSessionHint: false }),
    ).toBe("signedIn");
  });

  it("пускает по паре «refresh + подсказка», пока обновляться незачем", () => {
    expect(sessionGate(alive)).toBe("signedIn");
  });

  /**
   * Тот самый баг: подсказка живёт 30 дней и переживает отзыв refresh-токена. Пока она одна
   * означала «вошёл», слушатель с мёртвой сессией не мог попасть на /login — middleware
   * заворачивал его обратно на страницу, где всё отвечало 401.
   */
  it("не верит подсказке, оставшейся без refresh-куки", () => {
    expect(
      sessionGate({ renewal: "unavailable", hasRefreshCookie: false, hasSessionHint: true }),
    ).toBe("signedOut");
  });

  it("объявляет сессию законченной, когда бэкенд отверг обновление", () => {
    // Подсказка на месте и refresh-кука на месте — и всё равно наружу: verdict от бэкенда.
    expect(sessionGate({ ...alive, renewal: "rejected" })).toBe("sessionEnded");
  });

  // Перезапуск бэкенда не должен выкидывать всех, кто в этот момент кликнул по ссылке.
  it("переживает недоступность бэкенда, не разлогинивая", () => {
    expect(sessionGate({ ...alive, renewal: "unavailable" })).toBe("signedIn");
  });

  it("не пускает того, у кого нет ничего", () => {
    expect(
      sessionGate({ renewal: "unavailable", hasRefreshCookie: false, hasSessionHint: false }),
    ).toBe("signedOut");
  });

  it("не пускает по одной refresh-куке без подсказки", () => {
    expect(
      sessionGate({ renewal: "unavailable", hasRefreshCookie: true, hasSessionHint: false }),
    ).toBe("signedOut");
  });
});
