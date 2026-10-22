"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { api, errorMessage, formatEta, formatSize, titleHref } from "@/lib/api";
import type { Download, DownloadState } from "@/lib/api";
import { useActivity, useActivityRefresh } from "@/components/Activity";
import { Icon } from "@/components/icons";
import { Alert, Badge, Button, Empty, Modal, PageHeader, Skeleton, Spinner, Switch, Segmented } from "@/components/ui";
import type { Tone } from "@/components/ui";

type Group = "active" | "completed" | "failed" | "all";

const groupOf: Record<DownloadState, Group | null> = {
  Downloading: "active", ImportPending: "active", Importing: "active",
  Imported: "completed",
  ImportBlocked: "failed", FailedPending: "failed", Failed: "failed",
  Ignored: null,
};

const stateLabel: Record<DownloadState, [string, Tone]> = {
  Downloading: ["Downloading", "live"], ImportPending: ["Waiting to import", "neutral"], Importing: ["Importing", "live"],
  Imported: ["Imported", "ok"], ImportBlocked: ["Import blocked", "warn"], FailedPending: ["Failed", "bad"],
  Failed: ["Failed", "bad"], Ignored: ["Removed", "neutral"],
};

const POLL_MS = 2000;

export default function DownloadsPage() {
  const [downloads, setDownloads] = useState<Download[] | null>(null);
  const [group, setGroup] = useState<Group>("active");
  const [error, setError] = useState<string | null>(null);
  const [removing, setRemoving] = useState<Download | null>(null);
  const { connected, epoch, progress } = useActivity();

  const refresh = useCallback(() => {
    api.downloads().then((d) => { setDownloads(d); setError(null); }).catch((e: unknown) => setError(errorMessage(e)));
  }, []);

  useEffect(refresh, [refresh, epoch]);
  useActivityRefresh((a) => a.downloadId !== null, refresh);

  // Polls only while the live stream is down.
  useEffect(() => {
    if (connected) return;
    const timer = setInterval(refresh, POLL_MS);
    return () => clearInterval(timer);
  }, [connected, refresh]);

  const visible = (downloads ?? []).filter((d) => groupOf[d.state] !== null).map((d) => {
    const live = progress.get(d.id);
    return live ? { ...d, progress: live.progress, downloadRate: live.rate, eta: live.eta } : d;
  });
  const inGroup = (d: Download, g: Group) => g === "all" || groupOf[d.state] === g;
  const count = (g: Group) => visible.filter((d) => inGroup(d, g)).length;
  const shown = visible.filter((d) => inGroup(d, group)).sort((a, b) => b.added.localeCompare(a.added));
  const speed = visible.reduce((n, d) => n + (groupOf[d.state] === "active" && d.status !== "Paused" ? d.downloadRate ?? 0 : 0), 0);

  const act = (action: Promise<void>) => action.then(refresh).catch((e: unknown) => setError(errorMessage(e)));

  return (
    <>
      <PageHeader title="Downloads" description={downloads && (count("active") > 0
        ? <span><span className="text-ink tabular-nums">{count("active")}</span> active, <span className="text-ink tabular-nums">{formatSize(speed)}/s</span> in total</span>
        : "Nothing downloading right now.")}>
        <Segmented label="Show" value={group} onChange={setGroup} options={[
          { value: "active", label: "Active", count: count("active") },
          { value: "completed", label: "Completed", count: count("completed") },
          { value: "failed", label: "Failed", count: count("failed") },
          { value: "all", label: "All", count: count("all") },
        ]} />
      </PageHeader>
      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      {!downloads ? (
        <div className="space-y-2">{Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-20" />)}</div>
      ) : shown.length === 0 ? (
        <Empty icon="download" title={group === "failed" ? "No failed downloads" : group === "completed" ? "Nothing completed yet" : "Nothing downloading"}>
          Releases grabbed for your titles show up here with live progress.
        </Empty>
      ) : (
        <ul className="divide-y divide-hair-2 overflow-hidden rounded-tile border border-hair bg-fill">
          {shown.map((d) => (
            <Row key={d.id} download={d}
              onToggle={() => act(d.status === "Paused" ? api.resume(d.id) : api.pause(d.id))}
              onRemove={() => setRemoving(d)} />
          ))}
        </ul>
      )}
      {removing && <RemoveDialog download={removing} onClose={() => setRemoving(null)} onRemoved={() => { setRemoving(null); refresh(); }} />}
    </>
  );
}

function Row({ download: d, onToggle, onRemove }: { download: Download; onToggle: () => void; onRemove: () => void }) {
  const [label, tone] = stateLabel[d.state];
  const paused = d.status === "Paused";
  const active = groupOf[d.state] === "active";
  const progress = Math.round((d.progress ?? (d.state === "Imported" ? 1 : 0)) * 100);
  const href = d.movieId !== null ? titleHref("movie", d.movieId) : d.seriesId !== null ? titleHref("series", d.seriesId) : null;

  return (
    <li className="px-4 py-4 transition hover:bg-fill-2 sm:px-5">
      <div className="flex items-start gap-3">
        <span className={`mt-0.5 grid size-9 shrink-0 place-items-center rounded-lg ${d.category === "movies" ? "bg-fill-2 text-live" : "bg-fill-2 text-ink"}`}>
          <Icon name={d.category === "movies" ? "film" : "tv"} size={16} />
        </span>
        <div className="min-w-0 flex-1">
          {href
            ? <Link href={href} className="block truncate text-sm font-medium hover:text-ink" title={d.releaseTitle}>{d.releaseTitle}</Link>
            : <p className="truncate text-sm font-medium" title={d.releaseTitle}>{d.releaseTitle}</p>}
          <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-ink-3">
            <Badge tone={paused ? "neutral" : tone}>{paused ? "Paused" : label}</Badge>
            <span className="tabular-nums">{formatSize(d.size)}</span>
            <span>{d.indexer}</span>
            {active && !paused && d.downloadRate != null && (
              <>
                <span className="flex items-center gap-1 tabular-nums"><Icon name="down" size={12} />{formatSize(d.downloadRate)}/s</span>
                <span className="flex items-center gap-1 tabular-nums"><Icon name="clock" size={12} />{formatEta(d.eta)} left</span>
              </>
            )}
          </div>
          {d.message && <p className="mt-1.5 text-xs text-warn">{d.message}</p>}
        </div>
        <div className="flex shrink-0 gap-0.5">
          {active && (
            <Button variant="ghost" size="sm" aria-label={paused ? "Resume" : "Pause"} title={paused ? "Resume" : "Pause"} onClick={onToggle}>
              <Icon name={paused ? "play" : "pause"} size={16} />
            </Button>
          )}
          <Button variant="ghost" size="sm" aria-label="Remove" title="Remove" onClick={onRemove}>
            <Icon name="trash" size={16} />
          </Button>
        </div>
      </div>

      {active && (
        <div className="mt-3 flex items-center gap-3 sm:pl-12">
          <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-fill-2" role="progressbar" aria-label="Progress" aria-valuenow={progress} aria-valuemin={0} aria-valuemax={100}>
            <div className={`h-full rounded-full transition-[width] duration-700 ${paused ? "bg-ink-3" : "bg-gradient-to-r from-white to-white"}`} style={{ width: `${progress}%` }} />
          </div>
          <span className="w-10 text-right text-xs text-ink-3 tabular-nums">{progress}%</span>
        </div>
      )}
    </li>
  );
}

function RemoveDialog({ download, onClose, onRemoved }: { download: Download; onClose: () => void; onRemoved: () => void }) {
  const [deleteData, setDeleteData] = useState(true);
  const [blocklist, setBlocklist] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function remove() {
    setBusy(true);
    try {
      await api.removeDownload(download.id, deleteData, blocklist);
      onRemoved();
    } catch (e) {
      setError(errorMessage(e));
      setBusy(false);
    }
  }

  return (
    <Modal onClose={onClose} title="Remove download?" description={<span className="break-all">{download.releaseTitle}</span>}
      footer={<>
        <Button variant="ghost" onClick={onClose}>Cancel</Button>
        <Button variant="danger" onClick={remove} disabled={busy}>{busy ? <Spinner /> : "Remove"}</Button>
      </>}>
      <div className="space-y-4 px-5 pb-5">
        <Switch checked={deleteData} onChange={setDeleteData} label="Delete downloaded data" description="Removes the files from the download folder." />
        <Switch checked={blocklist} onChange={setBlocklist} label="Blocklist this release" description="This exact release will not be grabbed again." />
        {error && <Alert>{error}</Alert>}
      </div>
    </Modal>
  );
}
