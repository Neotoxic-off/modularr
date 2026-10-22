"use client";

import { useEffect, useState } from "react";
import { api, errorMessage } from "@/lib/api";
import type { Folder } from "@/lib/api";
import { Icon } from "@/components/icons";
import { Alert, Button, Modal, Spinner } from "@/components/ui";

// Browses the server's filesystem (the container's, when run in Docker).
export function FolderPicker({ start, onPick, onClose }: { start: string; onPick: (path: string) => void; onClose: () => void }) {
  const [path, setPath] = useState<string | null>(start || null);
  const [folder, setFolder] = useState<Folder | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.folders(path)
      .then((f) => { setFolder(f); setError(null); })
      // A not-yet-created start folder falls back to the roots.
      .catch((e: unknown) => (path === start && start ? setPath(null) : setError(errorMessage(e))));
  }, [path, start]);

  return (
    <Modal onClose={onClose} title={<><h2 className="font-semibold">Choisir un dossier</h2><p className="truncate font-mono text-xs text-muted">{folder?.path ?? "/"}</p></>}>
      {error && <div className="p-4"><Alert>{error}</Alert></div>}
      {!folder ? <div className="p-5"><Spinner /></div> : (
        <ul className="max-h-[50vh] overflow-y-auto py-1">
          {folder.path !== null && (
            <li>
              <button onClick={() => setPath(folder.parent)} className="flex w-full items-center gap-3 px-5 py-2 text-sm text-muted hover:bg-raised">
                <Icon name="up" />..
              </button>
            </li>
          )}
          {folder.directories.map((d) => (
            <li key={d.path}>
              <button onClick={() => setPath(d.path)} className="flex w-full items-center gap-3 px-5 py-2 text-left text-sm hover:bg-raised">
                <Icon name="folder" className="shrink-0 text-accent" /><span className="truncate">{d.name}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
      <footer className="flex justify-end gap-2 border-t border-line px-5 py-3">
        <Button variant="ghost" onClick={onClose}>Annuler</Button>
        <Button variant="primary" disabled={!folder?.path} onClick={() => folder?.path && onPick(folder.path)}>Choisir ce dossier</Button>
      </footer>
    </Modal>
  );
}
