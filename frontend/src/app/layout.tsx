// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { Metadata, Viewport } from "next";
import { Onest } from "next/font/google";
import { cookies } from "next/headers";
import { AppShell } from "@/components/AppShell";
import { QueryProvider } from "@/components/QueryProvider";
import { AuthProvider } from "@/contexts/AuthContext";
import { I18nProvider } from "@/contexts/I18nContext";
import { PlayerProvider } from "@/contexts/PlayerContext";
import { SettingsProvider } from "@/contexts/SettingsContext";
import { SleepTimerProvider } from "@/contexts/SleepTimerContext";
import { ToastProvider } from "@/contexts/ToastContext";
import { UploadProvider } from "@/contexts/UploadContext";
import { EARLY_FETCH_SCRIPT, SESSION_HINT_COOKIE } from "@/lib/earlyFetch";
import { DEFAULT_LOCALE, LOCALE_COOKIE, isLocale, loadDictionary } from "@/lib/i18n";
import { parseSessionHint } from "@/lib/sessionHint";
import { NO_FLASH_THEME_SCRIPT, THEME_COLORS } from "@/lib/themeScript";
import "./globals.css";
import { ReactNode } from "react";

const onest = Onest({
  subsets: ["latin", "cyrillic"],
  variable: "--font-onest",
  display: "swap",
});

export const metadata: Metadata = {
  title: "Caimack",
  description: "A personal, music streaming library.",
  robots: { index: false, follow: false },
  appleWebApp: {
    capable: true,
    title: "Caimack",
    statusBarStyle: "black-translucent",
  },
};

export const viewport: Viewport = {
  themeColor: THEME_COLORS.dark,
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
};

export default async function RootLayout({ children }: { children: ReactNode }) {
  // Кука-подсказка не HttpOnly и несёт достаточно, чтобы отрисовать каркас приложения сразу,
  // не дожидаясь /auth/me на клиенте.
  const jar = await cookies();
  const initialUser = parseSessionHint(jar.get(SESSION_HINT_COOKIE)?.value);

  // Язык выбирается здесь, а не после гидратации: в клиентский бандл словари больше не входят,
  // сервер подаёт только активный. Куки может не быть — тогда английский, а провайдер догрузит
  // сохранённый выбор и поставит куку на будущее.
  const cookieLocale = jar.get(LOCALE_COOKIE)?.value;
  const initialLocale = cookieLocale && isLocale(cookieLocale) ? cookieLocale : DEFAULT_LOCALE;
  const initialDictionary = await loadDictionary(initialLocale);

  return (
    <html lang={initialLocale} className={onest.variable} suppressHydrationWarning>
      <head>
        <script dangerouslySetInnerHTML={{ __html: NO_FLASH_THEME_SCRIPT }} />
        <script dangerouslySetInnerHTML={{ __html: EARLY_FETCH_SCRIPT }} />
      </head>
      <body>
        <I18nProvider initialLocale={initialLocale} initialDictionary={initialDictionary}>
          <ToastProvider>
            <QueryProvider>
              <AuthProvider initialUser={initialUser}>
                <SettingsProvider>
                  <PlayerProvider>
                    <SleepTimerProvider>
                      <UploadProvider>
                        <AppShell>{children}</AppShell>
                      </UploadProvider>
                    </SleepTimerProvider>
                  </PlayerProvider>
                </SettingsProvider>
              </AuthProvider>
            </QueryProvider>
          </ToastProvider>
        </I18nProvider>
      </body>
    </html>
  );
}
