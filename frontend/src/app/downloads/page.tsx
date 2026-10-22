"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import type { ReactNode } from "react";
import { api, errorMessage, formatEta, formatSize, titleHref } from "@/lib/api";
import type { Download, DownloadState, LibraryItem } from "@/lib/api";
import { useActivity, useActivityRefresh } from "@/components/Activity";
import { Icon } from "@/components/icons";
import { PosterArt } from "@/components/Poster";
import { useToast } from "@/components/Toasts";
import { Alert, Badge, Button, Checkbox, Empty, Modal, PageHeader, Progress, Skeleton } from "@/components/ui";

type Group = "active" | "importing" | "failed" | "completed";

const groupOf: Record<DownloadState, Group | null> = {
  Downloading: "active",
  ImportPending: "importing", Importing: "importing",
  ImportBlocked: "failed", FailedPending: "failed", Failed: "failed",
  Imported: "completed",
  Ignored: null,
};

const POLL_MS = 2000;
const COMPLETED_DAYS = 7;

export default function DownloadsPage() {
  const toast = useToast();
  const [downloads, setDownloads] = useState<Download[] | null>(null);
  const [titles, setTitles] = useState<{ movies: ReadonlyMap<number, LibraryItem>; series: ReadonlyMap<number, LibraryItem> }>({ movies: new Map(), series: new Map() });
  const [error, setError] = useState<string | null>(null);
  const [removing, setRemoving] = useState<Download | null>(null);
  const { connected, epoch, progress } = useActivity();

  const refresh = useCallback(() => {
    api.downloads().then((d) => { setDownloads(d); setError(null); }).catch((e: unknown) => setError(errorMessage(e)));
  }, []);

  useEffect(refresh, [refresh, epoch]);
  useActivityRefresh((a) => a.downloadId !== null, refresh);

  // Posters, destinations and whether a file is already there come from the library.
  useEffect(() => {
    Promise.all([api.movies(), api.series()])
      .then(([movies, series]) => setTitles({ movies: new Map(movies.map((m) => [m.id, m])), series: new Map(series.map((s) => [s.id, s])) }))
      .catch(() => undefined);
  }, [epoch]);

  // Polls only while the live stream is down.
  useEffect(() => {
    if (connected) return;
    const timer = setInterval(refresh, POLL_MS);
    return () => clearInterval(timer);
  }, [connected, refresh]);

  const since = new Date();
  since.setDate(since.getDate() - COMPLETED_DAYS);
  const visible = (downloads ?? [])
    .filter((d) => groupOf[d.state] !== null && (groupOf[d.state] !== "completed" || new Date(d.added) > since))
    .map((d) => {
      const live = progress.get(d.id);
      return live ? { ...d, progress: live.progress, downloadRate: live.rate, eta: live.eta } : d;
    })
    .sort((a, b) => b.added.localeCompare(a.added));
  const inGroup = (g: Group) => visible.filter((d) => groupOf[d.state] === g);
  const active = inGroup("active");
  const running = active.filter((d) => d.status !== "Paused");
  const speed = running.reduce((n, d) => n + (d.downloadRate ?? 0), 0);
  const titleOf = (d: Download) => (d.movieId !== null ? titles.movies.get(d.movieId) : d.seriesId !== null ? titles.series.get(d.seriesId) : undefined);

  const act = async (action: () => Promise<unknown>, done?: string, d?: Download) => {
    try {
      await action();
      if (done && d) toast({ key: `download:${d.id}`, icon: "search", tone: "seek", title: titleOf(d)?.title ?? d.releaseTitle, message: done });
    } catch (e) {
      toast({ key: d ? `download:${d.id}` : "downloads", icon: "alert", tone: "bad", title: "Downloads", message: errorMessage(e) });
    }
    refresh();
  };
  const searchAgain = (d: Download) => (d.movieId !== null ? api.autoSearch("movie", d.movieId) : d.seriesId !== null ? api.autoSearch("series", d.seriesId) : Promise.resolve());

  const row = (d: Download) => {
    const item = titleOf(d);
    const canSearch = !!item && !item.hasFile;
    return (
      <Row key={d.id} download={d} item={item} onRemove={() => setRemoving(d)}
        onToggle={() => void act(() => (d.status === "Paused" ? api.resume(d.id) : api.pause(d.id)))}
        onRetry={canSearch ? () => void act(() => searchAgain(d), "Searching again.", d) : undefined}
        onBlocklist={canSearch ? () => void act(async () => { await api.removeDownload(d.id, true, true); await searchAgain(d); }, "Release blocklisted. Searching again.", d) : undefined} />
    );
  };

  return (
    <>
      <PageHeader title="Downloads" description={downloads && (running.length > 0
        ? <span><span className="text-ink tabular-nums">{formatSize(speed)}/s</span> across {running.length} {running.length === 1 ? "download" : "downloads"}</span>
        : "Nothing downloading right now.")}>
        {active.length > 0 && (running.length > 0
          ? <Button icon="pause" onClick={() => void act(() => Promise.all(running.map((d) => api.pause(d.id))))}>Pause all</Button>
          : <Button icon="play" onClick={() => void act(() => Promise.all(active.map((d) => api.resume(d.id))))}>Resume all</Button>)}
      </PageHeader>
      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      {!downloads ? (
        <div className="space-y-3">{Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-24 rounded-[20px]" />)}</div>
      ) : visible.length === 0 ? (
        <Empty icon="download" title="Queue is empty.">
          <p>Releases grabbed for your titles show up here with live progress.</p>
          <Link href="/explore/" className="mt-3 inline-flex h-9 items-center rounded-pill px-4 text-[13px] font-semibold text-ink hover:bg-fill">Explore →</Link>
        </Empty>
      ) : (
        <div className="space-y-8">
          <Group title="Active" items={active}>{active.map(row)}</Group>
          <Group title="Importing" items={inGroup("importing")}>{inGroup("importing").map(row)}</Group>
          <Group title="Failed" items={inGroup("failed")}>{inGroup("failed").map(row)}</Group>
          {inGroup("completed").length > 0 && (
            <details className="group">
              <summary className="flex min-h-11 cursor-pointer list-none items-center gap-2">
                <h2 className="caption">Completed, last {COMPLETED_DAYS} days</h2>
                <span className="text-[11px] text-ink-3 tabular-nums">{inGroup("completed").length}</span>
                <Icon name="chevronDown" size={16} className="text-ink-3 transition group-open:rotate-180" />
              </summary>
              <ul className="mt-3 space-y-3">{inGroup("completed").map(row)}</ul>
            </details>
          )}
        </div>
      )}
      {removing && <RemoveDialog download={removing} onClose={() => setRemoving(null)} onRemoved={() => { setRemoving(null); refresh(); }} />}
    </>
  );
}

function Group({ title, items, children }: { title: string; items: readonly Download[]; children: ReactNode }) {
  if (items.length === 0) return null;
  return (
    <section aria-label={title}>
      <h2 className="caption mb-3 flex items-center gap-2">{title}<span className="tabular-nums">{items.length}</span></h2>
      <ul className="space-y-3">{children}</ul>
    </section>
  );
}

function Row({ download: d, item, onToggle, onRemove, onRetry, onBlocklist }: {
  download: Download;
  item: LibraryItem | undefined;
  onToggle: () => void;
  onRemove: () => void;
  onRetry?: () => void;
  onBlocklist?: () => void;
}) {
  const group = groupOf[d.state];
  const paused = d.status === "Paused";
  const value = d.progress ?? (group === "completed" ? 1 : 0);
  const href = d.movieId !== null ? titleHref("movie", d.movieId) : d.seriesId !== null ? titleHref("series", d.seriesId) : null;
  const eta = d.eta !== null ? formatEta(d.eta) : null;

  return (
    <li className="well flex gap-4 rounded-[20px] p-4">
      <PosterArt src={item?.poster ?? null} title={item?.title ?? ""} className="w-[52px] shrink-0 rounded-[10px]" />
      <div className="min-w-0 flex-1 space-y-2.5">
        <div className="flex items-start gap-3">
          <div className="min-w-0 flex-1">
            {href
              ? <Link href={href} className="block truncate text-[15px] font-semibold hover:underline">{item?.title ?? d.releaseTitle}</Link>
              : <p className="truncate text-[15px] font-semibold">{d.releaseTitle}</p>}
            <p className="truncate text-[13px] text-ink-3" title={d.releaseTitle}>{d.releaseTitle}</p>
          </div>
          {group === "active" && (
            <button onClick={onToggle} aria-label={paused ? "Resume" : "Pause"} title={paused ? "Resume" : "Pause"}
              className="grid size-11 shrink-0 cursor-pointer place-items-center rounded-full text-ink-2 hover:bg-fill-2 hover:text-ink">
              <Icon name={paused ? "play" : "pause"} size={17} />
            </button>
          )}
          <button onClick={onRemove} aria-label="Remove" title="Remove"
            className="grid size-11 shrink-0 cursor-pointer place-items-center rounded-full text-ink-2 hover:bg-fill-2 hover:text-ink">
            <Icon name="trash" size={17} />
          </button>
        </div>

        {group === "active" && (
          <div className="space-y-1.5">
            <Progress value={value} eta={eta} label={`${item?.title ?? "Download"} progress`} tone={paused ? "ink" : "live"} />
            <p className="flex flex-wrap gap-x-3 text-[13px] text-ink-3 tabular-nums">
              <span className="font-semibold text-ink">{Math.round(value * 100)}%</span>
              {paused ? <span>Paused</span> : <>
                {d.downloadRate != null && <span>{formatSize(d.downloadRate)}/s</span>}
                {eta && <span>{eta} left</span>}
              </>}
              <span>{formatSize(d.size)}</span>
              <span className="max-sm:hidden">{d.indexer}</span>
            </p>
          </div>
        )}
        {group === "importing" && <p className="flex items-center gap-2 text-[13px] text-live"><span className="size-1.5 animate-pulse-dot rounded-full bg-live" />Moving to library…</p>}
        {group === "failed" && (
          <div className="space-y-2.5">
            <p className="flex items-start gap-2 text-[13px] text-bad"><Icon name="alert" size={15} className="mt-0.5 shrink-0" />{d.message ?? (d.state === "ImportBlocked" ? "The import is blocked." : "The download failed.")}</p>
            {onRetry && onBlocklist && (
              <div className="flex flex-wrap gap-2">
                <Button size="sm" icon="refresh" onClick={onRetry}>Retry</Button>
                <Button size="sm" variant="ghost" icon="ban" onClick={onBlocklist}>Blocklist & search again</Button>
              </div>
            )}
          </div>
        )}
        {group === "completed" && (
          <p className="flex flex-wrap items-center gap-2 text-[13px]">
            <Badge tone="ok"><Icon name="check" size={12} strokeWidth={2.6} />Imported</Badge>
            {item && <span className="min-w-0 truncate text-ink-3" title={item.path}>Moved to {item.path}</span>}
          </p>
        )}
      </div>
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
        <Button variant="danger" onClick={remove} loading={busy}>Remove</Button>
      </>}>
      <div className="space-y-2 px-5 pb-5">
        <Checkbox checked={deleteData} onChange={setDeleteData} label="Delete data" description="Removes the files from the download folder." />
        <Checkbox checked={blocklist} onChange={setBlocklist} label="Blocklist" description="This exact release is never grabbed again." />
        {error && <Alert>{error}</Alert>}
      </div>
    </Modal>
  );
}
