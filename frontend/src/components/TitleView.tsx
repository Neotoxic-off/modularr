"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { api, episodeCode, errorMessage, formatEta, formatRuntime, formatSize, isSeries, isoDay, kindOf, libraryHref } from "@/lib/api";
import type { Download, Episode, ItemPatch, Kind, LibraryItem, MovieFile, Season, SeriesDetail, SubtitleTarget } from "@/lib/api";
import { realLanguages, useOptions } from "@/lib/options";
import { isActive, useActivity, useTitleEvents } from "@/components/Activity";
import { ActivityTimeline } from "@/components/ActivityFeed";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { PosterArt } from "@/components/Poster";
import { useChrome } from "@/components/Shell";
import { ReleasesDialog } from "@/components/ReleasesDialog";
import { SubtitleSummary, SubtitlesDialog } from "@/components/SubtitlesDialog";
import { useToast } from "@/components/Toasts";
import { Alert, Badge, Button, Checkbox, Empty, Field, Menu, MenuItem, Modal, PickList, Progress, Rating, Segmented, Select, Skeleton, StatusBadge, Switch } from "@/components/ui";
import type { Status } from "@/components/ui";

type Detail = { item: LibraryItem; file?: MovieFile | null; seasons?: Season[]; episodes?: Episode[] };
type Tab = "overview" | "content" | "history";

const libraryName: Record<Kind, string> = { movie: "Movies", series: "Series", anime: "Anime" };
const mainVersions = ["VO", "VF", "VOSTFR", "MULTi"];
const SAVED_MS = 2000;

const formatDay = (iso: string) => new Date(`${iso}T00:00`).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" });

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
  const { epoch, progress } = useActivity();
  const [download, setDownload] = useState<Download | null>(null);
  const [releases, setReleases] = useState<{ season?: number } | null>(null);
  const [subtitles, setSubtitles] = useState<{ target: SubtitleTarget; title: string } | null>(null);
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

  const fail = (e: unknown) => toast({ key: `title:${item.id}`, icon: "alert", tone: "bad", title: item.title, message: errorMessage(e) });

  async function search(season?: number) {
    setSearching(season ?? "all");
    try {
      await api.autoSearch(kind, item.id, season);
      toast({ key: `title:${item.id}`, icon: "search", tone: "seek", title: item.title,
        message: season === undefined ? "Searching. A release that fits your profile goes to Downloads." : `Searching season ${season}.` });
    } catch (e) {
      fail(e);
    } finally {
      setSearching(null);
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

  const action = (
    <PrimaryAction item={item} file={detail.file ?? null} download={download} live={live ?? null} nextAir={nextAir} searching={searching}
      onSearch={() => search()} onReleases={() => setReleases({})} onSubtitles={() => setSubtitles({ target: movieTarget, title: item.title })}
      onToggle={() => download && void (download.status === "Paused" ? api.resume(download.id) : api.pause(download.id)).then(loadDownload, fail)} />
  );
  const hasAction = !item.hasFile || !isSeries(item) || !!download;
  const tabs: readonly { value: Tab; label: string }[] = [
    { value: "overview", label: "Overview" },
    { value: "content", label: isSeries(item) ? "Seasons" : "Files" },
    { value: "history", label: "History" },
  ];
  const onTab = (t: Tab) => (tab === t ? "" : "max-md:hidden");

  return (
    <div className={hasAction ? "max-md:pb-20" : ""}>
      <Hero item={item} status={status} downloading={!!download} />

      <Segmented label="Sections" value={tab} onChange={setTab} options={tabs} className="mt-4 flex w-full md:hidden [&>button]:flex-1 [&>button]:justify-center" />

      <div className="mt-6 grid gap-6 lg:grid-cols-[minmax(0,1fr)_380px] lg:items-start">
        <div className="min-w-0 space-y-8">
          {item.overview && <p className={`max-w-[68ch] text-[15px] leading-7 text-ink-2 ${onTab("overview")}`}>{item.overview}</p>}
          <div className={onTab("content")}>
            {isSeries(item) && detail.seasons && detail.episodes ? (
              <Seasons series={item} seasons={detail.seasons} episodes={detail.episodes} focus={focusSeason} searching={searching}
                onSearch={search} onReleases={(season) => setReleases({ season })} onError={fail}
                onDetail={(d) => onDetail({ item: d.series, seasons: d.seasons, episodes: d.episodes })}
                onEpisode={(e) => onDetail({ ...detail, episodes: detail.episodes?.map((x) => (x.id === e.id ? e : x)) })}
                onSubtitles={(e) => setSubtitles({ target: { episodeId: e.id }, title: `${item.title} ${episodeCode(e)}` })} />
            ) : (
              <Files item={item} target={movieTarget} onSubtitles={() => setSubtitles({ target: movieTarget, title: item.title })} />
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

      {releases && <ReleasesDialog item={item} season={releases.season} onClose={() => setReleases(null)} />}
      {subtitles && <SubtitlesDialog target={subtitles.target} title={subtitles.title} onClose={() => setSubtitles(null)} />}
      {removing && <RemoveDialog item={item} kind={kind} onClose={() => setRemoving(false)} />}
    </div>
  );
}

function Hero({ item, status, downloading }: { item: LibraryItem; status?: Status; downloading: boolean }) {
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
        <div className="min-w-0 flex-1 animate-rise space-y-3 max-md:rounded-tile max-md:bg-black/55 max-md:p-4">
          {status && (downloading
            ? <Link href="/downloads/" className="inline-block rounded-pill"><StatusBadge status={status} /></Link>
            : <StatusBadge status={status} />)}
          <h1 tabIndex={-1} className="scrim-text font-serif text-[clamp(40px,6vw,64px)] leading-none text-balance outline-none">{item.title}</h1>
          <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-[13px] text-ink-2">
            {item.year > 0 && <span className="tabular-nums">{item.year}</span>}
            <Rating value={item.rating} className="text-[13px]" />
            {runtime && <span>{runtime}</span>}
            {seasons > 0 && <span>{seasons} {seasons === 1 ? "season" : "seasons"}</span>}
            {item.certification && <span className="rounded-chip px-1.5 ring-1 ring-white/30">{item.certification}</span>}
            {item.genres.length > 0 && <span>{item.genres.slice(0, 3).join(", ")}</span>}
          </p>
        </div>
      </div>
    </section>
  );
}

// The one thing to do next, by state. Nothing that has a file offers a search or a release: the server refuses those.
function PrimaryAction({ item, file, download, live, nextAir, searching, onSearch, onReleases, onSubtitles, onToggle }: {
  item: LibraryItem;
  file: MovieFile | null;
  download: Download | null;
  live: { progress: number; rate: number; eta: number | null } | null;
  nextAir: string | null;
  searching: number | "all" | null;
  onSearch: () => void;
  onReleases: () => void;
  onSubtitles: () => void;
  onToggle: () => void;
}) {
  if (download) {
    const paused = download.status === "Paused";
    const value = live?.progress ?? download.progress ?? 0;
    const eta = live?.eta ?? download.eta;
    return (
      <div className="space-y-3">
        <div className="flex items-baseline justify-between gap-3 text-[13px]">
          <span className="font-semibold text-live">{paused ? "Paused" : "Downloading"}</span>
          <span className="text-ink-2 tabular-nums">
            {!paused && live && `${formatSize(live.rate)}/s · `}{!paused && eta !== null && `${formatEta(eta)} left · `}<span className="text-ink">{Math.round(value * 100)}%</span>
          </span>
        </div>
        <Progress value={value} eta={eta !== null ? formatEta(eta) : null} label="Download progress" />
        <div className="flex gap-2">
          <Button size="sm" icon={paused ? "play" : "pause"} onClick={onToggle}>{paused ? "Resume" : "Pause"}</Button>
          <Link href="/downloads/" className="inline-flex h-9 items-center rounded-pill px-4 text-[13px] font-semibold text-ink-2 hover:bg-fill hover:text-ink">View in downloads</Link>
        </div>
      </div>
    );
  }
  if (item.hasFile) {
    return (
      <div className="flex items-center gap-3">
        <span className="flex min-w-0 flex-1 items-center gap-2 text-[13px] font-semibold text-ok"><Icon name="check" size={16} strokeWidth={2.6} />{["Downloaded", file?.quality, file?.version].filter(Boolean).join(" · ")}</span>
        <Button size="sm" icon="subtitles" onClick={onSubtitles}>Subtitles</Button>
      </div>
    );
  }
  if (isSeries(item) && item.missing === 0) {
    return <p className="text-[13px] text-ink-2">{nextAir ? `Next episode airs ${formatDay(nextAir)}.` : "Nothing has aired yet."}</p>;
  }
  const partly = isSeries(item) && item.episodesWithFile > 0;
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
      toast({ key: `title:${item.id}`, icon: "folder", tone: "ok", title: item.title, message: result.items === 0
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
      toast({ key: `title:${item.id}`, icon: "copy", tone: "ok", title: "Folder path copied", message: item.path });
    } catch (e) {
      onError(e);
    }
  }

  const versions = [...new Set([...mainVersions, item.wantedVersion])].filter((v) => !options || options.versions.includes(v));

  return (
    <aside aria-labelledby="copy-heading" className="glass space-y-5 rounded-panel p-5">
      <header className="flex min-h-11 items-center gap-3">
        <h2 id="copy-heading" className="text-[17px] font-semibold max-md:sr-only">Your copy</h2>
        {/* Phones fold the preferences away under the action. */}
        <button type="button" onClick={() => setOpen(!open)} aria-expanded={open}
          className="flex min-h-11 cursor-pointer items-center gap-2 text-[17px] font-semibold md:hidden">
          Preferences<Icon name="chevronDown" size={18} className={`text-ink-3 transition ${open ? "rotate-180" : ""}`} />
        </button>
        {saved > 0 && <span role="status" className="flex animate-fade items-center gap-1 text-[13px] text-ok"><Icon name="check" size={15} />Saved</span>}
        <span className="flex-1" />
        <Menu label={`More actions for ${item.title}`}>
          <MenuItem icon="refresh" onClick={() => void rescan()}>Rescan folder</MenuItem>
          <MenuItem icon="copy" onClick={() => void copyPath()}>Copy folder path</MenuItem>
          {user.role === "Admin" && <MenuItem icon="trash" danger onClick={onRemove}>Delete…</MenuItem>}
        </Menu>
      </header>

      {action && <div className="max-md:hidden">{action}</div>}

      <div className={`space-y-5 ${open ? "" : "max-md:hidden"}`}>
        {!options ? <Skeleton className="h-48" /> : (
          <>
            <div className="space-y-2">
              <p className="text-sm font-medium">Version</p>
              <Segmented label="Version" value={item.wantedVersion} onChange={(version) => patch({ version })} options={versions.map((v) => ({ value: v, label: v }))} />
              <p className="text-[13px] text-ink-3">
                {item.version
                  ? <>Set for this title. <button type="button" className="cursor-pointer font-medium text-ink-2 underline underline-offset-2 hover:text-ink" onClick={() => patch({ version: null })}>Reset to {item.policyVersion}</button></>
                  : `Following default: ${item.policyVersion}`}
              </p>
            </div>
            <PickList label="Subtitles" values={item.wantedSubtitles} choices={realLanguages(options).map((l) => l.name)} empty="No subtitles wanted"
              onChange={(subtitles) => patch({ subtitles })}
              hint={<>Subtitles never change the audio.{item.subtitles && <> <button type="button" className="cursor-pointer font-medium text-ink-2 underline underline-offset-2 hover:text-ink" onClick={() => patch({ subtitles: null })}>Use the {kind} default</button></>}</>} />
            <div className="border-t border-hair-2 pt-2">
              <Switch checked={item.monitored} onChange={(monitored) => patch({ monitored })} label="Monitored"
                description={item.monitored ? "Missing and new releases are grabbed automatically." : "Nothing is grabbed automatically."} />
            </div>
            <Field label="Quality profile">
              <Select value={item.qualityProfileId} onChange={(e) => patch({ qualityProfileId: Number(e.target.value) })}>
                {options.profiles.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </Select>
            </Field>
          </>
        )}
      </div>
    </aside>
  );
}

function Files({ item, target, onSubtitles }: { item: LibraryItem; target: SubtitleTarget; onSubtitles: () => void }) {
  return (
    <section aria-labelledby="files-heading" className="space-y-4">
      <h2 id="files-heading" className="text-[17px] font-semibold">Files</h2>
      <div className="well space-y-4 rounded-[20px] p-4">
        <p className="flex items-start gap-3 text-sm">
          <Icon name="folder" size={18} className="mt-0.5 shrink-0 text-ink-3" />
          <span className="min-w-0 break-all text-ink-2">{item.path}</span>
        </p>
        {item.hasFile
          ? <SubtitleSummary target={target} onOpen={onSubtitles} />
          : <p className="text-[13px] text-ink-3">No file yet. Once a release is imported it lands in this folder.</p>}
      </div>
    </section>
  );
}

function Seasons({ series, seasons, episodes, focus, searching, onSearch, onReleases, onDetail, onEpisode, onSubtitles, onError }: {
  series: LibraryItem;
  seasons: Season[];
  episodes: Episode[];
  focus: number | null;
  searching: number | "all" | null;
  onSearch: (season: number) => void;
  onReleases: (season: number) => void;
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
      <h2 id="seasons-heading" className="text-[17px] font-semibold">Seasons</h2>
      <ul className="space-y-3">
        {ordered.map((season) => {
          const expanded = open.has(season.number);
          const panel = `season-${season.number}-episodes`;
          const name = season.number === 0 ? "Specials" : `Season ${season.number}`;
          return (
            <li key={season.number} id={`season-${season.number}`} className="well scroll-mt-28 overflow-hidden rounded-[20px]">
              <div className="flex items-center gap-2 py-2 pr-2 pl-1 sm:gap-3 sm:pr-3">
                <Switch hideLabel checked={season.monitored} onChange={(m) => void monitorSeason(season.number, m)} label={`Monitor ${name}`} />
                <button aria-expanded={expanded} aria-controls={panel}
                  onClick={() => setOpen((o) => { const n = new Set(o); if (!n.delete(season.number)) n.add(season.number); return n; })}
                  className="flex min-h-11 min-w-0 flex-1 cursor-pointer items-center gap-3 rounded-ctl text-left">
                  <span className="min-w-0">
                    <span className={`block font-semibold ${season.monitored ? "" : "text-ink-3"}`}>{name}</span>
                    <span className="flex items-center gap-2 text-[13px] text-ink-3 tabular-nums">
                      {season.episodesWithFile}/{season.episodes} on disk
                      <span className="h-1 w-16 overflow-hidden rounded-full bg-fill-2" aria-hidden="true">
                        <span className={`block h-full rounded-full ${season.hasFile ? "bg-ok" : "bg-ink"}`} style={{ width: `${season.episodes ? (season.episodesWithFile / season.episodes) * 100 : 0}%` }} />
                      </span>
                    </span>
                  </span>
                  <Icon name="chevronDown" size={18} className={`ml-auto shrink-0 text-ink-3 transition-transform ${expanded ? "rotate-180" : ""}`} />
                </button>
                {season.hasFile ? <Badge tone="ok"><Icon name="check" size={12} strokeWidth={2.6} />Complete</Badge>
                  : season.monitored && season.missing > 0 && (
                    <>
                      <Button size="sm" icon="bolt" loading={searching === season.number} disabled={searching !== null} onClick={() => onSearch(season.number)}
                        aria-label={`Get ${name}`}><span className="max-sm:hidden">Get season</span></Button>
                      <Menu label={`More for ${name}`} className="grid size-11 shrink-0 cursor-pointer place-items-center rounded-full text-ink-2 hover:bg-fill-2 hover:text-ink">
                        <MenuItem icon="list" onClick={() => onReleases(season.number)}>Choose a release</MenuItem>
                      </Menu>
                    </>
                  )}
              </div>
              {expanded && (
                <ol id={panel} className="animate-fade divide-y divide-hair-2 border-t border-hair-2 bg-black/20">
                  {episodes.filter((e) => e.seasonNumber === season.number).map((e) => (
                    <EpisodeRow key={e.id} episode={e} aired={e.airDate !== null && e.airDate <= today}
                      onMonitor={(m) => void monitorEpisode(e, m)} onSubtitles={() => onSubtitles(e)} onReleases={() => onReleases(season.number)} />
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

function EpisodeRow({ episode: e, aired, onMonitor, onSubtitles, onReleases }: {
  episode: Episode;
  aired: boolean;
  onMonitor: (monitored: boolean) => void;
  onSubtitles: () => void;
  onReleases: () => void;
}) {
  const name = e.title ?? `Episode ${e.episodeNumber}`;
  return (
    <li className="flex min-h-14 items-center gap-3 py-2 pr-2 pl-4 text-sm sm:pr-3">
      <span className="w-6 shrink-0 text-[13px] text-ink-3 tabular-nums">{e.episodeNumber}</span>
      <span className="min-w-0 flex-1">
        <span className={`block truncate ${e.monitored || e.hasFile ? "" : "text-ink-3"}`}>{name}</span>
        <span className="block text-[12px] text-ink-3 tabular-nums">
          {e.airDate ? formatDay(e.airDate) : "Air date unknown"}
          {!e.hasFile && aired && <span className={`sm:hidden ${e.monitored ? "text-warn" : ""}`}> · Missing</span>}
        </span>
      </span>
      {e.hasFile ? (
        <>
          <Badge tone="ok"><Icon name="check" size={12} strokeWidth={2.6} />On disk</Badge>
          <button onClick={onSubtitles} aria-label={`Subtitles for ${name}`} title="Subtitles"
            className="grid size-11 shrink-0 cursor-pointer place-items-center rounded-full text-ink-2 hover:bg-fill-2 hover:text-ink">
            <Icon name="subtitles" size={17} />
          </button>
        </>
      ) : aired ? (
        <>
          <Badge tone={e.monitored ? "warn" : "neutral"} className="max-sm:hidden">Missing</Badge>
          <label className="grid size-11 shrink-0 cursor-pointer place-items-center rounded-full hover:bg-fill-2" title="Monitored">
            <input type="checkbox" checked={e.monitored} onChange={(ev) => onMonitor(ev.target.checked)} aria-label={`Monitor ${name}`} className="size-4 cursor-pointer accent-white" />
          </label>
          <button onClick={onReleases} aria-label={`Choose a release for ${name}`} title="Choose a release"
            className="grid size-11 shrink-0 cursor-pointer place-items-center rounded-full text-ink-2 hover:bg-fill-2 hover:text-ink">
            <Icon name="list" size={17} />
          </button>
        </>
      ) : <span className="pr-2 text-[12px] text-ink-3">Upcoming</span>}
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
          <Skeleton className="mt-6 h-16 rounded-[20px]" />
          <Skeleton className="h-16 rounded-[20px]" />
        </div>
        <Skeleton className="h-80 rounded-panel" />
      </div>
    </div>
  );
}
