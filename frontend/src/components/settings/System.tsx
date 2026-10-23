"use client";

import { useEffect, useState } from "react";
import { api, backupHref, formatSize } from "@/lib/api";
import type { Backup } from "@/lib/api";
import { dayTime } from "@/lib/format";
import { Icon } from "@/components/icons";
import { Alert, Button, Empty, IconButton, Section, Skeleton, WellList, useAction } from "@/components/ui";

export function Backups() {
  const [backups, setBackups] = useState<Backup[] | null>(null);
  const { busy, error, run } = useAction();

  useEffect(() => {
    api.backups().then(setBackups).catch(() => setBackups([]));
  }, []);

  return (
    <Section title="Backups" description="Daily, last 7 kept. They hold your keys and password hashes."
      actions={<Button size="sm" icon="plus" loading={busy} onClick={() => void run(async () => { await api.createBackup(); setBackups(await api.backups()); })}>Back up now</Button>}>
      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      {!backups ? <Skeleton className="h-20" /> : backups.length === 0 ? <Empty icon="download" title="No backup yet" /> : (
        <WellList>
          {backups.map((b) => (
            <li key={b.name} className="flex items-center gap-3 px-4 py-2">
              <span className="min-w-0 flex-1">
                <span className="block truncate font-mono text-meta">{b.name}</span>
                <span className="block text-meta text-ink-3">{dayTime(b.created)} · {formatSize(b.size)}</span>
              </span>
              <a href={backupHref(b.name)} download aria-label={`Download ${b.name}`} title="Download"
                className="grid size-11 shrink-0 place-items-center rounded-full text-ink-2 transition hover:bg-fill-2 hover:text-ink"><Icon name="download" size={16} /></a>
              <IconButton variant="ghost" icon="trash" label={`Delete ${b.name}`} disabled={busy}
                onClick={() => confirm(`Delete the backup ${b.name}?`) && void run(async () => { await api.deleteBackup(b.name); setBackups(await api.backups()); })} />
            </li>
          ))}
        </WellList>
      )}
    </Section>
  );
}
