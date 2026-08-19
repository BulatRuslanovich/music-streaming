// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { Metadata, Viewport } from "next";
import { Onest } from "next/font/google";
import { AppShell } from "@/components/AppShell";
import { QueryProvider } from "@/components/QueryProvider";
import { AuthProvider } from "@/contexts/AuthContext";
import { I18nProvider } from "@/contexts/I18nContext";
import { OfflineProvider } from "@/contexts/OfflineContext";
import { PlayerProvider } from "@/contexts/PlayerContext";
import { SettingsProvider } from "@/contexts/SettingsContext";
import { SleepTimerProvider } from "@/contexts/SleepTimerContext";
import { ToastProvider } from "@/contexts/ToastContext";
import { UploadProvider } from "@/contexts/UploadContext";
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

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en" className={onest.variable} suppressHydrationWarning>
      <head>
        <script dangerouslySetInnerHTML={{ __html: NO_FLASH_THEME_SCRIPT }} />
      </head>
      <body>
        <I18nProvider>
          <ToastProvider>
            <QueryProvider>
              <AuthProvider>
                <SettingsProvider>
                  <OfflineProvider>
                    <PlayerProvider>
                      <SleepTimerProvider>
                        <UploadProvider>
                          <AppShell>{children}</AppShell>
                        </UploadProvider>
                      </SleepTimerProvider>
                    </PlayerProvider>
                  </OfflineProvider>
                </SettingsProvider>
              </AuthProvider>
            </QueryProvider>
          </ToastProvider>
        </I18nProvider>
      </body>
    </html>
  );
}
