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

export default function AdminLayout({ children }: { children: React.ReactNode }) {
  const { isAdmin, loading } = useAuth();
  const pathname = usePathname();
  const router = useRouter();

  useEffect(() => {
    if (!loading && !isAdmin) router.replace("/");
  }, [loading, isAdmin, router]);

  if (loading) return <Skeleton variant="row" count={6} />;

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
