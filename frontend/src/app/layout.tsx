import type { Metadata, Viewport } from "next";
import { AppShell } from "@/components/AppShell";
import { AuthProvider } from "@/contexts/AuthContext";
import { PlayerProvider } from "@/contexts/PlayerContext";
import { ToastProvider } from "@/contexts/ToastContext";
import "./globals.css";

export const metadata: Metadata = {
  title: "My Music",
  description: "A personal, self-hosted music streaming library.",
  // The library is private; keep it out of any crawler that reaches the public hostname.
  robots: { index: false, follow: false },
};

export const viewport: Viewport = {
  themeColor: "#0b0d12",
  width: "device-width",
  initialScale: 1,
  // The mobile player sits above the home indicator on iOS, so the safe area must be readable.
  viewportFit: "cover",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
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
