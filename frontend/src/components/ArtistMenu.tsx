"use client";

import * as DropdownMenu from "@radix-ui/react-dropdown-menu";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { EditArtistDialog, type EditableArtist } from "./EditArtistDialog";
import { ArtistIcon, EditIcon, MoreIcon } from "./Icons";

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

  return (
    <div className="menu-anchor">
      <DropdownMenu.Root open={open} onOpenChange={onOpenChange}>
        <DropdownMenu.Trigger asChild>
          <button
            type="button"
            className="icon-button"
            aria-label={t("artists.moreActions", { name: artist.name })}
          >
            <MoreIcon size={16} />
          </button>
        </DropdownMenu.Trigger>

        <DropdownMenu.Portal>
          <DropdownMenu.Content className="menu" align="end" sideOffset={6}>
            <DropdownMenu.Item
              asChild
              onSelect={(event) => {
                event.preventDefault();
                onOpenChange(false);
                router.push(`/artists/${artist.id}`);
              }}
            >
              <button type="button">
                <ArtistIcon size={16} /> {t("menu.openArtist")}
              </button>
            </DropdownMenu.Item>

            {isAdmin && (
              <DropdownMenu.Item
                asChild
                onSelect={(event) => {
                  event.preventDefault();
                  setEditing(true);
                  onOpenChange(false);
                }}
              >
                <button type="button">
                  <EditIcon size={16} /> {t("menu.editArtist")}
                </button>
              </DropdownMenu.Item>
            )}
          </DropdownMenu.Content>
        </DropdownMenu.Portal>
      </DropdownMenu.Root>

      {editing && (
        <EditArtistDialog artist={artist} onClose={() => setEditing(false)} onSaved={onChanged} />
      )}
    </div>
  );
}
