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
    <Modal onClose={onClose} title="Choose a folder" description={<span className="font-mono text-xs break-all">{folder?.path ?? "Drives"}</span>}
      footer={<>
        <Button variant="ghost" onClick={onClose}>Cancel</Button>
        <Button variant="primary" disabled={!folder?.path} onClick={() => folder?.path && onPick(folder.path)}>Use this folder</Button>
      </>}>
      {error && <div className="px-5 pb-3"><Alert>{error}</Alert></div>}
      {!folder ? <div className="p-5"><Spinner /></div> : (
        <ul className="max-h-[50vh] overflow-y-auto border-t border-line py-1">
          {folder.path !== null && (
            <li>
              <button onClick={() => setPath(folder.parent)} className="flex w-full cursor-pointer items-center gap-3 px-5 py-2 text-sm text-muted hover:bg-raised">
                <Icon name="up" size={16} />Parent folder
              </button>
            </li>
          )}
          {folder.directories.length === 0 && <li className="px-5 py-3 text-sm text-muted">No subfolders.</li>}
          {folder.directories.map((d) => (
            <li key={d.path}>
              <button onClick={() => setPath(d.path)} className="flex w-full cursor-pointer items-center gap-3 px-5 py-2 text-left text-sm hover:bg-raised">
                <Icon name="folder" size={16} className="shrink-0 text-accent" /><span className="truncate">{d.name}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </Modal>
  );
}
