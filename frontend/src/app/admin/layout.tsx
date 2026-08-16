"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { ReactNode, useEffect } from "react";
import { cn } from "@/lib/cn";
import type { TranslationKey } from "@/lib/i18n";
import { useAuth } from "@/contexts/AuthContext";
import { SkeletonGroup } from "@/components/ui/skeleton";
import { useT } from "@/contexts/I18nContext";

const tabs: { href: string; label: TranslationKey }[] = [
  { href: "/admin/users", label: "admin.users" },
  { href: "/admin/artists", label: "nav.artists" },
  { href: "/admin/tracks", label: "nav.tracks" },
];

export default function AdminLayout({ children }: { children: ReactNode }) {
  const { isAdmin, loading } = useAuth();
  const t = useT();
  const pathname = usePathname();
  const router = useRouter();

  useEffect(() => {
    if (!loading && !isAdmin) router.replace("/");
  }, [loading, isAdmin, router]);

  if (loading) return <SkeletonGroup variant="row" count={6} />;
  if (!isAdmin) return null;

  return (
    <>
      {/* Ссылки, а не Tabs: каждый раздел — отдельный адрес, и его должно быть видно в строке. */}
      <nav
        aria-label={t("admin.tabs")}
        className="flex gap-1 overflow-x-auto border-b border-border [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
      >
        {tabs.map(({ href, label }) => {
          const active = pathname === href;

          return (
            <Link
              key={href}
              href={href}
              aria-current={active ? "page" : undefined}
              className={cn(
                "-mb-px shrink-0 border-b-2 px-4 py-2.5 text-sm font-semibold whitespace-nowrap transition-colors hover:no-underline",
                active
                  ? "border-primary text-primary"
                  : "border-transparent text-muted-foreground hover:text-foreground",
              )}
            >
              {t(label)}
            </Link>
          );
        })}
      </nav>

      {children}
    </>
  );
}
