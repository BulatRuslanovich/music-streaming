import type { Metadata, Viewport } from "next";
import { Onest } from "next/font/google";
import { AppShell } from "@/components/AppShell";
import { AuthProvider } from "@/contexts/AuthContext";
import { I18nProvider } from "@/contexts/I18nContext";
import { PlayerProvider } from "@/contexts/PlayerContext";
import { ToastProvider } from "@/contexts/ToastContext";
import { NO_FLASH_THEME_SCRIPT, THEME_COLORS } from "@/lib/themeScript";
import "./globals.css";
import { ReactNode } from "react";

const onest = Onest({
  subsets: ["latin", "cyrillic"],
  variable: "--font-sans",
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
    <html lang="en" className={onest.variable}>
      <head>
        <script dangerouslySetInnerHTML={{ __html: NO_FLASH_THEME_SCRIPT }} />
      </head>
      <body>
        <I18nProvider>
          <ToastProvider>
            <AuthProvider>
              <PlayerProvider>
                <AppShell>{children}</AppShell>
              </PlayerProvider>
            </AuthProvider>
          </ToastProvider>
        </I18nProvider>
      </body>
    </html>
  );
}
