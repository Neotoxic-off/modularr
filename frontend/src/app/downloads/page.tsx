"use client";

import { useCallback, useEffect, useState } from "react";
import { api, errorMessage, formatEta, formatSize } from "@/lib/api";
import type { Download, DownloadState } from "@/lib/api";
import { Icon } from "@/components/icons";
import { Alert, Badge, Button, Empty, PageHeader, Spinner, Tabs } from "@/components/ui";
import type { Tone } from "@/components/ui";

type Group = "active" | "completed" | "failed" | "all";

const groupOf: Record<DownloadState, Group | null> = {
  Downloading: "active", ImportPending: "active", Importing: "active",
  Imported: "completed",
  ImportBlocked: "failed", FailedPending: "failed", Failed: "failed",
  Ignored: null,
};

const stateLabel: Record<DownloadState, [string, Tone]> = {
  Downloading: ["Téléchargement", "accent"], ImportPending: ["Import en attente", "neutral"], Importing: ["Import…", "accent"],
  Imported: ["Importé", "green"], ImportBlocked: ["Import bloqué", "amber"], FailedPending: ["Échec", "red"],
  Failed: ["Échec", "red"], Ignored: ["Retiré", "neutral"],
};

const POLL_MS = 2000;

export default function DownloadsPage() {
  const [downloads, setDownloads] = useState<Download[] | null>(null);
  const [group, setGroup] = useState<Group>("active");
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(() => {
    api.downloads().then((d) => { setDownloads(d); setError(null); }).catch((e: unknown) => setError(errorMessage(e)));
  }, []);

  useEffect(() => {
    refresh();
    const timer = setInterval(refresh, POLL_MS);
    return () => clearInterval(timer);
  }, [refresh]);

  const visible = (downloads ?? []).filter((d) => groupOf[d.state] !== null);
  const count = (g: Group) => visible.filter((d) => g === "all" || groupOf[d.state] === g).length;
  const shown = visible
    .filter((d) => group === "all" || groupOf[d.state] === group)
    .sort((a, b) => b.added.localeCompare(a.added));

  return (
    <>
      <PageHeader title="Téléchargements">
        <Tabs value={group} onChange={setGroup} options={[
          { value: "active", label: "En cours", count: count("active") },
          { value: "completed", label: "Terminés", count: count("completed") },
          { value: "failed", label: "Échecs", count: count("failed") },
          { value: "all", label: "Tout", count: count("all") },
        ]} />
      </PageHeader>
      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      {!downloads ? <Spinner /> : shown.length === 0 ? <Empty>Rien ici.</Empty> : (
        <ul className="divide-y divide-line overflow-hidden rounded-xl border border-line bg-surface">
          {shown.map((d) => <Row key={d.id} download={d} onChanged={refresh} onError={setError} />)}
        </ul>
      )}
    </>
  );
}

function Row({ download: d, onChanged, onError }: { download: Download; onChanged: () => void; onError: (e: string) => void }) {
  const [confirming, setConfirming] = useState(false);
  const [deleteData, setDeleteData] = useState(true);
  const [blocklist, setBlocklist] = useState(false);
  const [label, tone] = stateLabel[d.state];
  const paused = d.status === "Paused";
  const active = groupOf[d.state] === "active";
  const progress = Math.round((d.progress ?? (d.state === "Imported" ? 1 : 0)) * 100);

  const act = (action: Promise<void>) => action.then(onChanged).catch((e: unknown) => onError(errorMessage(e)));

  return (
    <li className="space-y-3 px-5 py-4">
      <div className="flex items-start gap-3">
        <div className="min-w-0 flex-1 space-y-1">
          <p className="truncate text-sm font-medium" title={d.releaseTitle}>{d.releaseTitle}</p>
          <div className="flex flex-wrap items-center gap-1.5 text-xs text-muted">
            <Badge tone={paused ? "neutral" : tone}>{paused ? "En pause" : label}</Badge>
            <span>{d.category === "movies" ? "Film" : "Série"}</span>·<span>{formatSize(d.size)}</span>·<span>{d.indexer}</span>
            {active && !paused && d.downloadRate != null && <>·<span>{formatSize(d.downloadRate)}/s</span>·<span>reste {formatEta(d.eta)}</span></>}
          </div>
          {d.message && <p className="text-xs text-amber-300/80">{d.message}</p>}
        </div>
        <div className="flex shrink-0 gap-1">
          {active && (
            <Button variant="ghost" aria-label={paused ? "Reprendre" : "Pause"} title={paused ? "Reprendre" : "Pause"}
              onClick={() => act(paused ? api.resume(d.id) : api.pause(d.id))}>
              <Icon name={paused ? "play" : "pause"} />
            </Button>
          )}
          <Button variant="ghost" aria-label="Supprimer" title="Supprimer" onClick={() => setConfirming((c) => !c)}>
            <Icon name="trash" />
          </Button>
        </div>
      </div>

      {active && (
        <div className="flex items-center gap-3">
          <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-raised" role="progressbar" aria-valuenow={progress} aria-valuemin={0} aria-valuemax={100}>
            <div className={`h-full rounded-full transition-[width] duration-500 ${paused ? "bg-muted" : "bg-accent"}`} style={{ width: `${progress}%` }} />
          </div>
          <span className="w-10 text-right text-xs text-muted tabular-nums">{progress}%</span>
        </div>
      )}

      {confirming && (
        <div className="flex flex-wrap items-center gap-x-5 gap-y-2 rounded-lg border border-red-500/25 bg-red-500/5 p-3 text-sm">
          <label className="flex items-center gap-2">
            <input type="checkbox" checked={deleteData} onChange={(e) => setDeleteData(e.target.checked)} className="accent-red-400" />
            Supprimer les fichiers
          </label>
          <label className="flex items-center gap-2">
            <input type="checkbox" checked={blocklist} onChange={(e) => setBlocklist(e.target.checked)} className="accent-red-400" />
            Blacklister la release
          </label>
          <div className="ml-auto flex gap-2">
            <Button variant="ghost" onClick={() => setConfirming(false)}>Annuler</Button>
            <Button variant="danger" onClick={() => act(api.removeDownload(d.id, deleteData, blocklist))}>Supprimer</Button>
          </div>
        </div>
      )}
    </li>
  );
}
