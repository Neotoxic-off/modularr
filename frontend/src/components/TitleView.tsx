"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { api, episodeCode, errorMessage, formatEta, formatRuntime, formatSize, isSeries, isoDay, kindOf, libraryHref } from "@/lib/api";
import type { Download, Episode, ItemPatch, Kind, LibraryItem, MovieFile, Season, SeriesDetail, SubtitleTarget } from "@/lib/api";
import { realLanguages, useOptions } from "@/lib/options";
import { isActive, titleKey, useActivity, useTitleEvents } from "@/components/Activity";
import { ActivityTimeline } from "@/components/ActivityFeed";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { PosterArt } from "@/components/Poster";
import { useChrome } from "@/components/Shell";
import { ReleasesDialog } from "@/components/ReleasesDialog";
import { SubtitleSummary, SubtitlesDialog } from "@/components/SubtitlesDialog";
import { useToast } from "@/components/Toasts";
import { Alert, Badge, Button, ButtonLink, Checkbox, buttonClass, Empty, Field, IconButton, Menu, MenuItem, Modal, PickList, Progress, Rating, Segmented, Select, Skeleton, StatusBadge, Switch, Well, toneDot } from "@/components/ui";
import type { Status } from "@/components/ui";

type Detail = { item: LibraryItem; file?: MovieFile | null; seasons?: Season[]; episodes?: Episode[] };
type Tab = "overview" | "content" | "history";

const libraryName: Record<Kind, string> = { movie: "Movies", series: "Series", anime: "Anime" };
const mainVersions = ["VO", "VF", "VOSTFR", "MULTi"];
const SAVED_MS = 2000;
const reset = "cursor-pointer font-medium text-ink-2 underline underline-offset-2 hover:text-ink";

const formatDay = (iso: string) => new Date(`${iso}T00:00`).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" });
// The live activity toasts use the same key, so a search's result replaces its "Searching" toast.
const toastKey = (item: LibraryItem) => titleKey(isSeries(item) ? { movieId: null, seriesId: item.id } : { movieId: item.id, seriesId: null }) ?? item.title;

export function TitleView({ type, id, season }: { type: "movie" | "series"; id: number; season: number | null }) {
  const [detail, setDetail] = useState<Detail | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    const request = type === "movie"
      ? api.movie(id).then((d): Detail => ({ item: d.movie, file: d.file }))
      : api.seriesDetail(id).then((d): Detail => ({ item: d.series, seasons: d.seasons, episodes: d.episodes }));
    request.then(setDetail).catch((e: unknown) => setError(errorMessage(e)));
  }, [type, id]);

  useEffect(load, [load]);

  if (error && !detail) return <Empty icon="alert" title="This title could not be loaded">{error}</Empty>;
  if (!detail) return <TitleSkeleton />;
  return <Loaded detail={detail} focusSeason={season} onDetail={setDetail} reload={load} />;
}

function Loaded({ detail, focusSeason, onDetail, reload }: { detail: Detail; focusSeason: number | null; onDetail: (detail: Detail) => void; reload: () => void }) {
  const { item } = detail;
  const kind = kindOf(item);
  const toast = useToast();
  const { user, managed } = useSession();
  const admin = user.role === "Admin";
  const { epoch, progress } = useActivity();
  const [download, setDownload] = useState<Download | null>(null);
  const [releases, setReleases] = useState<{ season?: number; episode?: number } | null>(null);
  const [subtitles, setSubtitles] = useState<{ target: SubtitleTarget; title: string } | null>(null);
  // Bumped when the subtitles dialog closes so the file's summary shows what was downloaded there.
  const [subtitlesClosed, setSubtitlesClosed] = useState(0);
  const [removing, setRemoving] = useState(false);
  const [searching, setSearching] = useState<number | "all" | null>(null);
  const [tab, setTab] = useState<Tab>(focusSeason !== null ? "content" : "overview");
  const scope = useMemo(() => (isSeries(item) ? { seriesId: item.id } : { movieId: item.id }), [item]);
  const movieTarget = useMemo(() => ({ movieId: item.id }), [item.id]);

  const loadDownload = useCallback(() => {
    api.downloads()
      .then((all) => setDownload(all.find((d) => isActive(d) && (isSeries(item) ? d.seriesId : d.movieId) === item.id) ?? null))
      .catch(() => undefined);
  }, [item]);
  useEffect(loadDownload, [loadDownload, epoch]);

  // Searches, grabs and imports change the status, so the title reloads as they happen.
  const refresh = useCallback(() => { reload(); loadDownload(); }, [reload, loadDownload]);
  useTitleEvents(scope, refresh);
  useChrome({ backdrop: item.backdrop, crumb: { label: libraryName[kind], href: libraryHref[kind], title: item.title } });

  const fail = (e: unknown) => toast({ key: toastKey(item), icon: "alert", tone: "bad", title: item.title, message: errorMessage(e) });

  async function search(season?: number) {
    setSearching(season ?? "all");
    try {
      await api.autoSearch(kind, item.id, season);
      toast({ key: toastKey(item), icon: "search", tone: "seek", title: item.title,
        message: season === undefined ? "Searching. A release that fits your profile goes to Downloads." : `Searching season ${season}.` });
    } catch (e) {
      fail(e);
    } finally {
      setSearching(null);
    }
  }

  async function searchSubtitles() {
    try {
      await api.searchMissingSubtitles(kind, item.id);
      toast({ key: toastKey(item), icon: "subtitles", tone: "seek", title: item.title, message: "Searching the missing subtitles." });
    } catch (e) {
      fail(e);
    }
  }

  const live = download && progress.get(download.id);
  const today = isoDay(new Date());
  const nextAir = detail.episodes?.filter((e) => e.airDate !== null && e.airDate > today).map((e) => e.airDate ?? "").sort()[0] ?? null;
  const status: Status | undefined = download ? { state: "downloading", progress: live ? live.progress : download.progress ?? 0 }
    : !item.monitored ? { state: "unmonitored" }
    : item.hasFile ? { state: "library" }
    : isSeries(item) && item.missing === 0 ? (nextAir ? { state: "unreleased", date: nextAir } : undefined)
    : { state: "missing" };

  // linkarr's /go/watch opens the title in the media server by its TMDB (movie) or TVDB (series) id.
  const watch = managed && (isSeries(item) ? item.episodesWithFile > 0 : item.hasFile)
    ? `${managed.url.replace(/\/+$/, "")}/go/watch?${isSeries(item) ? `tvdb=${item.tvdbId}` : `tmdb=${item.tmdbId}`}` : null;
  const action = (
    <div className="space-y-3">
      <PrimaryAction admin={admin} item={item} file={detail.file ?? null} download={download} live={live ?? null} nextAir={nextAir} searching={searching}
        onSearch={() => search()} onReleases={() => setReleases({})}
        onToggle={() => download && void (download.status === "Paused" ? api.resume(download.id) : api.pause(download.id)).then(loadDownload, fail)} />
      {watch && (
        <a href={watch} target="_blank" rel="noopener noreferrer" className={`${buttonClass("primary")} w-full`}>
          <Icon name="play" size={16} />Watch
        </a>
      )}
    </div>
  );
  const hasAction = !item.hasFile || !isSeries(item) || !!download || !!watch;
  const tabs: readonly { value: Tab; label: string }[] = [
    { value: "overview", label: "Overview" },
    { value: "content", label: isSeries(item) ? "Seasons" : "Files" },
    { value: "history", label: "History" },
  ];
  const onTab = (t: Tab) => (tab === t ? "" : "max-md:hidden");

  return (
    <div className={hasAction ? "max-md:pb-20" : ""}>
      {/* The action panel already states the status, except that the title is unmonitored. */}
      <Hero item={item} status={hasAction && status?.state !== "unmonitored" ? undefined : status} />

      <Segmented label="Sections" value={tab} onChange={setTab} options={tabs} className="mt-4 flex w-full md:hidden [&>button]:flex-1 [&>button]:justify-center" />

      <div className="mt-6 grid gap-6 lg:grid-cols-[minmax(0,1fr)_380px] lg:items-start">
        <div className="min-w-0 space-y-8">
          {item.overview && <p className={`max-w-[68ch] text-body leading-7 text-ink-2 ${onTab("overview")}`}>{item.overview}</p>}
          <div className={onTab("content")}>
            {isSeries(item) && detail.seasons && detail.episodes ? (
              <Seasons admin={admin} series={item} seasons={detail.seasons} episodes={detail.episodes} focus={focusSeason} searching={searching}
                onSearch={search} onReleases={(season, episode) => setReleases({ season, episode })} onError={fail}
                onDetail={(d) => onDetail({ item: d.series, seasons: d.seasons, episodes: d.episodes })}
                onEpisode={(e) => onDetail({ ...detail, episodes: detail.episodes?.map((x) => (x.id === e.id ? e : x)) })}
                onSubtitles={(e) => setSubtitles({ target: { episodeId: e.id }, title: `${item.title} ${episodeCode(e)}` })} />
            ) : (
              <Files item={item} target={movieTarget} reload={subtitlesClosed} onSubtitles={admin ? () => setSubtitles({ target: movieTarget, title: item.title }) : undefined}
                onSearchMissing={admin ? searchSubtitles : undefined} />
            )}
          </div>
          <div className={onTab("history")}><ActivityTimeline scope={scope} /></div>
        </div>

        <div className={`lg:sticky lg:top-24 ${onTab("overview")} md:max-lg:row-start-1`}>
          <YourCopy item={item} kind={kind} action={hasAction ? action : null} onChange={(next) => onDetail({ ...detail, item: next })}
            onRemove={() => setRemoving(true)} onScanned={refresh} onError={fail} />
        </div>
      </div>

      {hasAction && (
        <div className="glass fixed inset-x-3 bottom-[calc(88px+env(safe-area-inset-bottom))] z-20 rounded-tile p-3 md:hidden">{action}</div>
      )}

      {releases && <ReleasesDialog item={item} season={releases.season} episode={releases.episode} onClose={() => setReleases(null)} />}
      {subtitles && <SubtitlesDialog target={subtitles.target} title={subtitles.title} onClose={() => { setSubtitles(null); setSubtitlesClosed((n) => n + 1); }} />}
      {removing && <RemoveDialog item={item} kind={kind} onClose={() => setRemoving(false)} />}
    </div>
  );
}

function Hero({ item, status }: { item: LibraryItem; status?: Status }) {
  const runtime = formatRuntime(item.runtime);
  const seasons = isSeries(item) ? item.seasons.filter((s) => s.number > 0).length : 0;
  return (
    <section className="relative flex min-h-[45vh] overflow-hidden rounded-panel ring-1 ring-white/10 md:min-h-[320px]">
      {item.backdrop && (
        // eslint-disable-next-line @next/next/no-img-element
        <img src={item.backdrop} alt="" className="absolute inset-0 h-full w-full animate-fade object-cover object-[50%_25%]" />
      )}
      <div className="absolute inset-0 bg-gradient-to-t from-canvas via-canvas/50 to-canvas/0" />
      <div className="absolute inset-0 bg-gradient-to-r from-canvas/85 via-canvas/30 to-transparent max-md:hidden" />

      <div className="relative mt-auto flex w-full items-end gap-6 p-3 sm:p-6 lg:gap-8 lg:p-8">
        <PosterArt src={item.poster} title={item.title} className="w-40 shrink-0 shadow-2xl shadow-black/70 max-md:hidden lg:w-48" />
        {/* On phones the text sits on its own scrim: a bright backdrop must not drop it under 4.5:1. */}
        <div className="min-w-0 flex-1 animate-rise space-y-3 max-md:rounded-tile max-md:bg-veil max-md:p-4">
          {status && <StatusBadge status={status} />}
          <h1 tabIndex={-1} className="scrim-text font-display text-[clamp(40px,6vw,64px)] leading-none text-balance outline-none">{item.title}</h1>
          <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-meta text-ink-2">
            {item.year > 0 && <span className="tabular-nums">{item.year}</span>}
            <Rating value={item.rating} className="text-meta" />
            {runtime && <span>{runtime}</span>}
            {seasons > 0 && <span>{seasons} {seasons === 1 ? "season" : "seasons"}</span>}
            {item.certification && <span className="rounded-chip px-1.5 ring-1 ring-white/30">{item.certification}</span>}
            {item.genres.length > 0 && <span>{item.genres.slice(0, 3).join(", ")}</span>}
            {!isSeries(item) && item.collectionTmdbId && (
              <Link href={`/explore/?saga=${item.collectionTmdbId}`}
                className="inline-flex items-center gap-1 rounded-pill bg-white/10 px-2.5 py-0.5 font-medium text-ink ring-1 ring-white/20 transition hover:bg-white/20">
                <Icon name="layers" size={12} />{item.collectionName ?? "Collection"}
              </Link>
            )}
          </p>
        </div>
      </div>
    </section>
  );
}

// The one thing to do next, by state. Nothing that has a file offers a search or a release: the server refuses those.
function PrimaryAction({ admin, item, file, download, live, nextAir, searching, onSearch, onReleases, onToggle }: {
  admin: boolean;
  item: LibraryItem;
  file: MovieFile | null;
  download: Download | null;
  live: { progress: number; rate: number; eta: number | null } | null;
  nextAir: string | null;
  searching: number | "all" | null;
  onSearch: () => void;
  onReleases: () => void;
  onToggle: () => void;
}) {
  if (download) {
    const paused = download.status === "Paused";
    const value = live?.progress ?? download.progress ?? 0;
    const eta = live?.eta ?? download.eta;
    return (
      <div className="space-y-3">
        <div className="flex items-baseline justify-between gap-3 text-meta">
          <span className="font-semibold text-live">{paused ? "Paused" : "Downloading"}</span>
          <span className="text-ink-2 tabular-nums">
            {!paused && live && `${formatSize(live.rate)}/s · `}{!paused && eta !== null && `${formatEta(eta)} left · `}<span className="text-ink">{Math.round(value * 100)}%</span>
          </span>
        </div>
        <Progress value={value} eta={eta !== null ? formatEta(eta) : null} label="Download progress" />
        <div className="flex gap-2">
          {admin && <Button size="sm" icon={paused ? "play" : "pause"} onClick={onToggle}>{paused ? "Resume" : "Pause"}</Button>}
          <ButtonLink href="/downloads/" variant="ghost" size="sm">View in downloads</ButtonLink>
        </div>
      </div>
    );
  }
  if (item.hasFile) {
    // Subtitles live in the Files section, the one entry point for them.
    return (
      <p className="flex min-h-9 items-center gap-2 text-meta font-semibold text-ok"><Icon name="check" size={16} strokeWidth={2.6} />{["Downloaded", file?.quality, file?.version].filter(Boolean).join(" · ")}</p>
    );
  }
  if (isSeries(item) && item.missing === 0) {
    return <p className="text-meta text-ink-2">{nextAir ? `Next episode airs ${formatDay(nextAir)}.` : "Nothing has aired yet."}</p>;
  }
  const partly = isSeries(item) && item.episodesWithFile > 0;
  if (!admin) return <p className="text-meta text-ink-2">{partly ? `${item.missing} missing.` : "Not downloaded yet."}</p>;
  return (
    <div className="flex flex-wrap gap-2 [&>button]:flex-1">
      <Button variant="primary" icon="bolt" loading={searching === "all"} disabled={searching !== null} onClick={onSearch}>
        {partly ? `Get missing (${item.missing})` : "Search now"}
      </Button>
      {!partly && <Button icon="list" onClick={onReleases}>Choose a release</Button>}
    </div>
  );
}

function YourCopy({ item, kind, action, onChange, onRemove, onScanned, onError }: {
  item: LibraryItem;
  kind: Kind;
  action: ReactNode;
  onChange: (item: LibraryItem) => void;
  onRemove: () => void;
  onScanned: () => void;
  onError: (e: unknown) => void;
}) {
  const options = useOptions();
  const toast = useToast();
  const { user } = useSession();
  const admin = user.role === "Admin";
  const [saved, setSaved] = useState(0);
  const [open, setOpen] = useState(false);

  useEffect(() => {
    if (!saved) return;
    const timer = setTimeout(() => setSaved(0), SAVED_MS);
    return () => clearTimeout(timer);
  }, [saved]);

  async function patch(change: ItemPatch) {
    try {
      onChange(await api.update(kind, item.id, change));
      setSaved(Date.now());
    } catch (e) {
      onError(e);
    }
  }

  async function rescan() {
    try {
      const result = await api.rescan(kind, item.id);
      toast({ key: toastKey(item), icon: "folder", tone: "ok", title: item.title, message: result.items === 0
        ? "The folder could not be scanned: it is missing, a link, or outside the library folders."
        : `Folder rescanned: ${result.added} found, ${result.removed} gone.` });
      onScanned();
    } catch (e) {
      onError(e);
    }
  }

  async function copyPath() {
    try {
      await navigator.clipboard.writeText(item.path);
      toast({ key: toastKey(item), icon: "copy", tone: "ok", title: "Folder path copied", message: item.path });
    } catch (e) {
      onError(e);
    }
  }

  const versions = [...new Set([...mainVersions, item.wantedVersion])].filter((v) => !options || options.versions.includes(v));

  return (
    <aside aria-labelledby="copy-heading" className="glass space-y-5 rounded-panel p-5">
      <header className="flex min-h-11 items-center gap-3">
        <h2 id="copy-heading" className="sr-only">Your copy</h2>
        {/* The preferences fold away under the action: they are set once and rarely touched. Members cannot change them. */}
        {admin && (
          <button type="button" onClick={() => setOpen(!open)} aria-expanded={open} aria-controls="copy-preferences"
            className="flex min-h-11 cursor-pointer items-center gap-2 text-title font-semibold">
            Preferences<Icon name="chevronDown" size={16} className={`text-ink-3 transition ${open ? "rotate-180" : ""}`} />
          </button>
        )}
        {saved > 0 && <span role="status" className="flex animate-fade items-center gap-1 text-meta text-ok"><Icon name="check" size={16} />Saved</span>}
        <span className="flex-1" />
        {admin ? (
          <Menu label={`More actions for ${item.title}`}>
            <MenuItem icon="refresh" onClick={() => void rescan()}>Rescan folder</MenuItem>
            <MenuItem icon="copy" onClick={() => void copyPath()}>Copy folder path</MenuItem>
            <MenuItem icon="trash" danger onClick={onRemove}>Delete…</MenuItem>
          </Menu>
        ) : <IconButton variant="ghost" icon="copy" label="Copy folder path" onClick={() => void copyPath()} />}
      </header>

      {action && <div className="max-md:hidden">{action}</div>}

      {admin && <fieldset id="copy-preferences" className={`space-y-5 ${open ? "" : "hidden"}`}>
        {!options ? <Skeleton className="h-48" /> : (
          <>
            <div className="space-y-2">
              <p className="text-body font-medium">Version</p>
              <Segmented label="Version" value={item.wantedVersion} onChange={(version) => patch({ version })} options={versions.map((v) => ({ value: v, label: v }))} />
            </div>
            <PickList label="Subtitles" values={item.wantedSubtitles} choices={realLanguages(options).map((l) => l.name)} empty="No subtitles wanted"
              onChange={(subtitles) => patch({ subtitles })} />
            {(item.version || item.subtitles) && (
              <p className="flex flex-wrap gap-x-4 text-meta">
                {item.version && <button type="button" className={reset} onClick={() => patch({ version: null })}>Reset version to {item.policyVersion}</button>}
                {item.subtitles && <button type="button" className={reset} onClick={() => patch({ subtitles: null })}>Use the {kind} subtitles</button>}
              </p>
            )}
            <div className="border-t border-hair-2 pt-2">
              <Switch checked={item.monitored} onChange={(monitored) => patch({ monitored })} label="Monitored" />
            </div>
            <Field label="Quality profile">
              <Select value={item.qualityProfileId} onChange={(e) => patch({ qualityProfileId: Number(e.target.value) })}>
                {options.profiles.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </Select>
            </Field>
          </>
        )}
      </fieldset>}
    </aside>
  );
}

function Files({ item, target, reload, onSubtitles, onSearchMissing }: {
  item: LibraryItem;
  target: SubtitleTarget;
  reload: number;
  onSubtitles?: () => void;
  onSearchMissing?: () => Promise<void>;
}) {
  return (
    <section aria-labelledby="files-heading" className="space-y-4">
      <h2 id="files-heading" className="text-title font-semibold">Files</h2>
      <Well className="space-y-4">
        <p className="flex items-start gap-3 text-meta">
          <Icon name="folder" size={16} className="mt-0.5 shrink-0 text-ink-3" />
          <span className="min-w-0 break-all text-ink-2">{item.path}</span>
        </p>
        {item.hasFile
          ? <SubtitleSummary target={target} reload={reload} onOpen={onSubtitles} onSearchMissing={onSearchMissing} />
          : <p className="text-meta text-ink-3">No file yet. Once a release is imported it lands in this folder.</p>}
      </Well>
    </section>
  );
}

function Seasons({ admin, series, seasons, episodes, focus, searching, onSearch, onReleases, onDetail, onEpisode, onSubtitles, onError }: {
  admin: boolean;
  series: LibraryItem;
  seasons: Season[];
  episodes: Episode[];
  focus: number | null;
  searching: number | "all" | null;
  onSearch: (season: number) => void;
  onReleases: (season: number, episode?: number) => void;
  onDetail: (detail: SeriesDetail) => void;
  onEpisode: (episode: Episode) => void;
  onSubtitles: (episode: Episode) => void;
  onError: (e: unknown) => void;
}) {
  const ordered = [...seasons].sort((a, b) => (a.number === 0 ? 1 : b.number === 0 ? -1 : b.number - a.number));
  // A link to a season opens it; otherwise the latest season still missing episodes.
  const initial = focus ?? ordered.find((s) => s.number > 0 && s.monitored && s.missing > 0)?.number;
  const [open, setOpen] = useState<ReadonlySet<number>>(new Set(initial === undefined ? [] : [initial]));
  const today = isoDay(new Date());

  useEffect(() => {
    if (focus === null) return;
    document.getElementById(`season-${focus}`)?.scrollIntoView({ block: "start", behavior: "smooth" });
  }, [focus]);

  if (seasons.length === 0) return <Empty icon="tv" title="No episodes listed yet" />;

  const monitorSeason = (number: number, monitored: boolean) =>
    api.setSeasonMonitored(series.id, number, monitored).then(onDetail, onError);
  const monitorEpisode = (episode: Episode, monitored: boolean) =>
    api.setEpisodeMonitored(episode.id, monitored).then(onEpisode, onError);

  return (
    <section aria-labelledby="seasons-heading" className="space-y-4">
      <h2 id="seasons-heading" className="text-title font-semibold">Seasons</h2>
      <ul className="space-y-3">
        {ordered.map((season) => {
          const expanded = open.has(season.number);
          const panel = `season-${season.number}-episodes`;
          const name = season.number === 0 ? "Specials" : `Season ${season.number}`;
          return (
            <li key={season.number} id={`season-${season.number}`} className="well scroll-mt-28 overflow-hidden rounded-card">
              <div className={`flex items-center gap-2 py-2 pr-2 sm:gap-3 sm:pr-3 ${admin ? "pl-1" : "pl-4"}`}>
                {admin && <Switch hideLabel checked={season.monitored} onChange={(m) => void monitorSeason(season.number, m)} label={`Monitor ${name}`} />}
                <button aria-expanded={expanded} aria-controls={panel}
                  onClick={() => setOpen((o) => { const n = new Set(o); if (!n.delete(season.number)) n.add(season.number); return n; })}
                  className="flex min-h-11 min-w-0 flex-1 cursor-pointer items-center gap-3 rounded-ctl text-left">
                  <span className="min-w-0">
                    <span className={`block font-semibold ${season.monitored ? "" : "text-ink-3"}`}>{name}</span>
                    <span className="flex items-center gap-2 text-meta text-ink-3 tabular-nums">
                      {season.episodesWithFile}/{season.episodes}<span className="sr-only"> on disk</span>
                      <span className="h-1 w-16 overflow-hidden rounded-full bg-fill-2" aria-hidden="true">
                        <span className={`block h-full rounded-full ${season.hasFile ? "bg-ok" : "bg-ink"}`} style={{ width: `${season.episodes ? (season.episodesWithFile / season.episodes) * 100 : 0}%` }} />
                      </span>
                    </span>
                  </span>
                  <Icon name="chevronDown" size={16} className={`ml-auto shrink-0 text-ink-3 transition-transform ${expanded ? "rotate-180" : ""}`} />
                </button>
                {!season.hasFile && admin && season.monitored && season.missing > 0 && (
                  <>
                    <Button size="sm" icon="bolt" loading={searching === season.number} disabled={searching !== null} onClick={() => onSearch(season.number)}
                      aria-label={`Get ${name}`}><span className="max-sm:hidden">Get season</span></Button>
                    <IconButton variant="ghost" icon="list" label={`Choose a release for ${name}`} onClick={() => onReleases(season.number)} />
                  </>
                )}
              </div>
              {expanded && (
                <ol id={panel} className="animate-fade divide-y divide-hair-2 border-t border-hair-2 bg-sunk">
                  {episodes.filter((e) => e.seasonNumber === season.number).map((e) => (
                    <EpisodeRow key={e.id} admin={admin} episode={e} aired={e.airDate !== null && e.airDate <= today}
                      onMonitor={(m) => void monitorEpisode(e, m)} onSubtitles={() => onSubtitles(e)} onReleases={() => onReleases(season.number, e.episodeNumber)} />
                  ))}
                </ol>
              )}
            </li>
          );
        })}
      </ul>
    </section>
  );
}

function EpisodeRow({ admin, episode: e, aired, onMonitor, onSubtitles, onReleases }: {
  admin: boolean;
  episode: Episode;
  aired: boolean;
  onMonitor: (monitored: boolean) => void;
  onSubtitles: () => void;
  onReleases: () => void;
}) {
  const name = e.title ?? `Episode ${e.episodeNumber}`;
  return (
    <li className="flex min-h-14 items-center gap-3 py-2 pr-2 pl-4 text-body sm:pr-3">
      <span className="w-6 shrink-0 text-meta text-ink-3 tabular-nums">{e.episodeNumber}</span>
      <span className="min-w-0 flex-1">
        <span className={`block truncate ${e.monitored || e.hasFile ? "" : "text-ink-3"}`}>{name}</span>
        <span className="block text-meta text-ink-3 tabular-nums">{e.airDate ? formatDay(e.airDate) : "Air date unknown"}</span>
      </span>
      {e.hasFile ? (
        <>
          <span role="img" aria-label="On disk" title="On disk" className={`size-2 shrink-0 rounded-full ${toneDot.ok}`} />
          {admin && <IconButton variant="ghost" icon="subtitles" label={`Subtitles for ${name}`} onClick={onSubtitles} />}
        </>
      ) : aired ? (
        <>
          <Badge tone={e.monitored ? "warn" : "neutral"}>Missing</Badge>
          {admin && (
            <label className="grid size-11 shrink-0 cursor-pointer place-items-center rounded-full hover:bg-fill-2" title="Monitored">
              <input type="checkbox" checked={e.monitored} onChange={(ev) => onMonitor(ev.target.checked)} aria-label={`Monitor ${name}`} className="size-4 cursor-pointer accent-white" />
            </label>
          )}
          {admin && <IconButton variant="ghost" icon="list" label={`Choose a release for ${name}`} onClick={onReleases} />}
        </>
      ) : <span className="pr-2 text-meta text-ink-3">Upcoming</span>}
    </li>
  );
}

function RemoveDialog({ item, kind, onClose }: { item: LibraryItem; kind: Kind; onClose: () => void }) {
  const router = useRouter();
  const [deleteFiles, setDeleteFiles] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function remove() {
    setBusy(true);
    try {
      await api.remove(kind, item.id, deleteFiles);
      router.push(libraryHref[kind]);
    } catch (e) {
      setError(errorMessage(e));
      setBusy(false);
    }
  }

  return (
    <Modal onClose={onClose} title={`Delete ${item.title}?`} description="It leaves your library and stops being monitored."
      footer={<>
        <Button variant="ghost" onClick={onClose}>Cancel</Button>
        <Button variant="danger" onClick={remove} loading={busy}>{deleteFiles ? "Delete with files" : "Delete"}</Button>
      </>}>
      <div className="space-y-4 px-5 pb-5">
        <Checkbox checked={deleteFiles} onChange={setDeleteFiles} label="Also delete its files from disk" description={<span className="break-all">{item.path}</span>} />
        {error && <Alert>{error}</Alert>}
      </div>
    </Modal>
  );
}

function TitleSkeleton() {
  return (
    <div className="space-y-6">
      <Skeleton className="h-[45vh] rounded-panel md:h-[320px]" />
      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_380px]">
        <div className="space-y-3">
          <Skeleton className="h-4 max-w-2xl" />
          <Skeleton className="h-4 max-w-xl" />
          <Skeleton className="mt-6 h-16 rounded-card" />
          <Skeleton className="h-16 rounded-card" />
        </div>
        <Skeleton className="h-80 rounded-panel" />
      </div>
    </div>
  );
}
