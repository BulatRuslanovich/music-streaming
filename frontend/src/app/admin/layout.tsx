"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect } from "react";
import { useAuth } from "@/contexts/AuthContext";
import { Skeleton } from "@/components/ui";

const tabs = [
  { href: "/admin/users", label: "Users" },
  { href: "/admin/artists", label: "Artists" },
  { href: "/admin/tracks", label: "Tracks" },
];

/**
 * Client-side gate for the admin area. The API's 403 is the real enforcement; this only keeps a
 * non-admin from staring at a page of failed requests.
 */
export default function AdminLayout({ children }: { children: React.ReactNode }) {
  const { isAdmin, loading } = useAuth();
  const pathname = usePathname();
  const router = useRouter();

  useEffect(() => {
    if (!loading && !isAdmin) router.replace("/");
  }, [loading, isAdmin, router]);

  if (loading) return <Skeleton variant="row" count={6} />;

  // The redirect is already in flight; render nothing rather than a flash of the panel.
  if (!isAdmin) return null;

  return (
    <>
      <nav className="admin-tabs" aria-label="Administration">
        {tabs.map(({ href, label }) => (
          <Link
            key={href}
            href={href}
            className={`admin-tab ${pathname === href ? "is-active" : ""}`}
            aria-current={pathname === href ? "page" : undefined}
          >
            {label}
          </Link>
        ))}
      </nav>

      {children}
    </>
  );
}
