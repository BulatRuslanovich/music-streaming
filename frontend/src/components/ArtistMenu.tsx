"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { EditArtistDialog, type EditableArtist } from "./EditArtistDialog";
import { ArtistIcon, EditIcon, MoreIcon } from "./Icons";

/**
 * ⋮ рядом с исполнителем. Повторяет меню трека, чтобы один и тот же жест работал в обоих видах
 * строк.
 */
export function ArtistMenu({
  artist,
  open,
  onOpenChange,
  onChanged,
}: {
  artist: EditableArtist;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onChanged?: () => void;
}) {
  const { isAdmin } = useAuth();
  const t = useT();
  const router = useRouter();

  const [editing, setEditing] = useState(false);
  const containerRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!open) return;

    const closeOnOutside = (event: MouseEvent) => {
      if (!containerRef.current?.contains(event.target as Node)) onOpenChange(false);
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") onOpenChange(false);
    };

    document.addEventListener("mousedown", closeOnOutside);
    document.addEventListener("keydown", closeOnEscape);

    return () => {
      document.removeEventListener("mousedown", closeOnOutside);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [open, onOpenChange]);

  return (
    <div className="menu-anchor" ref={containerRef}>
      <button
        type="button"
        className="icon-button"
        onClick={() => onOpenChange(!open)}
        aria-label={t("artists.moreActions", { name: artist.name })}
        aria-expanded={open}
      >
        <MoreIcon size={16} />
      </button>

      {open && (
        <div className="menu" role="menu">
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              onOpenChange(false);
              router.push(`/artists/${artist.id}`);
            }}
          >
            <ArtistIcon size={16} /> {t("menu.openArtist")}
          </button>

          {isAdmin && (
            <button
              type="button"
              role="menuitem"
              onClick={() => {
                setEditing(true);
                onOpenChange(false);
              }}
            >
              <EditIcon size={16} /> {t("menu.editArtist")}
            </button>
          )}
        </div>
      )}

      {editing && (
        <EditArtistDialog
          artist={artist}
          onClose={() => setEditing(false)}
          onSaved={onChanged}
        />
      )}
    </div>
  );
}
