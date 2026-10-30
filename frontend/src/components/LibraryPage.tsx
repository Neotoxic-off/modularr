"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { api, errorMessage, isSeries, kindOf, libraryHref, titleHref } from "@/lib/api";
import type { ActivityKind, Download, Kind, LibraryItem } from "@/lib/api";
import { isActive, useActivity, useActivityRefresh } from "@/components/Activity";
import { useSession } from "@/components/AuthGate";
import { PosterCard, PosterGrid, PosterSkeleton } from "@/components/Poster";
import { useToast } from "@/components/Toasts";
import { Alert, Button, ButtonLink, Empty, Field, Input, Menu, MenuItem, Modal, PageHeader, Segmented, Select } from "@/components/ui";
import type { Status } from "@/components/ui";

const statuses = ["all", "missing", "complete", "unmonitored"] as const;
type StatusFilter = (typeof statuses)[number];
const statusLabels: Record<StatusFilter, string> = { all: "All", missing: "Missing", complete: "Complete", unmonitored: "Unmonitored" };

const sorts = { added: "Recently added", title: "Title", year: "Year", rating: "Rating" } as const;
type Sort = keyof typeof sorts;

const titles: Record<Kind, string> = { movie: "Movies", series: "Series", anime: "Anime" };
const nouns: Record<Kind, string> = { movie: "movies", series: "series", anime: "anime" };

const isStatus = (value: string | null): value is StatusFilter => statuses.some((s) => s === value);
const isSort = (value: string | null): value is Sort => value !== null && value in sorts;

const sorters: Record<Sort, (a: LibraryItem, b: LibraryItem) => number> = {
  added: (a, b) => b.id - a.id,
  title: (a, b) => a.title.localeCompare(b.title),
  year: (a, b) => b.year - a.year,
  rating: (a, b) => (b.rating ?? 0) - (a.rating ?? 0),
};

// A series is missing only when an aired, monitored episode has no file; one waiting for its premiere is not.
// Events that change what the grid shows: a title's files, its download state, or the titles themselves.
const gridKinds: ReadonlySet<ActivityKind> = new Set(["Added", "Removed", "Grabbed", "Imported", "DownloadFailed", "ImportFailed", "ScanCompleted"]);

const isMissing = (item: LibraryItem) => item.monitored && !item.hasFile && (!isSeries(item) || item.missing > 0);

export function LibraryPage({ kind }: { kind: Kind }) {
  return <Suspense><Library kind={kind} /></Suspense>;
}

function Library({ kind }: { kind: Kind }) {
  const params = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const toast = useToast();
  const { user } = useSession();
  const { epoch, progress } = useActivity();
  const [items, setItems] = useState<LibraryItem[] | null>(null);
  const [downloads, setDownloads] = useState<Download[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [term, setTerm] = useState(() => params.get("q") ?? "");
  const typing = useRef<ReturnType<typeof setTimeout>>(undefined);
  const [scanning, setScanning] = useState(false);
  const [searching, setSearching] = useState(false);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [reload, setReload] = useState(0);

  const statusParam = params.get("status");
  const sortParam = params.get("sort");
  const status: StatusFilter = isStatus(statusParam) ? statusParam : "all";
  const sort: Sort = isSort(sortParam) ? sortParam : "added";
  const genre = params.get("genre") ?? "";
  const year = Number(params.get("year")) || 0;

  const set = (changes: Record<string, string>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(changes)) {
      if (v) next.set(k, v); else next.delete(k);
    }
    const query = next.toString();
    router.replace(query ? `${pathname}?${query}` : pathname, { scroll: false });
  };
  // The filter text goes into the URL after a pause in typing, so Back comes back to it.
  const type = (value: string) => {
    setTerm(value);
    clearTimeout(typing.current);
    typing.current = setTimeout(() => set({ q: value.trim() }), 300);
  };

  useEffect(() => {
    (kind === "movie" ? api.movies() : api.series())
      .then((all) => setItems(all.filter((i) => kindOf(i) === kind)))
      .catch((e: unknown) => setError(errorMessage(e)));
  }, [kind, epoch, reload]);

  useEffect(() => {
    api.downloads().then((d) => setDownloads(d.filter(isActive))).catch(() => undefined);
  }, [epoch, reload]);

  useActivityRefresh((a) => gridKinds.has(a.kind), () => setReload((n) => n + 1));

  async function rescan() {
    setScanning(true);
    try {
      const result = await api.rescanLibrary();
      toast({ key: "library-scan", icon: "check", tone: "ok", title: "Library rescanned",
        message: `${result.items} titles checked: ${result.added} new ${result.added === 1 ? "file" : "files"}, ${result.removed} gone from disk.` });
      setReload((n) => n + 1);
    } catch (e) {
      toast({ key: "library-scan", icon: "alert", tone: "bad", title: "Rescan failed", message: errorMessage(e) });
    } finally {
      setScanning(false);
    }
  }

  // One search per title, one after the other so the indexers are not hit all at once; one toast for the lot.
  async function searchAll(list: LibraryItem[]) {
    setSearching(true);
    toast({ key: "library-search", icon: "search", tone: "seek", title: `Searching ${list.length} ${list.length === 1 ? "title" : "titles"}`, message: "Looking for releases…" });
    let failed = 0;
    for (const item of list) {
      try {
        await api.autoSearch(kind, item.id);
      } catch {
        failed++;
      }
    }
    toast(failed === 0
      ? { key: "library-search", icon: "check", tone: "ok", title: `Searched ${list.length} ${list.length === 1 ? "title" : "titles"}`, message: "Grabs show up in Downloads." }
      : { key: "library-search", icon: "alert", tone: "bad", title: "Search all", message: `${failed} of ${list.length} searches could not start.` });
    setSearching(false);
  }

  const downloadOf = (item: LibraryItem) => downloads.find((d) => (isSeries(item) ? d.seriesId : d.movieId) === item.id);
  const statusOf = (item: LibraryItem): Status | undefined => {
    const download = downloadOf(item);
    if (download) return { state: "downloading", progress: progress.get(download.id)?.progress ?? download.progress ?? 0 };
    if (!item.monitored) return { state: "unmonitored" };
    if (item.hasFile) return { state: "library" };
    return isMissing(item) ? { state: "missing" } : undefined;
  };
  const matches: Record<StatusFilter, (item: LibraryItem) => boolean> = {
    all: () => true,
    missing: isMissing,
    complete: (item) => item.hasFile,
    unmonitored: (item) => !item.monitored,
  };

  const all = items ?? [];
  const needle = term.trim().toLowerCase();
  const refined = all
    .filter((i) => !needle || i.title.toLowerCase().includes(needle))
    .filter((i) => !genre || i.genres.includes(genre))
    .filter((i) => !year || i.year === year);
  const shown = refined.filter(matches[status]).sort(sorters[sort]);
  const genres = [...new Set(all.flatMap((i) => i.genres))].sort();
  const years = [...new Set(all.map((i) => i.year).filter((y) => y > 0))].sort((a, b) => b - a);
  const refinements = (genre ? 1 : 0) + (year ? 1 : 0) + (sort !== "added" ? 1 : 0);
  const isAdmin = user.role === "Admin";

  // Inline from md up, inside the Filters sheet on phones.
  const filterFields: readonly [string, ReactNode][] = [
    ["Genre", (
      <Select key="genre" value={genre} onChange={(e) => set({ genre: e.target.value })} aria-label="Genre">
        <option value="">All genres</option>
        {genres.map((g) => <option key={g} value={g}>{g}</option>)}
      </Select>
    )],
    ["Year", (
      <Select key="year" value={year || ""} onChange={(e) => set({ year: e.target.value })} aria-label="Year">
        <option value="">Any year</option>
        {years.map((y) => <option key={y} value={y}>{y}</option>)}
      </Select>
    )],
    ["Sort by", (
      <Select key="sort" value={sort} onChange={(e) => set({ sort: e.target.value === "added" ? "" : e.target.value })} aria-label="Sort by">
        {Object.entries(sorts).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
      </Select>
    )],
  ];

  return (
    <>
      {/* Phone: the Library tab lands here; the three libraries are one switch away. */}
      <Segmented label="Library" value={kind} className="mb-4 md:hidden" onChange={(k) => router.push(libraryHref[k])}
        options={(["movie", "series", "anime"] as const).map((k) => ({ value: k, label: titles[k] }))} />
      <PageHeader title={titles[kind]}>
        {isAdmin && (
          <Menu label="More library actions">
            <MenuItem icon="refresh" onClick={rescan} disabled={scanning}>{scanning ? "Rescanning…" : "Rescan library"}</MenuItem>
            <MenuItem icon="folder" href="/import/">Import existing folders</MenuItem>
          </Menu>
        )}
      </PageHeader>

      <div className="mb-6 flex flex-wrap items-center gap-3">
        <Segmented label="Status" value={status} onChange={(s) => set({ status: s === "all" ? "" : s })}
          options={statuses.map((s) => ({ value: s, label: statusLabels[s] }))} />
        <Input icon="search" value={term} onChange={(e) => type(e.target.value)} placeholder={`Filter ${nouns[kind]}`} aria-label={`Filter ${nouns[kind]}`} className="flex-1 basis-48 md:max-w-72" />
        <div className="ml-auto hidden gap-3 md:grid md:grid-cols-[180px_130px_170px]">{filterFields.map(([, field]) => field)}</div>
        {isAdmin && status === "missing" && shown.length > 0 && (
          <Button icon="search" onClick={() => void searchAll(shown)} loading={searching}>Search all ({shown.length})</Button>
        )}
        <Button className="md:hidden" icon="sliders" onClick={() => setFiltersOpen(true)}>
          Filters{refinements > 0 && <span className="text-ink-3 tabular-nums">{refinements}</span>}
        </Button>
      </div>

      {error && <div className="mb-6"><Alert>{error}</Alert></div>}

      {!items ? (
        <PosterGrid>{Array.from({ length: 12 }, (_, i) => <PosterSkeleton key={i} />)}</PosterGrid>
      ) : items.length === 0 ? (
        <Empty icon={kind === "movie" ? "film" : kind === "series" ? "tv" : "sparkles"} title={`No ${nouns[kind]} yet.`}>
          <div className="mt-3 flex flex-wrap justify-center gap-2">
            {isAdmin && <ButtonLink href="/explore/" variant="primary" icon="compass">Explore</ButtonLink>}
            {isAdmin && <ButtonLink href="/import/" icon="folder">Import existing</ButtonLink>}
          </div>
        </Empty>
      ) : shown.length === 0 ? (
        <Empty icon="search" title="Nothing matches.">
          <Button variant="ghost" className="mt-2" onClick={() => { setTerm(""); set({ status: "", genre: "", year: "", q: "" }); }}>Clear filters</Button>
        </Empty>
      ) : (
        <PosterGrid>
          {shown.map((item) => (
            <PosterCard key={item.id} href={titleHref(kind, item.id)} title={item.title} year={item.year} poster={item.poster} rating={item.rating}
              fraction={isSeries(item) ? `${item.episodesWithFile}/${item.episodes}` : undefined} status={statusOf(item)} />
          ))}
        </PosterGrid>
      )}

      {filtersOpen && (
        <Modal title="Filters" onClose={() => setFiltersOpen(false)}
          footer={<>
            <Button variant="ghost" onClick={() => set({ genre: "", year: "", sort: "" })}>Reset</Button>
            <Button variant="primary" onClick={() => setFiltersOpen(false)}>Show {shown.length} {shown.length === 1 ? "title" : "titles"}</Button>
          </>}>
          <div className="space-y-4 px-5 pb-5">
            {filterFields.map(([label, field]) => <Field key={label} label={label}>{field}</Field>)}
          </div>
        </Modal>
      )}
    </>
  );
}
