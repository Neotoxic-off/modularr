"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import Link from "next/link";
import { Suspense, useCallback, useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { api, errorMessage } from "@/lib/api";
import type { DiscoveryItem, ExplorePage, ExploreQuery, Kind } from "@/lib/api";
import { LoadMore } from "@/components/ActivityFeed";
import { markAdded } from "@/components/AddDialog";
import { PosterCard, PosterGrid, PosterSkeleton } from "@/components/Poster";
import { Alert, Button, Chip, Empty, Field, Modal, PageHeader, Segmented, Select, Switch } from "@/components/ui";

type KindFilter = "all" | Kind;
type List = "trending" | "popular" | "top" | "upcoming" | "airing" | "season";

const kinds: readonly { value: KindFilter; label: string }[] = [
  { value: "all", label: "All" },
  { value: "movie", label: "Movies" },
  { value: "series", label: "Series" },
  { value: "anime", label: "Anime" },
];

const year = new Date().getFullYear();
const years = Array.from({ length: year - 1919 }, (_, i) => year + 1 - i);

// Each list is a preset over the same filters; Airing and This season only exist for shows.
const lists: readonly { value: List; label: string; shows?: boolean; query: (kind: Kind) => ExploreQuery }[] = [
  { value: "trending", label: "Trending", query: () => ({ sort: "popularity", yearFrom: year - 1 }) },
  { value: "popular", label: "Popular", query: () => ({ sort: "popularity" }) },
  { value: "top", label: "Top rated", query: () => ({ sort: "rating" }) },
  { value: "upcoming", label: "Upcoming", query: () => ({ sort: "date", status: "upcoming" }) },
  { value: "airing", label: "Airing", shows: true, query: () => ({ sort: "popularity", status: "continuing" }) },
  { value: "season", label: "This season", shows: true, query: () => ({ sort: "popularity", status: "continuing", yearFrom: year, yearTo: year }) },
];

const isKind = (value: string | null): value is KindFilter => kinds.some((k) => k.value === value);
const isList = (value: string | null): value is List => lists.some((l) => l.value === value);

function Explore() {
  const params = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const kindParam = params.get("kind");
  const listParam = params.get("list");
  const kind: KindFilter = isKind(kindParam) ? kindParam : "all";
  const list: List = isList(listParam) && !(kind === "movie" && lists.find((l) => l.value === listParam)?.shows) ? listParam : "trending";
  const genre = params.get("genre") ?? "";
  const yearFrom = Number(params.get("yearFrom")) || undefined;
  const yearTo = Number(params.get("yearTo")) || undefined;
  const [hideOwned, setHideOwned] = useState(false);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [genreLists, setGenreLists] = useState<{ kind: KindFilter; genres: string[] } | null>(null);
  const [result, setResult] = useState<{ key: string; items: DiscoveryItem[]; page: number; more: boolean; notes: ExplorePage[] } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const key = `${kind}|${list}|${genre}|${yearFrom}|${yearTo}`;
  // The scroll sentinel can ask again before the next page is in.
  const pending = useRef(false);

  const set = (changes: Record<string, string>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(changes)) {
      if (v) next.set(k, v); else next.delete(k);
    }
    router.replace(`${pathname}?${next}`, { scroll: false });
  };

  useEffect(() => {
    if (kind === "all") return;
    api.exploreGenres(kind).then((genres) => setGenreLists({ kind, genres })).catch(() => undefined);
  }, [kind]);

  const load = useCallback((page: number) => {
    if (page > 1 && pending.current) return;
    pending.current = true;
    const preset = lists.find((l) => l.value === list) ?? lists[0];
    const targets: Kind[] = kind === "all" ? (preset?.shows ? ["series", "anime"] : ["movie", "series", "anime"]) : [kind];
    Promise.all(targets.map((k) => api.explore(k, { ...preset?.query(k), genre: genre || undefined, ...(yearFrom && { yearFrom }), ...(yearTo && { yearTo }), page })))
      .then((pages) => {
        // "All" interleaves the kinds so no single one floods the grid.
        const merged = Array.from({ length: Math.max(...pages.map((p) => p.items.length)) }, (_, i) => pages.flatMap((p) => p.items[i] ?? [])).flat();
        setResult((r) => ({
          key,
          items: r && r.key === key && page > 1 ? [...r.items, ...merged] : merged,
          page,
          more: pages.some((p) => p.hasMore),
          notes: pages,
        }));
        setError(null);
      })
      .catch((e: unknown) => setError(errorMessage(e)))
      .finally(() => { pending.current = false; });
  }, [kind, list, genre, yearFrom, yearTo, key]);

  useEffect(() => load(1), [load]);

  const current = result?.key === key ? result : null;
  const shown = (current?.items ?? []).filter((i) => !hideOwned || !i.inLibrary);
  const onAdded = (item: DiscoveryItem, id: number | null) => setResult((r) => r && { ...r, items: markAdded(r.items, item, id) });
  const notes = current?.notes ?? [];
  const genres = genreLists?.kind === kind ? genreLists.genres : [];

  const refinements = (genre ? 1 : 0) + (yearFrom || yearTo ? 1 : 0) + (hideOwned ? 1 : 0);
  const yearSelect = (label: string, param: "yearFrom" | "yearTo", value: number | undefined) => (
    <Select key={param} value={value ?? ""} onChange={(e) => set({ [param]: e.target.value })} aria-label={label}>
      <option value="">{param === "yearFrom" ? "Any start" : "Any end"}</option>
      {years.map((y) => <option key={y} value={y}>{y}</option>)}
    </Select>
  );
  // Inline from md up, inside the Filters sheet on phones.
  const filterFields: readonly [string, ReactNode][] = [
    ...(genres.length > 0 ? [["Genre", (
      <Select key="genre" value={genre} onChange={(e) => set({ genre: e.target.value })} aria-label="Genre">
        <option value="">All genres</option>
        {genres.map((g) => <option key={g} value={g}>{g}</option>)}
      </Select>
    )] as [string, ReactNode]] : []),
    ["From", yearSelect("From year", "yearFrom", yearFrom)],
    ["To", yearSelect("To year", "yearTo", yearTo)],
  ];
  const hide = <Switch checked={hideOwned} onChange={setHideOwned} label="Hide in library" />;
  const clear = () => { setHideOwned(false); set({ genre: "", list: "", yearFrom: "", yearTo: "" }); };

  return (
    <>
      <PageHeader title="Explore" description="Find something new. The + adds a title with your defaults." />
      <div className="mb-6 space-y-4">
        <Segmented label="Kind" value={kind} onChange={(k) => set({ kind: k === "all" ? "" : k, genre: "" })} options={kinds} />
        <div role="group" aria-label="List" className="rail -mx-4 flex scroll-px-4 gap-2 overflow-x-auto px-4 sm:mx-0 sm:flex-wrap sm:px-0">
          {lists.filter((l) => !l.shows || kind !== "movie").map((l) => (
            <Chip key={l.value} selected={list === l.value} onClick={() => set({ list: l.value === "trending" ? "" : l.value })}>{l.label}</Chip>
          ))}
        </div>
        <div className="flex items-center gap-3 max-md:hidden">
          <div className="grid grid-cols-[repeat(3,minmax(0,150px))] gap-3 lg:grid-cols-[180px_130px_130px]">{filterFields.map(([, field]) => field)}</div>
          <div className="ml-auto w-48">{hide}</div>
        </div>
        <Button className="md:hidden" icon="sliders" onClick={() => setFiltersOpen(true)}>
          Filters{refinements > 0 && <span className="text-ink-3 tabular-nums">{refinements}</span>}
        </Button>
      </div>

      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      {notes.some((n) => n.loading) && <p className="well mb-4 rounded-card px-4 py-3 text-[13px] text-ink-2">The full series catalogue is still loading. More titles appear over the next few minutes.</p>}
      {notes.some((n) => n.limited) && (
        <p className="well mb-4 rounded-card px-4 py-3 text-[13px] text-ink-2">
          Movies come from a short list without a TMDB key. <Link href="/settings/#general" className="font-semibold text-ink underline underline-offset-2">Add a TMDB key</Link> for the full catalogue.
        </p>
      )}
      {notes.map((n) => n.error).filter(Boolean).map((e) => <div key={e} className="mb-4"><Alert tone="warn">{e}</Alert></div>)}

      {!current ? (
        <PosterGrid>{Array.from({ length: 18 }, (_, i) => <PosterSkeleton key={i} />)}</PosterGrid>
      ) : shown.length === 0 ? (
        <Empty icon="compass" title="Nothing matches.">
          <Button variant="ghost" className="mt-2" onClick={clear}>Clear filters</Button>
        </Empty>
      ) : (
        <>
          <PosterGrid>
            {shown.map((item) => <PosterCard key={`${item.kind}${item.tmdbId}${item.tvdbId}`} item={item} onAdded={onAdded} />)}
          </PosterGrid>
          {current.more && <div className="pt-6"><LoadMore onMore={() => load(current.page + 1)} /></div>}
        </>
      )}

      {filtersOpen && (
        <Modal title="Filters" onClose={() => setFiltersOpen(false)}
          footer={<>
            <Button variant="ghost" onClick={clear}>Reset</Button>
            <Button variant="primary" onClick={() => setFiltersOpen(false)}>Done</Button>
          </>}>
          <div className="space-y-4 px-5 pb-5">
            {filterFields.map(([label, field]) => <Field key={label} label={label}>{field}</Field>)}
            {hide}
          </div>
        </Modal>
      )}
    </>
  );
}

export default function ExploreRoute() {
  return <Suspense><Explore /></Suspense>;
}
