"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import type { CSSProperties, ReactNode } from "react";
import { api, currentWeek, episodeCode, errorMessage, formatEta, formatSize, isoDay, isSeries, kindOf, libraryHref, titleHref } from "@/lib/api";
import type { Activity, AiringEpisode, Dashboard, DiscoveryItem, Download, Kind, LibraryItem, Paths } from "@/lib/api";
import { activityHref, eventLook, titleKey, useActivity, useActivityFeed, useActivityRefresh } from "@/components/Activity";
import { groupEvents, LoadMore, Timeline, TimelineSkeleton } from "@/components/ActivityFeed";
import { markAdded } from "@/components/AddDialog";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { PosterCard, PosterSkeleton } from "@/components/Poster";
import { useChrome } from "@/components/Shell";
import { SubtitlesDialog } from "@/components/SubtitlesDialog";
import { useToast } from "@/components/Toasts";
import { Button, Chip, Progress, Tile, TileError, TileSkeleton } from "@/components/ui";

type Summary = { data: Dashboard | null; error: string | null; reload: () => void };
type Library = ReadonlyMap<string, LibraryItem>;
type Filter = "all" | "downloads" | "imports" | "problems";

const filters: readonly { value: Filter; label: string; match: (a: Activity) => boolean }[] = [
  { value: "all", label: "All", match: () => true },
  { value: "downloads", label: "Downloads", match: (a) => ["Grabbed", "DownloadStarted", "DownloadStalled", "Regrabbed"].includes(a.kind) },
  { value: "imports", label: "Imports", match: (a) => a.kind === "Imported" },
  { value: "problems", label: "Problems", match: (a) => a.level === "Warning" || a.level === "Error" },
];

// Only state changes are read out, never progress ticks.
const announced = new Set(["Imported", "Grabbed", "NothingFound", "DownloadFailed", "ImportFailed"]);

const libraryKey = (item: LibraryItem) => (isSeries(item) ? `series:${item.id}` : `movie:${item.id}`);
const rise = (index: number) => ({ animationDelay: `${Math.min(index, 8) * 30}ms` });

function greeting() {
  const hour = new Date().getHours();
  return hour < 5 ? "Good night" : hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
}

function useSummary(): Summary {
  const { epoch } = useActivity();
  const [data, setData] = useState<Dashboard | null>(null);
  const [error, setError] = useState<string | null>(null);
  const reload = useCallback(() => {
    api.dashboard().then((d) => { setData(d); setError(null); }).catch((e: unknown) => setError(errorMessage(e)));
  }, []);
  useEffect(reload, [reload, epoch]);
  useActivityRefresh(() => true, reload);
  return { data, error, reload };
}

function useLibrary(): Library {
  const [items, setItems] = useState<LibraryItem[]>([]);
  const load = useCallback(() => {
    Promise.all([api.movies(), api.series()]).then(([movies, series]) => setItems([...movies, ...series])).catch(() => undefined);
  }, []);
  useEffect(load, [load]);
  useActivityRefresh((a) => a.kind === "Added" || a.kind === "Imported" || a.kind === "Removed" || a.kind === "ScanCompleted", load);
  return useMemo(() => new Map(items.map((i) => [libraryKey(i), i])), [items]);
}

// Trending titles not in the library yet, alternating movies, series and anime.
function useForYou() {
  const [items, setItems] = useState<DiscoveryItem[] | null>(null);
  useEffect(() => {
    const kinds: Kind[] = ["movie", "series", "anime"];
    void Promise.all(kinds.map((k) => api.discover(k, "trending").then((l) => l.items, () => [])))
      .then((lists) => setItems(Array.from({ length: Math.max(...lists.map((l) => l.length)) }, (_, i) => lists.flatMap((l) => l[i] ?? []))
        .flat().filter((i) => !i.inLibrary).slice(0, 12)));
  }, []);
  const onAdded = useCallback((item: DiscoveryItem, id: number | null) => setItems((list) => list && markAdded(list, item, id)), []);
  return { items, onAdded };
}

export default function TodayPage() {
  const { user } = useSession();
  const { progress, subscribe, markRead } = useActivity();
  const summary = useSummary();
  const library = useLibrary();
  const forYou = useForYou();
  const feed = useActivityFeed({});
  const [announcement, setAnnouncement] = useState("");

  useEffect(() => { void markRead(); }, [markRead]);
  useEffect(() => subscribe((a) => { if (announced.has(a.kind)) setAnnouncement(`${a.title} ${eventLook[a.kind].label.toLowerCase()}`); }), [subscribe]);

  const downloads = useMemo(() => (summary.data?.inProgress ?? []).map((d) => {
    const live = progress.get(d.id);
    return live ? { ...d, progress: live.progress, downloadRate: live.rate, eta: live.eta } : d;
  }), [summary.data, progress]);

  // The newest import; a title merely added shows only while nothing has been imported.
  const newestOf = (kind: Activity["kind"]) => (feed.items ?? []).flatMap((a) => (a.kind === kind ? library.get(titleKey(a) ?? "") ?? [] : []));
  const imported = newestOf("Imported");
  const newest = imported.length > 0 ? imported : newestOf("Added");
  const justAdded = newest.find((i) => i.backdrop) ?? newest[0] ?? null;
  useChrome({ backdrop: justAdded?.backdrop });

  const counts = summary.data?.counts;
  const firstRun = counts !== undefined && counts.movies + counts.series + counts.anime === 0;
  const today = isoDay(new Date());
  const importedToday = (feed.items ?? []).filter((a) => a.kind === "Imported" && isoDay(new Date(a.time)) === today).length;
  const airingToday = (summary.data?.airing ?? []).filter((e) => e.airDate === today).length;

  const tiles = {
    justAdded: <JustAddedTile item={justAdded} loading={!feed.items} style={rise(0)} />,
    downloading: <DownloadingTile downloads={downloads} library={library} summary={summary} style={rise(1)} />,
    airing: <AiringTile summary={summary} style={rise(2)} />,
    attention: <AttentionTile summary={summary} library={library} style={rise(3)} />,
    library: <LibraryTile summary={summary} style={rise(4)} />,
    storage: <StorageTile summary={summary} style={rise(5)} />,
    forYou: <ForYouTile items={forYou.items} onAdded={forYou.onAdded} style={rise(6)} />,
  };

  return (
    <div className="space-y-6">
      <p aria-live="polite" className="sr-only">{announcement}</p>
      <header className="pt-2 md:pt-4">
        <p className="caption">{new Date().toLocaleDateString(undefined, { weekday: "long", day: "numeric", month: "long" })}</p>
        <h1 tabIndex={-1} className="mt-3 font-display text-[clamp(40px,6vw,64px)] leading-none outline-none">
          {greeting()}, <span className="capitalize">{user.username}</span>.
        </h1>
        <p className="mt-3 text-[15px] text-ink-2">
          {firstRun ? "Three steps and your library is ready." : <SummaryLine downloading={downloads.length} imported={importedToday} airing={airingToday} missing={counts?.missing ?? 0} />}
        </p>
      </header>

      {firstRun ? (
        <>
          <FirstRun />
          {tiles.forYou}
        </>
      ) : (
        <>
          {/* Phone: one column in reading order, the small tiles in a swipeable strip. */}
          <div className="space-y-4 md:hidden">
            {downloads.length > 0 && tiles.downloading}
            <div className="rail -mx-4 flex gap-3 overflow-x-auto scroll-px-4 px-4 pb-1">
              <AiringTile summary={summary} compact />
              <AttentionTile summary={summary} library={library} compact />
              <LibraryTile summary={summary} compact />
              <StorageTile summary={summary} compact />
            </div>
            <TimelineSection feed={feed} library={library} downloads={downloads} onChanged={summary.reload} limit={10} />
            {tiles.justAdded}
            {tiles.forYou}
          </div>

          <div className="space-y-6 max-md:hidden">
            <div className="grid auto-rows-[minmax(180px,auto)] grid-cols-2 gap-4 xl:grid-cols-4">
              {tiles.justAdded}
              {tiles.downloading}
              {tiles.airing}
              {tiles.attention}
            </div>
            <div className="grid gap-6 lg:grid-cols-12">
              <div className="min-w-0 lg:col-span-8">
                <TimelineSection feed={feed} library={library} downloads={downloads} onChanged={summary.reload} />
              </div>
              <aside className="grid min-w-0 grid-cols-1 content-start gap-4 max-lg:grid-cols-2 lg:col-span-4">
                {tiles.library}
                {tiles.storage}
                <div className="min-w-0 max-lg:col-span-2">{tiles.forYou}</div>
              </aside>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

function SummaryLine({ downloading, imported, airing, missing }: { downloading: number; imported: number; airing: number; missing: number }) {
  const parts: ReactNode[] = [
    downloading > 0 && <Link key="d" href="/downloads/" className="hover:text-ink hover:underline">{downloading} downloading</Link>,
    imported > 0 && <a key="i" href="#timeline" className="hover:text-ink hover:underline">{imported} imported</a>,
    airing > 0 && <Link key="a" href="/calendar/" className="hover:text-ink hover:underline">{airing} airing tonight</Link>,
    missing > 0 && <Link key="m" href="/movies/?status=missing" className="hover:text-ink hover:underline">{missing} missing</Link>,
  ].filter(Boolean);
  if (parts.length === 0) return <>All quiet. Everything you follow is up to date.</>;
  return <>{parts.flatMap((p, i) => (i === 0 ? [p] : [<span key={`s${i}`} aria-hidden="true" className="px-2 text-ink-3">·</span>, p]))}</>;
}

function JustAddedTile({ item, loading, style }: { item: LibraryItem | null; loading: boolean; style: CSSProperties }) {
  if (!item) {
    return (
      <Tile title="Just added" style={style} className="xl:col-span-2 xl:row-span-2">
        {loading ? <TileSkeleton /> : (
          <div className="flex flex-1 flex-col items-start justify-end gap-3">
            <p className="text-[13px] text-ink-2">Nothing in your library yet. Titles show up here as they arrive.</p>
            <Link href="/explore/" className="text-[13px] font-semibold hover:underline">Explore <span aria-hidden="true">→</span></Link>
          </div>
        )}
      </Tile>
    );
  }
  const kind = kindOf(item);
  return (
    <section aria-label="Just added" style={style} className="glass relative flex min-h-[320px] animate-rise flex-col justify-between overflow-hidden rounded-tile xl:col-span-2 xl:row-span-2">
      {(item.backdrop ?? item.poster) && (
        // eslint-disable-next-line @next/next/no-img-element
        <img src={item.backdrop ?? item.poster ?? ""} alt="" className="absolute inset-0 h-full w-full object-cover" />
      )}
      <span aria-hidden="true" className="absolute inset-0 bg-gradient-to-t from-black/85 via-black/20 to-black/35" />
      <h2 className="caption relative p-5 text-ink-2">Just added</h2>
      <div className="relative flex flex-wrap items-end justify-between gap-4 p-5">
        <div className="min-w-0">
          <p className="scrim-text line-clamp-2 text-balance font-display text-[clamp(32px,4vw,52px)] leading-none">{item.title}</p>
          <p className="scrim-text mt-2 text-[13px] text-ink-2">
            {[item.year > 0 && item.year, item.genres.slice(0, 2).join(", "), isSeries(item) && `${item.episodesWithFile}/${item.episodes} episodes`].filter(Boolean).join("   ")}
          </p>
        </div>
        <Link href={titleHref(kind, item.id)} className="inline-flex h-11 items-center rounded-pill bg-white px-5 text-sm font-semibold text-canvas transition hover:bg-white/85 active:scale-[.98]">Open</Link>
      </div>
    </section>
  );
}

function DownloadingTile({ downloads, library, summary, style }: { downloads: Download[]; library: Library; summary: Summary; style?: CSSProperties }) {
  const toast = useToast();
  const toggle = (d: Download) =>
    (d.status === "Paused" ? api.resume(d.id) : api.pause(d.id)).then(summary.reload, (e: unknown) =>
      toast({ key: `download:${d.id}`, icon: "alert", tone: "bad", title: "Could not change the download", message: errorMessage(e) }));
  return (
    <Tile title="Now downloading" link={{ href: "/downloads/", label: "Downloads" }} style={style} className="xl:col-span-2">
      {summary.error && !summary.data ? <TileError message={summary.error} onRetry={summary.reload} />
        : !summary.data ? <TileSkeleton />
        : downloads.length === 0 ? (
          <p className="flex flex-1 items-end text-[13px] text-ink-2">
            <span>Nothing downloading. <Link href="/explore/" className="font-semibold text-ink hover:underline">Explore <span aria-hidden="true">→</span></Link></span>
          </p>
        ) : (
          <ul className="space-y-2">
            {downloads.slice(0, 3).map((d) => {
              const item = library.get(d.movieId !== null ? `movie:${d.movieId}` : `series:${d.seriesId}`);
              const paused = d.status === "Paused";
              const eta = d.eta !== null && !paused ? formatEta(d.eta) : null;
              return (
                <li key={d.id} className="well flex items-center gap-3 rounded-card p-3">
                  {item?.poster
                    // eslint-disable-next-line @next/next/no-img-element
                    ? <img src={item.poster} alt="" className="h-[54px] w-9 shrink-0 rounded-[8px] object-cover" />
                    : <span className="grid h-[54px] w-9 shrink-0 place-items-center rounded-[8px] bg-fill text-live"><Icon name="download" size={16} /></span>}
                  <div className="min-w-0 flex-1 space-y-1.5">
                    <p className="truncate text-sm font-semibold" title={d.releaseTitle}>{item?.title ?? d.releaseTitle}</p>
                    <Progress value={d.progress ?? 0} eta={eta} tone="live" label={`${item?.title ?? d.releaseTitle} progress`} />
                    <p className="flex gap-3 text-[12px] text-ink-3 tabular-nums">
                      <span className="text-ink-2">{Math.round((d.progress ?? 0) * 100)}%</span>
                      {paused ? <span>Paused</span> : <><span>{formatSize(d.downloadRate)}/s</span>{eta && <span>{eta} left</span>}</>}
                    </p>
                  </div>
                  <button onClick={() => void toggle(d)} aria-label={paused ? `Resume ${item?.title ?? "download"}` : `Pause ${item?.title ?? "download"}`}
                    className="grid size-11 shrink-0 cursor-pointer place-items-center rounded-full bg-fill text-ink-2 hover:bg-fill-2 hover:text-ink">
                    <Icon name={paused ? "play" : "pause"} size={16} />
                  </button>
                </li>
              );
            })}
          </ul>
        )}
    </Tile>
  );
}

const compactTile = "h-44 min-h-0 w-40 shrink-0 gap-2 p-4";

function AiringTile({ summary, compact = false, style }: { summary: Summary; compact?: boolean; style?: CSSProperties }) {
  const today = isoDay(new Date());
  const [selected, setSelected] = useState(today);
  const week = currentWeek();
  const airing = summary.data?.airing ?? [];
  const onDay = airing.filter((e) => e.airDate === selected);

  if (compact) {
    return (
      <Tile title="Airing" className={compactTile}>
        {!summary.data ? <TileSkeleton /> : (
          <div className="flex flex-1 flex-col justify-end">
            <p className="font-display text-5xl leading-none tabular-nums">{airing.length}</p>
            <p className="mt-1 text-[13px] text-ink-2">{airing.length === 1 ? "episode" : "episodes"} this week</p>
          </div>
        )}
      </Tile>
    );
  }

  return (
    <Tile title="Airing this week" link={{ href: "/calendar/", label: "Calendar" }} style={style}>
      {summary.error && !summary.data ? <TileError message={summary.error} onRetry={summary.reload} /> : !summary.data ? <TileSkeleton /> : (
        <>
          <div role="group" aria-label="Day" className="grid grid-cols-7 gap-1">
            {week.map((d) => {
              const day = isoDay(d);
              const count = airing.filter((e) => e.airDate === day).length;
              return (
                <button key={day} onClick={() => setSelected(day)} aria-pressed={day === selected}
                  aria-label={`${d.toLocaleDateString(undefined, { weekday: "long" })}, ${count} ${count === 1 ? "episode" : "episodes"}`}
                  className={`flex min-h-11 cursor-pointer flex-col items-center gap-1 rounded-ctl py-1.5 text-[11px] font-semibold text-ink-3 transition hover:bg-fill aria-pressed:bg-fill-3 aria-pressed:text-ink ${day === today ? "text-ink" : ""}`}>
                  {d.toLocaleDateString(undefined, { weekday: "narrow" })}
                  <span className={`size-1.5 rounded-full ${count > 0 ? "bg-live" : "bg-fill-2"}`} />
                </button>
              );
            })}
          </div>
          {onDay.length === 0 ? (
            <p className="text-[13px] text-ink-2">Nothing airing {selected === today ? "today" : `on ${new Date(`${selected}T00:00`).toLocaleDateString(undefined, { weekday: "long" })}`}.</p>
          ) : (
            <ul className="space-y-1.5">
              {onDay.slice(0, 3).map((e) => <AiringRow key={e.id} episode={e} today={today} />)}
              {onDay.length > 3 && <li className="text-[12px] text-ink-3">and {onDay.length - 3} more</li>}
            </ul>
          )}
        </>
      )}
    </Tile>
  );
}

function AiringRow({ episode: e, today }: { episode: AiringEpisode; today: string }) {
  const [mark, label, tone] = e.hasFile ? ["✓", "On disk", "text-ok"] : e.airDate > today ? ["○", "Upcoming", "text-ink-3"] : e.monitored ? ["!", "Missing", "text-warn"] : ["○", "Not monitored", "text-ink-3"];
  return (
    <li>
      <Link href={`${titleHref("series", e.seriesId)}&season=${e.seasonNumber}`} className="flex items-center gap-2.5 rounded-ctl py-1 hover:bg-fill">
        <span className={`w-4 text-center text-[13px] font-bold ${tone}`} aria-hidden="true">{mark}</span>
        <span className="min-w-0 flex-1 truncate text-[13px]"><span className="font-semibold">{e.seriesTitle}</span> <span className="text-ink-3 tabular-nums">{episodeCode(e)}</span></span>
        <span className="sr-only">{label}</span>
      </Link>
    </li>
  );
}

function AttentionTile({ summary, library, compact = false, style }: { summary: Summary; library: Library; compact?: boolean; style?: CSSProperties }) {
  const nothingFound = [...library.values()].filter((i) => i.monitored && !i.hasFile && i.lastSearch?.result === "Nothing grabbed").length;
  const subs = summary.data ? summary.data.subtitles.total - summary.data.subtitles.covered : 0;
  const rows = [
    { label: "Missing", value: summary.data?.counts.missing ?? 0, href: "/movies/?status=missing" },
    { label: "Nothing found", value: nothingFound, href: "/movies/?status=missing" },
    { label: "Missing subtitles", value: subs, href: "/movies/?status=complete" },
  ];
  const total = rows.reduce((n, r) => n + r.value, 0);

  if (compact) {
    return (
      <Tile title="Attention" className={compactTile}>
        {!summary.data ? <TileSkeleton /> : (
          <div className="flex flex-1 flex-col justify-end">
            <p className={`font-display text-5xl leading-none tabular-nums ${total > 0 ? "text-warn" : ""}`}>{total}</p>
            <p className="mt-1 text-[13px] text-ink-2">{total === 0 ? "All good" : "need a look"}</p>
          </div>
        )}
      </Tile>
    );
  }

  return (
    <Tile title="Needs attention" style={style}>
      {summary.error && !summary.data ? <TileError message={summary.error} onRetry={summary.reload} /> : !summary.data ? <TileSkeleton /> : (
        <ul className="grid flex-1 grid-cols-3 gap-2">
          {rows.map((r) => (
            <li key={r.label}>
              <Link href={r.href} className="well flex h-full min-h-24 flex-col justify-between rounded-card p-3 transition hover:bg-fill">
                <span className={`font-display text-5xl leading-none tabular-nums ${r.value > 0 ? "text-warn" : "text-ink-3"}`}>{r.value}</span>
                <span className="text-[12px] leading-tight font-medium text-ink-2">{r.label}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </Tile>
  );
}

function LibraryTile({ summary, compact = false, style }: { summary: Summary; compact?: boolean; style?: CSSProperties }) {
  const counts = summary.data?.counts;
  const total = counts ? counts.movies + counts.series + counts.anime : 0;
  if (compact) {
    return (
      <Tile title="Library" className={compactTile}>
        {!counts ? <TileSkeleton /> : (
          <div className="flex flex-1 flex-col justify-end">
            <p className="font-display text-5xl leading-none tabular-nums">{total}</p>
            <p className="mt-1 text-[13px] text-ink-2">{total === 1 ? "title" : "titles"}</p>
          </div>
        )}
      </Tile>
    );
  }
  const rows: [Kind, string, number][] = counts ? [["movie", "Movies", counts.movies], ["series", "Series", counts.series], ["anime", "Anime", counts.anime]] : [];
  return (
    <Tile title="Library" style={style}>
      {summary.error && !counts ? <TileError message={summary.error} onRetry={summary.reload} /> : !counts ? <TileSkeleton /> : (
        <>
          <p className="flex items-baseline gap-2"><span className="font-display text-5xl leading-none tabular-nums">{total}</span><span className="text-[13px] text-ink-2">titles</span></p>
          <ul className="space-y-1">
            {rows.map(([kind, label, value]) => (
              <li key={kind}>
                <Link href={libraryHref[kind]} className="-mx-2 flex min-h-9 items-center justify-between rounded-ctl px-2 text-sm hover:bg-fill">
                  <span className="text-ink-2">{label}</span><span className="font-semibold tabular-nums">{value}</span>
                </Link>
              </li>
            ))}
          </ul>
          <p className="text-[12px] text-ink-3 tabular-nums">{counts.episodesWithFile} of {counts.episodes} episodes on disk</p>
        </>
      )}
    </Tile>
  );
}

function Ring({ value, size }: { value: number; size: number }) {
  const radius = 42;
  const length = 2 * Math.PI * radius;
  return (
    <svg viewBox="0 0 100 100" width={size} height={size} aria-hidden="true" className="-rotate-90">
      <circle cx="50" cy="50" r={radius} fill="none" stroke="rgb(255 255 255 / .14)" strokeWidth="10" />
      <circle cx="50" cy="50" r={radius} fill="none" stroke={value > 0.9 ? "var(--color-warn)" : "#fff"} strokeWidth="10" strokeLinecap="round"
        strokeDasharray={`${length * value} ${length}`} />
    </svg>
  );
}

function StorageTile({ summary, compact = false, style }: { summary: Summary; compact?: boolean; style?: CSSProperties }) {
  const data = summary.data;
  const used = data && data.storage.totalBytes > 0 ? data.storage.usedBytes / data.storage.totalBytes : null;
  const coverage = data && data.subtitles.total > 0 ? Math.round((data.subtitles.covered / data.subtitles.total) * 100) : null;
  const ring = used !== null && (
    <div className="relative grid shrink-0 place-items-center">
      <Ring value={used} size={compact ? 76 : 104} />
      <span className="absolute text-center text-[13px] font-semibold tabular-nums">{Math.round(used * 100)}%<span className="sr-only"> of disk space used</span></span>
    </div>
  );
  if (compact) {
    return (
      <Tile title="Storage" className={compactTile}>
        {!data ? <TileSkeleton /> : used === null ? <p className="text-[13px] text-ink-2">Disk size unknown.</p> : (
          <div className="flex flex-1 flex-col items-start justify-end gap-1">{ring}<p className="text-[12px] text-ink-3 tabular-nums">{formatSize(data.storage.freeBytes)} free</p></div>
        )}
      </Tile>
    );
  }
  return (
    <Tile title="Storage" style={style}>
      {summary.error && !data ? <TileError message={summary.error} onRetry={summary.reload} /> : !data ? <TileSkeleton /> : (
        <>
          {used === null ? <p className="text-[13px] text-ink-2">The library disks could not be read.</p> : (
            <div className="flex items-center gap-5">
              {ring}
              <dl className="min-w-0 space-y-1.5 text-[13px] tabular-nums">
                <div><dt className="inline text-ink-3">Used </dt><dd className="inline font-semibold">{formatSize(data.storage.usedBytes)}</dd></div>
                <div><dt className="inline text-ink-3">Free </dt><dd className="inline font-semibold">{formatSize(data.storage.freeBytes)}</dd></div>
                <div><dt className="inline text-ink-3">Library </dt><dd className="inline font-semibold">{formatSize(data.storage.libraryBytes)}</dd></div>
              </dl>
            </div>
          )}
          <p className="flex items-center gap-2 border-t border-hair-2 pt-3 text-[13px] text-ink-2">
            <Icon name="subtitles" size={15} className="text-subs" />
            {coverage === null ? "No subtitles wanted yet." : <span>Subtitles cover <span className="font-semibold text-ink tabular-nums">{coverage}%</span> of files</span>}
          </p>
        </>
      )}
    </Tile>
  );
}

function ForYouTile({ items, onAdded, style }: { items: DiscoveryItem[] | null; onAdded: (item: DiscoveryItem, id: number | null) => void; style?: CSSProperties }) {
  return (
    <Tile title="For you" link={{ href: "/explore/", label: "Explore" }} style={style} className="min-h-0">
      {items && items.length === 0 ? <p className="text-[13px] text-ink-2">Recommendations are unavailable right now. Search still works.</p> : (
        <div className="rail -mx-5 flex gap-3 overflow-x-auto scroll-px-5 px-5">
          {items
            ? items.map((item) => <PosterCard key={`${item.kind}${item.tmdbId}${item.tvdbId}`} item={item} onAdded={onAdded} className="w-[118px] shrink-0" />)
            : Array.from({ length: 6 }, (_, i) => <PosterSkeleton key={i} className="w-[118px] shrink-0" />)}
        </div>
      )}
    </Tile>
  );
}

function TimelineSection({ feed, library, downloads, onChanged, limit }: {
  feed: ReturnType<typeof useActivityFeed>;
  library: Library;
  downloads: Download[];
  onChanged: () => void;
  limit?: number;
}) {
  const toast = useToast();
  const [filter, setFilter] = useState<Filter>("all");
  const [all, setAll] = useState(limit === undefined);
  const [subtitles, setSubtitles] = useState<{ movieId: number; title: string } | null>(null);
  const match = filters.find((f) => f.value === filter)?.match ?? (() => true);
  const groups = groupEvents((feed.items ?? []).filter(match));
  const shown = all ? groups : groups.slice(0, limit);

  const run = (label: string, action: Promise<void>) =>
    action.then(onChanged, (e: unknown) => toast({ key: label, icon: "alert", tone: "bad", title: label, message: errorMessage(e) }));

  const search = (item: LibraryItem) => run(`Search ${item.title}`, api.autoSearch(kindOf(item), item.id).then(() =>
    toast({ key: libraryKey(item), icon: "search", tone: "seek", title: item.title, message: "Searching for releases…" })));

  // Exactly one action per row; anything whose title already has a file only opens.
  function actionOf(a: Activity): ReactNode {
    const item = library.get(titleKey(a) ?? "");
    const href = activityHref(a);
    const download = downloads.find((d) => d.id === a.downloadId);
    const open = href && <Link href={href} className={rowButton}>Open</Link>;
    if (a.kind === "DownloadStarted" && download) {
      const paused = download.status === "Paused";
      return <button className={rowButton} onClick={() => void run("Download", paused ? api.resume(download.id) : api.pause(download.id))}>{paused ? "Resume" : "Pause"}</button>;
    }
    if (a.kind === "Grabbed" || a.kind === "Regrabbed" || a.kind === "SearchCompleted") return <Link href="/downloads/" className={rowButton}>View</Link>;
    if (a.kind === "ImportFailed" && download?.state === "ImportBlocked") {
      return <button className={rowButton} onClick={() => void run("Retry import", api.retryImport(download.id))}>Retry import</button>;
    }
    // Subtitles are not a release: a title with a file still offers them.
    if (a.kind === "SubtitlesMissing" && item) {
      return isSeries(item) ? <Link href={titleHref(kindOf(item), item.id)} className={rowButton}>Find subtitles</Link>
        : <button className={rowButton} onClick={() => setSubtitles({ movieId: item.id, title: item.title })}>Find subtitles</button>;
    }
    if (!item || item.hasFile || a.kind === "Imported") return open;
    if (a.kind === "NothingFound") return <button className={rowButton} onClick={() => void search(item)}>Search again</button>;
    if (a.kind === "DownloadFailed" || a.kind === "ImportFailed" || a.kind === "SearchFailed") return <button className={rowButton} onClick={() => void search(item)}>Retry</button>;
    return open;
  }

  return (
    <section id="timeline" aria-labelledby="timeline-heading" className="scroll-mt-24">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2 id="timeline-heading" className="font-display text-[28px] leading-none">Timeline</h2>
        <div role="group" aria-label="Show" className="rail -mx-4 flex gap-2 overflow-x-auto px-4 sm:mx-0 sm:px-0">
          {filters.map((f) => <Chip key={f.value} selected={filter === f.value} onClick={() => setFilter(f.value)}>{f.label}</Chip>)}
        </div>
      </div>
      {feed.error && !feed.items ? (
        <div className="glass rounded-tile p-5"><TileError message={feed.error} onRetry={feed.reload} /></div>
      ) : !feed.items ? <TimelineSkeleton /> : shown.length === 0 ? (
        <p className="well rounded-[20px] px-5 py-8 text-center text-[13px] text-ink-2">
          {filter === "all" ? "Nothing has happened yet." : "Nothing here yet."}
        </p>
      ) : (
        <>
          <Timeline groups={shown} stickyTop="top-[calc(64px+env(safe-area-inset-top))] md:top-[84px]"
            posterOf={(a) => library.get(titleKey(a) ?? "")?.poster ?? null} actionOf={actionOf} />
          {!all && groups.length > shown.length
            ? <div className="flex justify-center pt-4"><Button variant="glass" size="sm" onClick={() => setAll(true)}>See all</Button></div>
            : feed.more && <LoadMore onMore={feed.loadMore} />}
        </>
      )}
      {subtitles && <SubtitlesDialog target={{ movieId: subtitles.movieId }} title={subtitles.title} onClose={() => setSubtitles(null)} />}
    </section>
  );
}

const rowButton = "glass inline-flex h-9 cursor-pointer items-center rounded-pill px-4 text-[13px] font-semibold whitespace-nowrap text-ink transition hover:bg-fill-2 active:scale-[.98]";

// The first visit: three steps that tick themselves off from the server's real state.
function FirstRun() {
  const { user } = useSession();
  const [paths, setPaths] = useState<Paths | null>(null);
  useEffect(() => { api.paths().then(setPaths).catch(() => undefined); }, []);
  const steps = [
    { done: !!paths && !!paths.movies && !!paths.series && !!paths.anime && !!paths.downloads, title: "Choose your folders", text: "Where your library lives and where downloads land.", href: "/settings/#folders", action: "Folders" },
    ...(user.role === "Admin" ? [{ done: false, title: "Import your library", text: "Bring in the movies and shows already on your disks.", href: "/import/", action: "Import existing" }] : []),
    { done: false, title: "Set defaults, then add titles", text: "Pick subtitle languages per category, then find something to watch.", href: "/settings/#subtitles", action: "Subtitles" },
  ];
  return (
    <section aria-labelledby="first-run" className="space-y-4">
      <h2 id="first-run" className="font-display text-[28px] leading-none">Get set up</h2>
      <ol className="grid gap-4 md:grid-cols-3">
        {steps.map((s, i) => (
          <li key={s.title} style={rise(i)} className="glass flex animate-rise flex-col gap-3 rounded-tile p-5">
            <span className={`grid size-9 place-items-center rounded-full text-[13px] font-bold ${s.done ? "bg-ok text-canvas" : "bg-fill-2"}`}>
              {s.done ? <><Icon name="check" size={16} strokeWidth={3} /><span className="sr-only">Done</span></> : i + 1}
            </span>
            <h3 className="text-[17px] font-semibold">{s.title}</h3>
            <p className="flex-1 text-[13px] text-ink-2">{s.text}</p>
            <div className="flex flex-wrap gap-2">
              <Link href={s.href} className={rowButton}>{s.action}</Link>
              {i === steps.length - 1 && <Link href="/explore/" className={rowButton}>Explore</Link>}
            </div>
          </li>
        ))}
      </ol>
    </section>
  );
}
