import type { Metadata, Viewport } from "next";
import { Inter } from "next/font/google";
import { AppShell } from "@/components/AppShell";
import { AuthProvider } from "@/contexts/AuthContext";
import { PlayerProvider } from "@/contexts/PlayerContext";
import { ToastProvider } from "@/contexts/ToastContext";
import "./globals.css";

/**
 * Next downloads the font at build time and serves it from this origin, so the browser makes no
 * request to Google and the library stays as private as the rest of the app. Cyrillic is included
 * because track tags come from the files themselves and are not necessarily Latin.
 */
const inter = Inter({
  subsets: ["latin", "cyrillic"],
  variable: "--font-sans",
  display: "swap",
});

export const metadata: Metadata = {
  title: "CAIMACK",
  description: "A personal, music streaming library.",
  // The library is private; keep it out of any crawler that reaches the public hostname.
  robots: { index: false, follow: false },
};

export const viewport: Viewport = {
  // Must track --bg in styles/tokens.css so the browser chrome matches the page.
  themeColor: "#0b100c",
  width: "device-width",
  initialScale: 1,
  // The mobile player sits above the home indicator on iOS, so the safe area must be readable.
  viewportFit: "cover",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className={inter.variable}>
      <body>
        {/*
          Providers wrap the shell so that the audio element inside PlayerProvider is mounted once
          for the whole session: navigating between pages re-renders `children` only, and playback
          continues uninterrupted.
        */}
        <ToastProvider>
          <AuthProvider>
            <PlayerProvider>
              <AppShell>{children}</AppShell>
            </PlayerProvider>
          </AuthProvider>
        </ToastProvider>
      </body>
    </html>
  );
}
