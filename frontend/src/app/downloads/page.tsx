"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import type { ReactNode } from "react";
import { api, errorMessage, formatEta, formatSize, titleHref } from "@/lib/api";
import type { BlocklistItem, Download, DownloadNetwork, DownloadState, LibraryItem } from "@/lib/api";
import { day, dayTime } from "@/lib/format";
import { titleKey, useActivity, useActivityRefresh } from "@/components/Activity";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { PosterArt } from "@/components/Poster";
import { useToast } from "@/components/Toasts";
import { Alert, Button, ButtonLink, Checkbox, Empty, IconButton, Modal, PageHeader, Progress, Skeleton, WellList } from "@/components/ui";

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
  const admin = useSession().user.role === "Admin";
  const [downloads, setDownloads] = useState<Download[] | null>(null);
  const [titles, setTitles] = useState<{ movies: ReadonlyMap<number, LibraryItem>; series: ReadonlyMap<number, LibraryItem> }>({ movies: new Map(), series: new Map() });
  const [error, setError] = useState<string | null>(null);
  const [removing, setRemoving] = useState<Download | null>(null);
  const [blocked, setBlocked] = useState<BlocklistItem[]>([]);
  const [network, setNetwork] = useState<DownloadNetwork | null>(null);
  const [checking, setChecking] = useState(false);
  const { connected, epoch, progress } = useActivity();

  const refresh = useCallback(() => {
    api.downloads().then((d) => { setDownloads(d); setError(null); }).catch((e: unknown) => setError(errorMessage(e)));
  }, []);
  const loadBlocked = useCallback(() => { api.blocklist().then(setBlocked).catch(() => undefined); }, []);

  useEffect(refresh, [refresh, epoch]);
  useEffect(loadBlocked, [loadBlocked, epoch]);
  useEffect(() => { api.network().then(setNetwork).catch(() => undefined); }, [epoch]);
  const checkNetwork = () => {
    setChecking(true);
    api.refreshNetwork().then(setNetwork).catch(() => undefined).finally(() => setChecking(false));
  };
  useActivityRefresh((a) => a.downloadId !== null, refresh);
  useActivityRefresh((a) => a.kind === "Blocklisted", loadBlocked);

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
  // Torrent errors and metadata trouble surface as Warning; split them out so they do not drown in the active list.
  const stuck = active.filter((d) => d.status === "Warning");
  const running = active.filter((d) => d.status !== "Paused");
  const speed = running.reduce((n, d) => n + (d.downloadRate ?? 0), 0);
  const titleOf = (d: Download) => (d.movieId !== null ? titles.movies.get(d.movieId) : d.seriesId !== null ? titles.series.get(d.seriesId) : undefined);

  const act = async (action: () => Promise<unknown>, done?: string, d?: Download) => {
    try {
      await action();
      if (done && d) toast({ key: titleKey(d) ?? `download:${d.id}`, icon: "search", tone: "seek", title: titleOf(d)?.title ?? d.releaseTitle, message: done });
    } catch (e) {
      toast({ key: d ? titleKey(d) ?? `download:${d.id}` : "downloads", icon: "alert", tone: "bad", title: "Downloads", message: errorMessage(e) });
    }
    refresh();
  };
  const searchAgain = (d: Download) => (d.movieId !== null ? api.autoSearch("movie", d.movieId) : d.seriesId !== null ? api.autoSearch("series", d.seriesId) : Promise.resolve());
  const unblock = (b: BlocklistItem) => void act(() => api.unblock(b.id)).then(loadBlocked);
  // The blocklist only keeps the release title, so that is what ties an entry to its download.
  const blockOf = (d: Download) => blocked.find((b) => b.title === d.releaseTitle);

  const row = (d: Download) => {
    const item = titleOf(d);
    const block = blockOf(d);
    const canSearch = admin && !!item && !item.hasFile;
    if (!admin) return <Row key={d.id} download={d} item={item} />;
    return (
      <Row key={d.id} download={d} item={item} onRemove={() => setRemoving(d)}
        onToggle={() => void act(() => (d.status === "Paused" ? api.resume(d.id) : api.pause(d.id)))}
        onRetryImport={d.state === "ImportBlocked" ? () => void act(() => api.retryImport(d.id), "Importing again.", d) : undefined}
        onRetry={canSearch ? () => void act(() => searchAgain(d), "Searching again.", d) : undefined}
        onBlocklist={canSearch && !block ? () => void act(async () => { await api.removeDownload(d.id, true, true); await searchAgain(d); }, "Release blocklisted. Searching again.", d) : undefined}
        onUnblock={block ? () => unblock(block) : undefined} />
    );
  };

  // Several downloads of one series or movie collapse into one line; a single one stays a plain row.
  const rows = (list: readonly Download[]) => {
    const groups = new Map<string, Download[]>();
    for (const d of list) {
      const key = d.seriesId !== null ? `series:${d.seriesId}` : d.movieId !== null ? `movie:${d.movieId}` : `download:${d.id}`;
      groups.set(key, [...(groups.get(key) ?? []), d]);
    }
    return [...groups].map(([key, items]) => items.length === 1 && items[0]
      ? row(items[0])
      : <TitleGroup key={key} items={items} item={items[0] && titleOf(items[0])}>{items.map(row)}</TitleGroup>);
  };

  return (
    <>
      <PageHeader title="Downloads" description={downloads && (running.length > 0
        ? <span><span className="text-ink tabular-nums">{formatSize(speed)}/s</span> across {running.length} {running.length === 1 ? "download" : "downloads"}</span>
        : "Nothing downloading right now.")}>
        {admin && active.length > 0 && (running.length > 0
          ? <Button icon="pause" onClick={() => void act(() => Promise.all(running.map((d) => api.pause(d.id))))}>Pause all</Button>
          : <Button icon="play" onClick={() => void act(() => Promise.all(active.map((d) => api.resume(d.id))))}>Resume all</Button>)}
      </PageHeader>
      {network && <NetworkLine network={network} checking={checking} onCheck={checkNetwork} />}
      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      {!downloads ? (
        <div className="space-y-3">{Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-24 rounded-card" />)}</div>
      ) : visible.length === 0 && blocked.length === 0 ? (
        <Empty icon="download" title="Queue is empty.">
          {admin && <ButtonLink href="/explore/" variant="ghost" size="sm" className="mt-3">Explore</ButtonLink>}
        </Empty>
      ) : (
        <div className="space-y-8">
          <Group title="Stuck" count={stuck.length}>{rows(stuck)}</Group>
          <Group title="Active" count={active.length - stuck.length}>{rows(active.filter((d) => !stuck.includes(d)))}</Group>
          <Group title="Importing" count={inGroup("importing").length}>{rows(inGroup("importing"))}</Group>
          <Group title="Failed" count={inGroup("failed").length}>{rows(inGroup("failed"))}</Group>
          {inGroup("completed").length > 0 && (
            <details className="group">
              <summary className="flex min-h-11 cursor-pointer list-none items-center gap-2">
                <h2 className="caption">Completed, last {COMPLETED_DAYS} days</h2>
                <span className="text-meta text-ink-3 tabular-nums">{inGroup("completed").length}</span>
                <Icon name="chevronDown" size={16} className="text-ink-3 transition group-open:rotate-180" />
              </summary>
              <WellList className="mt-3">{rows(inGroup("completed"))}</WellList>
            </details>
          )}
          {blocked.length > 0 && (
            <details className="group">
              <summary className="flex min-h-11 cursor-pointer list-none items-center gap-2">
                <h2 className="caption">Blocklist</h2>
                <span className="text-meta text-ink-3 tabular-nums">{blocked.length}</span>
                <Icon name="chevronDown" size={16} className="text-ink-3 transition group-open:rotate-180" />
              </summary>
              {/* Releases never grabbed again; unblocking lets a search pick them. */}
              <WellList className="mt-3" label="Blocklist">
                {blocked.map((b) => (
                  <li key={b.id} className="flex items-center gap-3 px-4 py-2.5">
                    <Icon name="ban" size={16} className="shrink-0 text-ink-3" />
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-meta font-medium" title={b.title}>{b.title}</p>
                      <p className="line-clamp-3 break-words text-meta text-ink-3">
                        {[b.quality, b.reason, day(b.date)].filter(Boolean).join(" · ")}
                      </p>
                    </div>
                    {admin && <Button size="sm" variant="ghost" onClick={() => unblock(b)} aria-label={`Unblock ${b.title}`}>Unblock</Button>}
                  </li>
                ))}
              </WellList>
            </details>
          )}
        </div>
      )}
      {removing && <RemoveDialog download={removing} onClose={() => setRemoving(null)} onRemoved={() => { setRemoving(null); refresh(); }} />}
    </>
  );
}

// "via 1.2.3.4 · Org · NL": where torrent traffic leaves from, so a dropped VPN shows.
function NetworkLine({ network: n, checking, onCheck }: { network: DownloadNetwork; checking: boolean; onCheck: () => void }) {
  const checked = n.checkedAt ? `Checked ${dayTime(n.checkedAt)}, listening on port ${n.listenPort}, ${n.openConnections} connections` : undefined;
  return (
    <p className="-mt-2 mb-4 flex items-center gap-1 text-meta text-ink-3" title={checked}>
      <Icon name="globe" size={14} className="shrink-0" />
      {n.publicIp ? (
        <span className="truncate">via <span className="text-ink tabular-nums">{n.publicIp}</span>{[n.org, n.country].filter(Boolean).map((p) => ` · ${p}`).join("")}</span>
      ) : n.error ? (
        <span className="line-clamp-3 break-words text-warn">Public IP unknown: {n.error}</span>
      ) : <span>Checking the public IP…</span>}
      <IconButton variant="ghost" icon="refresh" label="Check the public IP again" onClick={onCheck} disabled={checking} />
    </p>
  );
}

function TitleGroup({ items, item, children }: { items: readonly Download[]; item: LibraryItem | undefined; children: ReactNode }) {
  const size = items.reduce((n, d) => n + (d.size ?? 0), 0);
  const done = items.reduce((n, d) => n + (d.progress ?? 0) * (d.size ?? 0), 0);
  const rate = items.reduce((n, d) => n + (d.status === "Paused" ? 0 : d.downloadRate ?? 0), 0);
  const value = size > 0 ? done / size : 0;
  return (
    <li>
      <details className="group/title">
        <summary className="flex cursor-pointer list-none items-center gap-3 px-4 py-3">
          <PosterArt src={item?.poster ?? null} title={item?.title ?? ""} className="w-9 shrink-0" />
          <div className="min-w-0 flex-1 space-y-1.5">
            <p className="truncate text-body font-semibold">{item?.title ?? items[0]?.releaseTitle}</p>
            <Progress value={value} label={`${item?.title ?? "Downloads"} progress`} tone="live" />
            <p className="flex flex-wrap gap-x-3 text-meta text-ink-3 tabular-nums">
              <span className="font-semibold text-ink">{Math.round(value * 100)}%</span>
              <span>{items.length} downloads</span>
              {rate > 0 && <span>{formatSize(rate)}/s</span>}
            </p>
          </div>
          <Icon name="chevronDown" size={16} className="shrink-0 text-ink-3 transition group-open/title:rotate-180" />
        </summary>
        <ul className="divide-y divide-hair-2 border-t border-hair-2 bg-sunk pl-4">{children}</ul>
      </details>
    </li>
  );
}

function Group({ title, count, children }: { title: string; count: number; children: ReactNode }) {
  if (count === 0) return null;
  return (
    <section aria-label={title}>
      <h2 className="caption mb-3 flex items-center gap-2">{title}<span className="tabular-nums">{count}</span></h2>
      <WellList>{children}</WellList>
    </section>
  );
}

function Row({ download: d, item, onToggle, onRemove, onRetryImport, onRetry, onBlocklist, onUnblock }: {
  download: Download;
  item: LibraryItem | undefined;
  onToggle?: () => void;
  onRemove?: () => void;
  onRetryImport?: () => void;
  onRetry?: () => void;
  onBlocklist?: () => void;
  onUnblock?: () => void;
}) {
  const group = groupOf[d.state];
  const paused = d.status === "Paused";
  const value = d.progress ?? (group === "completed" ? 1 : 0);
  const href = d.movieId !== null ? titleHref("movie", d.movieId) : d.seriesId !== null ? titleHref("series", d.seriesId) : null;
  const eta = d.eta !== null ? formatEta(d.eta) : null;
  const name = item?.title ?? d.releaseTitle;

  return (
    <li className="flex gap-3 px-4 py-3">
      <PosterArt src={item?.poster ?? null} title={item?.title ?? ""} className="w-9 shrink-0 self-start" />
      <div className="min-w-0 flex-1 space-y-1.5">
        <div className="flex items-start gap-3">
          <div className="min-w-0 flex-1">
            {href
              ? <Link href={href} className="block truncate text-body font-semibold hover:underline">{item?.title ?? d.releaseTitle}</Link>
              : <p className="truncate text-body font-semibold">{d.releaseTitle}</p>}
            {href && <p className="truncate text-meta text-ink-3" title={d.releaseTitle}>{d.releaseTitle}</p>}
          </div>
          {group === "completed" && <Icon name="check" size={16} role="img" aria-hidden={false} aria-label="Imported" className="mt-1 shrink-0 text-ok" />}
          {group === "active" && onToggle && <IconButton variant="ghost" icon={paused ? "play" : "pause"} label={`${paused ? "Resume" : "Pause"} ${name}`} onClick={onToggle} />}
          {onRemove && <IconButton variant="ghost" icon="trash" label={`Remove ${name}`} onClick={onRemove} />}
        </div>

        {group === "active" && (
          <div className="space-y-1.5">
            <Progress value={value} eta={eta} label={`${item?.title ?? "Download"} progress`} tone={paused ? "ink" : "live"} />
            <p className="flex flex-wrap gap-x-3 text-meta text-ink-3 tabular-nums">
              <span className="font-semibold text-ink">{Math.round(value * 100)}%</span>
              {d.status === "Warning" ? <span className="text-warn">{d.message ?? "Stuck"}</span> : paused ? <span>Paused</span> : d.hasMetadata === false ? (
                <span>Waiting for metadata · {d.seeds ?? 0} seeders · {d.peers ?? 0} peers</span>
              ) : <>
                {d.downloadRate != null && <span>{formatSize(d.downloadRate)}/s</span>}
                {eta && <span>{eta} left</span>}
              </>}
            </p>
          </div>
        )}
        {group === "importing" && <p className="flex items-center gap-2 text-meta text-live"><span className="size-1.5 animate-pulse-dot rounded-full bg-live" />Moving to library…</p>}
        {group === "failed" && (
          <div className="space-y-2.5">
            <p className="flex items-start gap-2 text-meta text-bad"><Icon name="alert" size={14} className="mt-0.5 shrink-0" />{d.message ?? (d.state === "ImportBlocked" ? "The import is blocked." : "The download failed.")}</p>
            {(onRetryImport ?? onRetry ?? onUnblock) && (
              <div className="flex flex-wrap gap-2">
                {onRetryImport ? <Button size="sm" icon="refresh" onClick={onRetryImport} aria-label={`Retry import of ${name}`}>Retry import</Button>
                  : onRetry && <Button size="sm" icon="refresh" onClick={onRetry} aria-label={`Retry ${name}`}>Retry</Button>}
                {onBlocklist && <Button size="sm" variant="ghost" icon="ban" onClick={onBlocklist} aria-label={`Blocklist ${name} and search again`}>Blocklist & search again</Button>}
                {onUnblock && <Button size="sm" variant="ghost" onClick={onUnblock} aria-label={`Unblock ${name}`}>Unblock</Button>}
              </div>
            )}
          </div>
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
