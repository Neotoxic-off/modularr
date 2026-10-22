"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { api, errorMessage, formatAgo, formatRuntime, isSeries, kindOf, libraryHref } from "@/lib/api";
import type { Episode, ItemPatch, Kind, LibraryItem, MovieDetail, SeriesDetail, SubtitleTarget } from "@/lib/api";
import { realLanguages, useOptions, versionLabel } from "@/lib/options";
import { useTitleEvents } from "@/components/Activity";
import { ActivityTimeline } from "@/components/ActivityFeed";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { PosterArt } from "@/components/Poster";
import { useChrome } from "@/components/Shell";
import { ReleasesDialog } from "@/components/ReleasesDialog";
import { SubtitleSummary, SubtitlesDialog } from "@/components/SubtitlesDialog";
import { Alert, Badge, Button, Empty, Field, Modal, PickList, Rating, Select, Skeleton, Spinner, Switch } from "@/components/ui";

type Detail = { item: LibraryItem; hasFile?: boolean; seasons?: SeriesDetail["seasons"]; episodes?: Episode[] };

const libraryName: Record<Kind, string> = { movie: "Movies", series: "Series", anime: "Anime" };

export function TitleView({ type, id }: { type: "movie" | "series"; id: number }) {
  const [detail, setDetail] = useState<Detail | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    const request = type === "movie"
      ? api.movie(id).then((d: MovieDetail): Detail => ({ item: d.movie, hasFile: d.hasFile }))
      : api.seriesDetail(id).then((d: SeriesDetail): Detail => ({ item: d.series, seasons: d.seasons, episodes: d.episodes }));
    request.then(setDetail).catch((e: unknown) => setError(errorMessage(e)));
  }, [type, id]);

  useEffect(load, [load]);

  if (error && !detail) return <Empty icon="alert" title="This title could not be loaded">{error}</Empty>;
  if (!detail) return <TitleSkeleton />;
  return <Loaded detail={detail} onChange={(item) => setDetail({ ...detail, item })} reload={load} />;
}

function Loaded({ detail, onChange, reload }: { detail: Detail; onChange: (item: LibraryItem) => void; reload: () => void }) {
  const { item } = detail;
  const kind = kindOf(item);
  const { user } = useSession();
  const options = useOptions();
  const [releases, setReleases] = useState<{ season?: number } | null>(null);
  const [subtitles, setSubtitles] = useState<{ target: SubtitleTarget; title: string } | null>(null);
  const [removing, setRemoving] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [searching, setSearching] = useState<number | "all" | null>(null);
  const [scanning, setScanning] = useState(false);
  const scope = isSeries(item) ? { seriesId: item.id } : { movieId: item.id };
  const movieTarget = useMemo(() => ({ movieId: item.id }), [item.id]);
  // Searches and imports change the status and "Last search", so the title reloads as they happen.
  useTitleEvents(scope, reload);

  async function patch(change: ItemPatch) {
    setError(null);
    try {
      onChange(await api.update(kind, item.id, change));
    } catch (e) {
      setError(errorMessage(e));
    }
  }

  async function search(season?: number) {
    setSearching(season ?? "all");
    setError(null);
    try {
      await api.autoSearch(kind, item.id, season);
      setNotice(season === undefined ? "Search started. Anything that matches your profile is sent to Downloads." : `Searching season ${season}.`);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setSearching(null);
    }
  }

  async function rescan() {
    setScanning(true);
    setError(null);
    try {
      const result = await api.rescan(kind, item.id);
      setNotice(result.items === 0
        ? "The folder could not be scanned: it is missing, a link, or outside the library folders."
        : `Folder scanned: ${result.added} file(s) found, ${result.removed} missing.`);
      reload();
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setScanning(false);
    }
  }

  const runtime = formatRuntime(item.runtime);
  // The server refuses searches and grabs for files already on disk, so nothing offers them.
  const hasFile = isSeries(item) ? item.hasFile : !!detail.hasFile;
  useChrome({ backdrop: item.backdrop, crumb: { label: libraryName[kind], href: libraryHref[kind], title: item.title } });
  const status = isSeries(item)
    ? { done: item.episodes > 0 && item.episodesWithFile >= item.episodes, label: `${item.episodesWithFile} of ${item.episodes} episodes` }
    : { done: !!detail.hasFile, label: detail.hasFile ? "Downloaded" : "Missing" };

  return (
    <>
      <section className="relative overflow-hidden rounded-panel ring-1 ring-white/10">
        <div className="absolute inset-0">
          {item.backdrop && (
            // eslint-disable-next-line @next/next/no-img-element
            <img src={item.backdrop} alt="" className="h-full w-full animate-fade object-cover object-[50%_20%] opacity-60" />
          )}
          <div className="absolute inset-0 bg-gradient-to-t from-canvas via-canvas/75 to-canvas/20" />
          <div className="absolute inset-0 bg-gradient-to-r from-canvas/90 to-transparent" />
        </div>

        <div className="relative p-5 sm:p-6 lg:p-8">
          <Link href={libraryHref[kind]} className="inline-flex min-h-11 items-center gap-1 text-sm text-ink-2 hover:text-ink md:hidden">
            <Icon name="chevronLeft" size={16} />{libraryName[kind]}
          </Link>
          <div className="mt-[6vh] flex flex-col gap-6 sm:flex-row sm:items-end md:mt-[14vh] lg:gap-10">
            <PosterArt src={item.poster} title={item.title} className="w-32 shrink-0 shadow-2xl shadow-black/70 sm:w-48 lg:w-56" />
            <div className="min-w-0 flex-1 animate-rise">
              <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5 text-sm text-white/75">
                {item.year > 0 && <span>{item.year}</span>}
                {runtime && <span>{runtime}</span>}
                {item.certification && <span className="rounded border border-white/30 px-1.5 text-xs">{item.certification}</span>}
                <Rating value={item.rating} className="text-sm" />
                {isSeries(item) && <span>{item.seasons.filter((s) => s.number > 0).length} seasons</span>}
              </div>
              <h1 className="mt-2 scrim-text font-serif text-[clamp(40px,6vw,64px)] leading-none text-balance">{item.title}</h1>
              {item.genres.length > 0 && (
                <div className="mt-4 flex flex-wrap gap-1.5">
                  {item.genres.map((g) => <span key={g} className="rounded-full bg-white/[0.07] px-2.5 py-1 text-xs text-white/80 ring-1 ring-white/10">{g}</span>)}
                </div>
              )}
              <div className="mt-6 flex flex-wrap items-center gap-2.5">
                {!hasFile && (
                  <>
                    <Button variant="primary" onClick={() => search()} loading={searching === "all"} disabled={searching !== null} icon="bolt">Search now</Button>
                    <Button variant="glass" onClick={() => setReleases({})} icon="list">Choose a release</Button>
                  </>
                )}
                <Button variant="ghost" onClick={rescan} disabled={scanning} title="Look for files added or deleted in the folder">
                  {scanning ? <Spinner /> : <Icon name="folder" size={16} />}Rescan folder
                </Button>
                {user.role === "Admin" && (
                  <Button variant="ghost" onClick={() => setRemoving(true)} aria-label="Remove from library"><Icon name="trash" size={16} />Remove</Button>
                )}
              </div>
            </div>
          </div>
        </div>
      </section>

      <div className="mt-10 grid gap-8 lg:grid-cols-[minmax(0,1fr)_340px]">
        <div className="min-w-0 space-y-8">
          {notice && <p role="status" className="flex animate-fade items-center gap-2 rounded-lg bg-fill-2 px-4 py-2.5 text-sm text-ink"><Icon name="check" size={16} />{notice}</p>}
          {error && <Alert>{error}</Alert>}
          {item.overview && <p className="max-w-[68ch] text-[15px] leading-7 text-ink-2">{item.overview}</p>}
          {isSeries(item) && detail.seasons && detail.episodes && (
            <Seasons seasons={detail.seasons} episodes={detail.episodes} searching={searching}
              onSearch={search} onReleases={(season) => setReleases({ season })}
              onSubtitles={(e) => setSubtitles({ target: { episodeId: e.id }, title: `${item.title} S${String(e.seasonNumber).padStart(2, "0")}E${String(e.episodeNumber).padStart(2, "0")}` })} />
          )}
          <ActivityTimeline scope={scope} />
        </div>

        <aside className="space-y-4">
          <div className="space-y-4 rounded-tile border border-hair bg-fill p-5">
            <div className="flex items-center justify-between gap-3">
              <span className="text-sm text-ink-3">Status</span>
              <Badge tone={status.done ? "ok" : "warn"}>{status.label}</Badge>
            </div>
            <Switch checked={item.monitored} onChange={(monitored) => patch({ monitored })} label="Monitored"
              description={item.monitored ? "New and missing releases are grabbed automatically." : "Nothing is grabbed automatically."} />
            {!isSeries(item) && detail.hasFile && !subtitles && (
              <SubtitleSummary target={movieTarget} onOpen={() => setSubtitles({ target: movieTarget, title: item.title })} />
            )}
          </div>

          <div className="space-y-4 rounded-tile border border-hair bg-fill p-5">
            {!options ? <Spinner /> : (
              <>
                <Field label="Quality profile">
                  <Select value={item.qualityProfileId} onChange={(e) => patch({ qualityProfileId: Number(e.target.value) })}>
                    {options.profiles.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                  </Select>
                </Field>
                <Field label="Version" hint={item.version ? `Overrides the version rules, which would pick ${versionLabel(item.policyVersion)}.` : "Follows your version rules."}>
                  <Select value={item.version ?? ""} onChange={(e) => patch({ version: e.target.value || null })}>
                    <option value="">Use default ({item.policyVersion})</option>
                    {options.versions.map((v) => <option key={v} value={v}>{versionLabel(v)}</option>)}
                  </Select>
                </Field>
                <PickList label="Subtitles" values={item.wantedSubtitles} choices={realLanguages(options).map((l) => l.name)} empty="No subtitles wanted"
                  onChange={(subtitles) => patch({ subtitles })}
                  hint={item.subtitles ? <>Overrides the category default. <button type="button" className="cursor-pointer underline" onClick={() => patch({ subtitles: null })}>Use the default</button></> : "Follows the category default."} />
              </>
            )}
          </div>

          <dl className="space-y-3 rounded-tile border border-hair bg-fill p-5 text-sm">
            <Info label="Original language">{item.originalLanguage}</Info>
            <Info label="Last search">
              {item.lastSearch ? <span title={new Date(item.lastSearch.time).toLocaleString()}>{item.lastSearch.result}, {formatAgo(item.lastSearch.time)}</span> : "Not yet"}
            </Info>
            <Info label="Folder"><span className="font-mono text-xs break-all">{item.path}</span></Info>
          </dl>
        </aside>
      </div>

      {releases && <ReleasesDialog item={item} season={releases.season} onClose={() => setReleases(null)} />}
      {subtitles && <SubtitlesDialog target={subtitles.target} title={subtitles.title} onClose={() => setSubtitles(null)} />}
      {removing && <RemoveDialog item={item} kind={kind} onClose={() => setRemoving(false)} />}
    </>
  );
}

function Info({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-[128px_minmax(0,1fr)] gap-3">
      <dt className="text-ink-3">{label}</dt>
      <dd className="min-w-0">{children}</dd>
    </div>
  );
}

function Seasons({ seasons, episodes, searching, onSearch, onReleases, onSubtitles }: {
  seasons: SeriesDetail["seasons"];
  episodes: Episode[];
  searching: number | "all" | null;
  onSearch: (season: number) => void;
  onReleases: (season: number) => void;
  onSubtitles: (episode: Episode) => void;
}) {
  const ordered = [...seasons].sort((a, b) => (a.number === 0 ? 1 : b.number === 0 ? -1 : b.number - a.number));
  const [open, setOpen] = useState<ReadonlySet<number>>(new Set(ordered.slice(0, 1).map((s) => s.number)));
  const today = new Date().toISOString().slice(0, 10);

  if (seasons.length === 0) return <Empty icon="tv" title="No episodes listed yet" />;

  return (
    <section aria-label="Seasons">
      <h2 className="mb-4 text-[17px] font-semibold">Seasons</h2>
      <div className="divide-y divide-hair-2 overflow-hidden rounded-tile border border-hair bg-fill">
        {ordered.map((season) => {
          const expanded = open.has(season.number);
          const ratio = season.episodes ? season.episodesWithFile / season.episodes : 0;
          const panel = `season-${season.number}`;
          return (
            <div key={season.number}>
              <div className="flex items-center gap-3 px-4 py-3">
                <button aria-expanded={expanded} aria-controls={panel}
                  onClick={() => setOpen((o) => { const n = new Set(o); if (!n.delete(season.number)) n.add(season.number); return n; })}
                  className="flex min-w-0 flex-1 cursor-pointer items-center gap-3 text-left">
                  <Icon name="chevronDown" size={16} className={`shrink-0 text-ink-3 transition-transform ${expanded ? "" : "-rotate-90"}`} />
                  <span className="font-medium">{season.number === 0 ? "Specials" : `Season ${season.number}`}</span>
                  {!season.monitored && <Badge>Unmonitored</Badge>}
                  <span className="ml-auto hidden items-center gap-2.5 text-xs text-ink-3 tabular-nums sm:flex">
                    <span className="h-1 w-20 overflow-hidden rounded-full bg-fill-2">
                      <span className={`block h-full rounded-full ${ratio >= 1 ? "bg-ok" : "bg-ink"}`} style={{ width: `${ratio * 100}%` }} />
                    </span>
                    {season.episodesWithFile}/{season.episodes}
                  </span>
                </button>
                {season.hasFile ? <Badge tone="ok"><Icon name="check" size={12} />Complete</Badge> : (
                  <>
                    <Button variant="ghost" size="sm" onClick={() => onSearch(season.number)} disabled={searching !== null} aria-label={`Search season ${season.number}`} title="Search this season">
                      {searching === season.number ? <Spinner /> : <Icon name="bolt" size={14} />}<span className="hidden sm:inline">Search</span>
                    </Button>
                    <Button variant="ghost" size="sm" onClick={() => onReleases(season.number)} aria-label={`Choose a release for season ${season.number}`} title="Choose a release">
                      <Icon name="list" size={14} />
                    </Button>
                  </>
                )}
              </div>
              {expanded && (
                <ol id={panel} className="animate-fade border-t border-hair bg-black/20">
                  {episodes.filter((e) => e.seasonNumber === season.number).map((e) => {
                    const aired = e.airDate !== null && e.airDate <= today;
                    return (
                      <li key={e.id} className="flex items-center gap-3 px-4 py-2.5 text-sm sm:pl-11">
                        <span className="w-8 shrink-0 text-xs text-ink-3 tabular-nums">{e.episodeNumber}</span>
                        <span className={`min-w-0 flex-1 truncate ${e.monitored ? "" : "text-ink-3"}`}>{e.title ?? `Episode ${e.episodeNumber}`}</span>
                        <span className="hidden w-24 shrink-0 text-right text-xs text-ink-3 tabular-nums sm:block">{e.airDate ?? "TBA"}</span>
                        {e.hasFile && (
                          <Button variant="ghost" size="sm" onClick={() => onSubtitles(e)} aria-label={`Subtitles of episode ${e.episodeNumber}`} title="Subtitles">
                            <Icon name="subtitles" size={14} />
                          </Button>
                        )}
                        <span className="w-20 shrink-0 text-right">
                          {e.hasFile ? <Badge tone="ok">On disk</Badge> : !aired ? <Badge>Upcoming</Badge> : <Badge tone={e.monitored ? "warn" : "neutral"}>Missing</Badge>}
                        </span>
                      </li>
                    );
                  })}
                </ol>
              )}
            </div>
          );
        })}
      </div>
    </section>
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
    <Modal onClose={onClose} title={`Remove ${item.title}?`} description="It stops being monitored and leaves your library."
      footer={<>
        <Button variant="ghost" onClick={onClose}>Cancel</Button>
        <Button variant="danger" onClick={remove} disabled={busy}>{busy ? <Spinner /> : deleteFiles ? "Remove and delete files" : "Remove"}</Button>
      </>}>
      <div className="space-y-4 px-5 pb-5">
        <Switch checked={deleteFiles} onChange={setDeleteFiles} label="Also delete files from disk" description={item.path} />
        {error && <Alert>{error}</Alert>}
      </div>
    </Modal>
  );
}

function TitleSkeleton() {
  return (
    <div className="space-y-8 pt-[20vh]">
      <div className="flex items-end gap-8">
        <Skeleton className="aspect-[2/3] w-32 rounded-card sm:w-48" />
        <div className="flex-1 space-y-3">
          <Skeleton className="h-4 w-40" />
          <Skeleton className="h-12 w-2/3" />
          <Skeleton className="h-10 w-72" />
        </div>
      </div>
      <Skeleton className="h-24 max-w-2xl" />
    </div>
  );
}
