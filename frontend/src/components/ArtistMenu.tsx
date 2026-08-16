"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { EditArtistDialog, type EditableArtist } from "./EditArtistDialog";
import { Button } from "./ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "./ui/dropdown-menu";
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
    <>
      <DropdownMenu open={open} onOpenChange={onOpenChange}>
        <DropdownMenuTrigger asChild>
          <Button
            variant="ghost"
            size="icon"
            aria-label={t("artists.moreActions", { name: artist.name })}
          >
            <MoreIcon size={16} />
          </Button>
        </DropdownMenuTrigger>

        <DropdownMenuContent>
          <DropdownMenuItem
            onAction={() => {
              onOpenChange(false);
              router.push(`/artists/${artist.id}`);
            }}
          >
            <ArtistIcon size={16} /> {t("menu.openArtist")}
          </DropdownMenuItem>

          {isAdmin && (
            <DropdownMenuItem
              onAction={() => {
                setEditing(true);
                onOpenChange(false);
              }}
            >
              <EditIcon size={16} /> {t("menu.editArtist")}
            </DropdownMenuItem>
          )}
        </DropdownMenuContent>
      </DropdownMenu>

      {editing && (
        <EditArtistDialog artist={artist} onClose={() => setEditing(false)} onSaved={onChanged} />
      )}
    </>
  );
}
