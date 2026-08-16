import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import type { CapacitorConfig } from "@capacitor/cli";

/**
 * Оболочка ничего не собирает и не хранит: это WebView поверх того же прода, который открывается
 * в браузере. Поэтому фронт и API остаются на одном origin, относительный `/api` из
 * frontend/src/lib/http.ts работает как есть, а сессионные куки остаются first-party.
 *
 * Плата за это — приложение бесполезно без сети и не умеет офлайн-загрузки: service worker живёт
 * на стороне сайта и кэширует ровно то же, что кэшировал бы браузер.
 */

const PLACEHOLDER_DOMAINS = new Set(["music.example.com", "localhost", "example.com"]);

/**
 * Домен читается из корневого .env, а не дублируется здесь: он уже описан там для Caddy, и второе
 * место, которое надо помнить и держать в синхроне, рано или поздно разъедется с первым.
 */
function readRootEnv(): Record<string, string> {
  const values: Record<string, string> = {};

  let contents: string;
  try {
    // Capacitor транспилирует этот файл в CommonJS и подключает через require, поэтому здесь
    // работает __dirname, а import.meta.url сломал бы загрузку конфига.
    contents = readFileSync(resolve(__dirname, "../.env"), "utf8");
  } catch {
    return values;
  }

  for (const line of contents.split("\n")) {
    const match = /^\s*([A-Z0-9_]+)\s*=\s*(.*?)\s*$/.exec(line);
    if (!match) continue;
    values[match[1]] = match[2].replace(/^["']|["']$/g, "");
  }

  return values;
}

function resolveServerUrl(): string {
  const explicit = process.env.MOBILE_SERVER_URL?.trim();
  if (explicit) return explicit.replace(/\/+$/, "");

  const domain = readRootEnv().PUBLIC_DOMAIN?.trim();

  if (!domain || PLACEHOLDER_DOMAINS.has(domain)) {
    console.warn(
      `\n  ВНИМАНИЕ: PUBLIC_DOMAIN в .env — ${domain ?? "не задан"}.\n` +
        `  Приложение соберётся, но откроет пустоту. Пропиши боевой домен в .env\n` +
        `  или собери с MOBILE_SERVER_URL=https://твой-домен npm run sync.\n`,
    );
  }

  return `https://${domain ?? "music.caimack.ru"}`;
}

const serverUrl = resolveServerUrl();

const config: CapacitorConfig = {
  appId: "com.caimack.app",
  appName: "Caimack",

  /**
   * Локальной сборки нет — при заданном server.url содержимое webDir в WebView не попадает. Папка
   * нужна только потому, что без неё CLI откажется работать; лежащая там страница видна лишь если
   * подменить server.url на пустой.
   */
  webDir: "www",

  // Тот же цвет, что и background_color в frontend/src/app/manifest.ts: иначе при старте и при
  // повороте экрана из-под тёмного интерфейса подсвечивает белый фон WebView.
  backgroundColor: "#121212",

  server: {
    url: serverUrl,
    // Официально server.url предназначен для live-reload, а не для прода. Ограничение не
    // техническое: так собранное приложение — тонкий клиент, и Apple такое режет по гайдлайну 4.2.
    // Для Android и сайдлоада это рабочий способ, но он временный.
    cleartext: serverUrl.startsWith("http://"),
  },

  android: {
    backgroundColor: "#121212",
    // Ссылки наружу (last.fm и прочее) должны уходить в браузер, а не запирать пользователя
    // в WebView без адресной строки и кнопки «назад».
    allowMixedContent: false,
  },

  // По этой метке бэкенд и фронт смогут отличить оболочку от браузера — понадобится, когда
  // появятся нативные загрузки или токен-авторизация вместо кук.
  appendUserAgent: "CaimackApp",
};

export default config;
