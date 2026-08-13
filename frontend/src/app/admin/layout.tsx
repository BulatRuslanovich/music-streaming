"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { ReactNode, useEffect } from "react";
import { useAuth } from "@/contexts/AuthContext";
import { Skeleton } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

const tabs = [
  { href: "/admin/users", label: "admin.users" as const },
  { href: "/admin/artists", label: "nav.artists" as const },
  { href: "/admin/tracks", label: "nav.tracks" as const },
];

export default function AdminLayout({ children }: { children: ReactNode }) {
  const { isAdmin, loading } = useAuth();
  const t = useT();
  const pathname = usePathname();
  const router = useRouter();

  useEffect(() => {
    if (!loading && !isAdmin) router.replace("/");
  }, [loading, isAdmin, router]);

  if (loading) return <Skeleton variant="row" count={6} />;

  if (!isAdmin) return null;

  return (
    <>
      <nav className="admin-tabs" aria-label={t("admin.tabs")}>
        {tabs.map(({ href, label }) => (
          <Link
            key={href}
            href={href}
            className={`admin-tab ${pathname === href ? "is-active" : ""}`}
            aria-current={pathname === href ? "page" : undefined}
          >
            {t(label)}
          </Link>
        ))}
      </nav>

      {children}
    </>
  );
}
