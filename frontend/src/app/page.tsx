"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import type { CSSProperties, ReactNode } from "react";
import { api, currentWeek, episodeCode, errorMessage, formatEta, formatSize, isoDay, isSeries, kindOf, titleHref } from "@/lib/api";
import type { Activity, AiringEpisode, Dashboard, DiscoveryItem, Download, Kind, LibraryItem, Paths } from "@/lib/api";
import { day as formatDay, weekday } from "@/lib/format";
import { activityHref, eventLook, titleKey, useActivity, useActivityFeed, useActivityRefresh } from "@/components/Activity";
import { groupEvents, LoadMore, Timeline, TimelineSkeleton } from "@/components/ActivityFeed";
import { markAdded } from "@/components/AddDialog";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { PosterCard, PosterSkeleton } from "@/components/Poster";
import { useChrome } from "@/components/Shell";
import { SubtitlesDialog } from "@/components/SubtitlesDialog";
import { useToast } from "@/components/Toasts";
import { Button, ButtonLink, IconButton, Progress, Tile, TileError, TileSkeleton, Well, WellList } from "@/components/ui";

type Summary = { data: Dashboard | null; error: string | null; reload: () => void };
type Library = ReadonlyMap<string, LibraryItem>;

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
function useForYou(enabled: boolean) {
  const [items, setItems] = useState<DiscoveryItem[] | null>(null);
  useEffect(() => {
    if (!enabled) return;
    const kinds: Kind[] = ["movie", "series", "anime"];
    void Promise.all(kinds.map((k) => api.discover(k, "trending").then((l) => l.items, () => [])))
      .then((lists) => setItems(Array.from({ length: Math.max(...lists.map((l) => l.length)) }, (_, i) => lists.flatMap((l) => l[i] ?? []))
        .flat().filter((i) => !i.inLibrary).slice(0, 12)));
  }, [enabled]);
  const onAdded = useCallback((item: DiscoveryItem, id: number | null) => setItems((list) => list && markAdded(list, item, id)), []);
  return { items, onAdded };
}

export default function DashboardPage() {
  const { user } = useSession();
  const admin = user.role === "Admin";
  const { progress, subscribe, markRead } = useActivity();
  const summary = useSummary();
  const library = useLibrary();
  const forYou = useForYou(admin);
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
  const [setUp, setSetUp] = useState(() => remembered(SUBTITLES_CHOSEN) && remembered(IMPORT_SKIPPED));
  // Members cannot set anything up, so an empty library only tells them so.
  const firstRun = (!admin || !setUp) && counts !== undefined && counts.movies + counts.series + counts.anime === 0;
  const today = isoDay(new Date());
  const importedToday = (feed.items ?? []).filter((a) => a.kind === "Imported" && isoDay(new Date(a.time)) === today).length;
  const airingToday = (summary.data?.airing ?? []).filter((e) => e.airDate === today).length;

  const tiles = {
    justAdded: <JustAddedTile item={justAdded} loading={!feed.items} style={rise(0)} />,
    downloading: <DownloadingTile downloads={downloads} library={library} summary={summary} style={rise(1)} />,
    airing: <AiringTile summary={summary} style={rise(2)} />,
    storage: <StorageTile summary={summary} style={rise(3)} />,
    forYou: admin && <ForYouTile items={forYou.items} onAdded={forYou.onAdded} style={rise(4)} />,
  };

  return (
    <div className="space-y-6">
      <p aria-live="polite" className="sr-only">{announcement}</p>
      <header className="pt-2 md:pt-4">
        <h1 tabIndex={-1} className="font-display text-[clamp(40px,6vw,64px)] leading-none outline-none">
          {greeting()}, <span className="capitalize">{user.username}</span>.
        </h1>
        <p className="mt-3 text-body text-ink-2">
          {firstRun ? (admin ? "Two steps and your library is ready." : "Your admin hasn't added anything yet.")
            : <SummaryLine downloading={downloads.length} imported={importedToday} airing={airingToday} movies={counts?.missingMovies ?? 0} episodes={counts?.missingEpisodes ?? 0} />}
        </p>
      </header>

      {firstRun ? admin && (
        <>
          <FirstRun onComplete={() => setSetUp(true)} />
          {tiles.forYou}
        </>
      ) : (
        <>
          {/* Phone: one column in reading order, the small tiles in a swipeable strip. */}
          <div className="space-y-4 md:hidden">
            {downloads.length > 0 && tiles.downloading}
            <div className="rail -mx-4 flex gap-3 overflow-x-auto scroll-px-4 px-4 pb-1">
              <AiringTile summary={summary} compact />
              <StorageTile summary={summary} compact />
            </div>
            {tiles.forYou}
            <TimelineSection feed={feed} library={library} downloads={downloads} onChanged={summary.reload} limit={10} />
            {tiles.justAdded}
          </div>

          <div className="space-y-6 max-md:hidden">
            <div className="grid auto-rows-[minmax(180px,auto)] grid-cols-2 gap-4 xl:grid-cols-4">
              {tiles.justAdded}
              {tiles.downloading}
              {tiles.airing}
              {tiles.storage}
            </div>
            {tiles.forYou}
            <TimelineSection feed={feed} library={library} downloads={downloads} onChanged={summary.reload} />
          </div>
        </>
      )}
    </div>
  );
}

// Downloads are left out: the Now downloading tile right below already lists them.
function SummaryLine({ downloading, imported, airing, movies, episodes }: { downloading: number; imported: number; airing: number; movies: number; episodes: number }) {
  const missing = [
    movies > 0 && <Link key="mm" href="/movies/?status=missing" className="hover:underline">{movies} {movies === 1 ? "movie" : "movies"}</Link>,
    episodes > 0 && <Link key="me" href="/series/?status=missing" className="hover:underline">{episodes} {episodes === 1 ? "episode" : "episodes"}</Link>,
  ].filter(Boolean);
  const parts: ReactNode[] = [
    imported > 0 && <a key="i" href="#timeline" className="hover:text-ink hover:underline">{imported} imported</a>,
    airing > 0 && <Link key="a" href="/calendar/" className="hover:text-ink hover:underline">{airing} airing tonight</Link>,
    missing.length > 0 && <span key="m" className="text-warn">{missing.flatMap((p, i) => (i === 0 ? [p] : [" · ", p]))} missing</span>,
  ].filter(Boolean);
  if (parts.length === 0) return downloading > 0 ? null : <>All quiet. Everything you follow is up to date.</>;
  return <>{parts.flatMap((p, i) => (i === 0 ? [p] : [<span key={`s${i}`} aria-hidden="true" className="px-2 text-ink-3">·</span>, p]))}</>;
}

function JustAddedTile({ item, loading, style }: { item: LibraryItem | null; loading: boolean; style: CSSProperties }) {
  const admin = useSession().user.role === "Admin";
  if (!item) {
    return (
      <Tile title="Just added" style={style} className="xl:col-span-2 xl:row-span-2">
        {loading ? <TileSkeleton /> : (
          <div className="flex flex-1 flex-col items-start justify-end gap-3">
            <p className="text-meta text-ink-2">Nothing in your library yet. Titles show up here as they arrive.</p>
            {admin && <ButtonLink href="/explore/" size="sm">Explore</ButtonLink>}
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
      <span aria-hidden="true" className="absolute inset-0 scrim-b" />
      <h2 className="caption relative p-5 text-ink-2">Just added</h2>
      <div className="relative flex flex-wrap items-end justify-between gap-4 p-5">
        <div className="min-w-0">
          <p className="scrim-text line-clamp-2 text-balance font-display text-[clamp(32px,4vw,52px)] leading-none">{item.title}</p>
          <p className="scrim-text mt-2 text-meta text-ink-2">
            {[item.year > 0 && item.year, item.genres.slice(0, 2).join(", "), isSeries(item) && `${item.episodesWithFile}/${item.episodes} episodes`].filter(Boolean).join("   ")}
          </p>
        </div>
        <ButtonLink href={titleHref(kind, item.id)} variant="primary">Open</ButtonLink>
      </div>
    </section>
  );
}

function DownloadingTile({ downloads, library, summary, style }: { downloads: Download[]; library: Library; summary: Summary; style?: CSSProperties }) {
  const toast = useToast();
  const admin = useSession().user.role === "Admin";
  const toggle = (d: Download) =>
    (d.status === "Paused" ? api.resume(d.id) : api.pause(d.id)).then(summary.reload, (e: unknown) =>
      toast({ key: titleKey(d) ?? `download:${d.id}`, icon: "alert", tone: "bad", title: "Could not change the download", message: errorMessage(e) }));
  return (
    <Tile title="Now downloading" link={{ href: "/downloads/", label: "Downloads" }} style={style} className="xl:col-span-2">
      {summary.error && !summary.data ? <TileError message={summary.error} onRetry={summary.reload} />
        : !summary.data ? <TileSkeleton />
        : downloads.length === 0 ? (
          <p className="flex flex-1 items-end text-meta text-ink-2">Nothing downloading.</p>
        ) : (
          <WellList>
            {downloads.slice(0, 3).map((d) => {
              const item = library.get(d.movieId !== null ? `movie:${d.movieId}` : `series:${d.seriesId}`);
              const paused = d.status === "Paused";
              const eta = d.eta !== null && !paused ? formatEta(d.eta) : null;
              return (
                <li key={d.id} className="flex items-center gap-3 p-3">
                  {item?.poster
                    // eslint-disable-next-line @next/next/no-img-element
                    ? <img src={item.poster} alt="" className="h-[54px] w-9 shrink-0 rounded-thumb object-cover" />
                    : <span className="grid h-[54px] w-9 shrink-0 place-items-center rounded-thumb bg-fill text-live"><Icon name="download" size={16} /></span>}
                  <div className="min-w-0 flex-1 space-y-1.5">
                    <p className="truncate text-body font-semibold" title={d.releaseTitle}>{item?.title ?? d.releaseTitle}</p>
                    <Progress value={d.progress ?? 0} eta={eta} tone="live" label={`${item?.title ?? d.releaseTitle} progress`} />
                    <p className="flex gap-3 text-meta text-ink-3 tabular-nums">
                      <span className="text-ink-2">{Math.round((d.progress ?? 0) * 100)}%</span>
                      {paused ? <span>Paused</span> : <><span>{formatSize(d.downloadRate)}/s</span>{eta && <span>{eta} left</span>}</>}
                    </p>
                  </div>
                  {admin && (
                    <IconButton variant="ghost" icon={paused ? "play" : "pause"} onClick={() => void toggle(d)}
                      label={paused ? `Resume ${item?.title ?? "download"}` : `Pause ${item?.title ?? "download"}`} />
                  )}
                </li>
              );
            })}
          </WellList>
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
            <p className="mt-1 text-meta text-ink-2">{airing.length === 1 ? "episode" : "episodes"} this week</p>
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
                  aria-label={`${weekday(d)}, ${formatDay(d)}, ${count} ${count === 1 ? "episode" : "episodes"}`}
                  className={`flex min-h-11 cursor-pointer flex-col items-center gap-0.5 rounded-ctl py-1.5 text-meta font-semibold text-ink-3 transition hover:bg-fill aria-pressed:bg-fill-3 aria-pressed:text-ink ${day === today ? "text-ink" : ""}`}>
                  <span>{weekday(d, "short")}</span>
                  <span className="tabular-nums">{d.getDate()}</span>
                  <span className={`size-1.5 rounded-full ${count > 0 ? "bg-ink-2" : "bg-fill-2"}`} />
                </button>
              );
            })}
          </div>
          {onDay.length === 0 ? (
            <p className="text-meta text-ink-2">Nothing airing {selected === today ? "today" : `on ${weekday(`${selected}T00:00`)}`}.</p>
          ) : (
            <ul className="space-y-1.5">
              {onDay.slice(0, 3).map((e) => <AiringRow key={e.id} episode={e} today={today} />)}
              {onDay.length > 3 && <li className="text-meta text-ink-3">and {onDay.length - 3} more</li>}
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
        <span className={`w-4 text-center text-meta font-bold ${tone}`} aria-hidden="true">{mark}</span>
        <span className="min-w-0 flex-1 truncate text-meta"><span className="font-semibold">{e.seriesTitle}</span> <span className="text-ink-3 tabular-nums">{episodeCode(e)}</span></span>
        <span className="sr-only">{label}</span>
      </Link>
    </li>
  );
}

function Ring({ value, size }: { value: number; size: number }) {
  const radius = 42;
  const length = 2 * Math.PI * radius;
  return (
    <svg viewBox="0 0 100 100" width={size} height={size} aria-hidden="true" className="-rotate-90">
      <circle cx="50" cy="50" r={radius} fill="none" stroke="var(--color-fill-3)" strokeWidth="10" />
      <circle cx="50" cy="50" r={radius} fill="none" stroke={value > 0.9 ? "var(--color-warn)" : "var(--color-ink)"} strokeWidth="10" strokeLinecap="round"
        strokeDasharray={`${length * value} ${length}`} />
    </svg>
  );
}

function StorageTile({ summary, compact = false, style }: { summary: Summary; compact?: boolean; style?: CSSProperties }) {
  const data = summary.data;
  const used = data && data.storage.totalBytes > 0 ? data.storage.usedBytes / data.storage.totalBytes : null;
  return (
    <Tile title="Storage" style={style} className={compact ? compactTile : ""}>
      {summary.error && !data && !compact ? <TileError message={summary.error} onRetry={summary.reload} /> : !data ? <TileSkeleton />
        : used === null ? <p className="text-meta text-ink-2">Disk size unknown.</p> : (
          <div className="flex flex-1 flex-col items-start justify-end gap-2">
            <div className="relative grid shrink-0 place-items-center">
              <Ring value={used} size={compact ? 76 : 104} />
              <span className="absolute text-center text-meta font-semibold tabular-nums">{Math.round(used * 100)}%<span className="sr-only"> of disk space used</span></span>
            </div>
            <p className="text-meta text-ink-2 tabular-nums">{formatSize(data.storage.freeBytes)} free</p>
          </div>
        )}
    </Tile>
  );
}

function ForYouTile({ items, onAdded, style }: { items: DiscoveryItem[] | null; onAdded: (item: DiscoveryItem, id: number | null) => void; style?: CSSProperties }) {
  return (
    <Tile title="For you" link={{ href: "/explore/", label: "Explore" }} style={style} className="min-h-0">
      {items && items.length === 0 ? <p className="text-meta text-ink-2">Recommendations are unavailable right now. Search still works.</p> : (
        <div className="rail -mx-5 flex gap-3 overflow-x-auto scroll-px-5 px-5">
          {items
            ? items.map((item) => <PosterCard key={`${item.kind}${item.tmdbId}${item.tvdbId}`} item={item} onAdded={onAdded} className="w-[128px] shrink-0 md:w-[148px]" />)
            : Array.from({ length: 8 }, (_, i) => <PosterSkeleton key={i} className="w-[128px] shrink-0 md:w-[148px]" />)}
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
  const admin = useSession().user.role === "Admin";
  const [all, setAll] = useState(limit === undefined);
  const [subtitles, setSubtitles] = useState<{ movieId: number; title: string } | null>(null);
  const groups = groupEvents(feed.items ?? []);
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
    const open = href && <ButtonLink href={href} size="sm">Open</ButtonLink>;
    if (!admin) return open;
    if (a.kind === "DownloadStarted" && download) {
      const paused = download.status === "Paused";
      return <Button size="sm" onClick={() => void run("Download", paused ? api.resume(download.id) : api.pause(download.id))}>{paused ? "Resume" : "Pause"}</Button>;
    }
    if (a.kind === "Grabbed" || a.kind === "Regrabbed" || a.kind === "SearchCompleted") return <ButtonLink href="/downloads/" size="sm">View</ButtonLink>;
    if (a.kind === "ImportFailed" && download?.state === "ImportBlocked") {
      return <Button size="sm" onClick={() => void run("Retry import", api.retryImport(download.id))}>Retry import</Button>;
    }
    // Subtitles are not a release: a title with a file still offers them.
    if (a.kind === "SubtitlesMissing" && item) {
      return isSeries(item) ? <ButtonLink href={titleHref(kindOf(item), item.id)} size="sm">Find subtitles</ButtonLink>
        : <Button size="sm" onClick={() => setSubtitles({ movieId: item.id, title: item.title })}>Find subtitles</Button>;
    }
    if (!item || item.hasFile || a.kind === "Imported") return open;
    if (a.kind === "NothingFound") return <Button size="sm" onClick={() => void search(item)}>Search again</Button>;
    if (a.kind === "DownloadFailed" || a.kind === "ImportFailed" || a.kind === "SearchFailed") return <Button size="sm" onClick={() => void search(item)}>Retry</Button>;
    return open;
  }

  return (
    <section id="timeline" aria-labelledby="timeline-heading" className="scroll-mt-24">
      <h2 id="timeline-heading" className="mb-4 font-display text-[28px] leading-none">Timeline</h2>
      {feed.error && !feed.items ? (
        <div className="glass rounded-tile p-5"><TileError message={feed.error} onRetry={feed.reload} /></div>
      ) : !feed.items ? <TimelineSkeleton /> : shown.length === 0 ? (
        <Well className="text-meta text-ink-2">Nothing has happened yet.</Well>
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

// The first visit: three steps that tick themselves off from the server's real state.
// Remembered in this browser: the setup guide is a hint, it goes away once every step was done or, for the import, put off here.
// An import that adds titles ends it too, since the guide only shows while the library is empty.
const SUBTITLES_CHOSEN = "modularr:setup-subtitles";
const IMPORT_SKIPPED = "modularr:setup-import-skipped";

function remembered(key: string): boolean {
  try {
    return localStorage.getItem(key) === "1";
  } catch {
    return false;
  }
}

function remember(key: string) {
  try {
    localStorage.setItem(key, "1");
  } catch {
    // Private mode: the step just shows again next time.
  }
}

function FirstRun({ onComplete }: { onComplete: () => void }) {
  const [paths, setPaths] = useState<Paths | null>(null);
  const [subtitles, setSubtitles] = useState(() => remembered(SUBTITLES_CHOSEN));
  const [importSkipped, setImportSkipped] = useState(() => remembered(IMPORT_SKIPPED));
  useEffect(() => { api.paths().then(setPaths).catch(() => undefined); }, []);
  const folders = !!paths && !!paths.movies && !!paths.series && !!paths.anime && !!paths.downloads;
  useEffect(() => { if (folders && subtitles && importSkipped) onComplete(); }, [folders, subtitles, importSkipped, onComplete]);
  const chooseSubtitles = () => { remember(SUBTITLES_CHOSEN); setSubtitles(true); };
  const skipImport = () => { remember(IMPORT_SKIPPED); setImportSkipped(true); };
  const steps = [
    { done: folders, title: "Choose your folders", text: "Where your library lives and where downloads land.", href: "/settings/#library", action: "Folders" },
    { done: subtitles, title: "Choose subtitles", text: "Which subtitle languages to fetch for movies, series and anime.", href: "/settings/#languages", action: "Subtitles", onClick: chooseSubtitles },
    { done: importSkipped, title: "Import your library", text: "Bring in the movies and shows already on your disks.", href: "/import/", action: "Import existing", optional: true },
  ];
  return (
    <section aria-labelledby="first-run" className="space-y-4">
      <h2 id="first-run" className="font-display text-[28px] leading-none">Get set up</h2>
      <ol className="grid gap-4 md:grid-cols-3">
        {steps.map((s, i) => (
          <li key={s.title} style={rise(i)} className="glass flex animate-rise flex-col gap-3 rounded-tile p-5">
            <span className={`grid size-9 place-items-center rounded-full text-meta font-bold ${s.done ? "bg-ok text-canvas" : "bg-fill-2"}`}>
              {s.done ? <><Icon name="check" size={16} strokeWidth={3} /><span className="sr-only">Done</span></> : "optional" in s ? <Icon name="plus" size={16} /> : i + 1}
            </span>
            {"optional" in s && <span className="caption -mb-2">Optional</span>}
            <h3 className="text-title font-semibold">{s.title}</h3>
            <p className="flex-1 text-meta text-ink-2">{s.text}</p>
            <div className="flex flex-wrap gap-2">
              <ButtonLink href={s.href} size="sm" onClick={"onClick" in s ? s.onClick : undefined}>{s.action}</ButtonLink>
              {"optional" in s && !s.done && <Button type="button" size="sm" variant="ghost" onClick={skipImport}>Not now</Button>}
            </div>
          </li>
        ))}
      </ol>
    </section>
  );
}
