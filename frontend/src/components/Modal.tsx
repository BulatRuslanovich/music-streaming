"use client";

import * as Dialog from "@radix-ui/react-dialog";
import { ReactNode } from "react";
import { useT } from "@/contexts/I18nContext";
import { CloseIcon } from "./Icons";

export function Modal({
  title,
  onClose,
  children,
}: {
  title: string;
  onClose: () => void;
  children: ReactNode;
}) {
  const t = useT();

  return (
    <Dialog.Root open onOpenChange={(open) => !open && onClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className="modal-backdrop">
          <Dialog.Content className="modal" aria-describedby={undefined}>
            <header className="modal-header">
              <Dialog.Title asChild>
                <h2>{title}</h2>
              </Dialog.Title>
              <Dialog.Close asChild>
                <button type="button" className="icon-button" aria-label={t("action.close")}>
                  <CloseIcon size={18} />
                </button>
              </Dialog.Close>
            </header>

            {children}
          </Dialog.Content>
        </Dialog.Overlay>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
