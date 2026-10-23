"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import Link from "next/link";
import { Suspense, useCallback, useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { api, errorMessage } from "@/lib/api";
import type { CollectionCard, DiscoveryItem, ExplorePage, ExploreQuery, Kind } from "@/lib/api";
import { LoadMore } from "@/components/ActivityFeed";
import { markAdded } from "@/components/AddDialog";
import { PosterCard, PosterGrid, PosterSkeleton } from "@/components/Poster";
import { SagaCard, SagaModal, sagaCard } from "@/components/Saga";
import { Alert, Button, Chip, Empty, Field, Input, Modal, PageHeader, Segmented, Select, Skeleton, Switch } from "@/components/ui";

type KindFilter = "all" | Kind;
type List = "trending" | "popular" | "top" | "upcoming" | "airing" | "season" | "sagas";

const kinds: readonly { value: KindFilter; label: string }[] = [
  { value: "all", label: "All" },
  { value: "movie", label: "Movies" },
  { value: "series", label: "Series" },
  { value: "anime", label: "Anime" },
];

const year = new Date().getFullYear();
const years = Array.from({ length: year - 1919 }, (_, i) => year + 1 - i);

// Each list is a preset over the same filters; Airing and This season only exist for shows, Sagas (movie collections) only for movies.
const lists: readonly { value: List; label: string; shows?: boolean; movies?: boolean; query?: (kind: Kind) => ExploreQuery }[] = [
  { value: "trending", label: "Trending", query: () => ({ sort: "popularity", yearFrom: year - 1 }) },
  { value: "popular", label: "Popular", query: () => ({ sort: "popularity" }) },
  { value: "top", label: "Top rated", query: () => ({ sort: "rating" }) },
  { value: "upcoming", label: "Upcoming", query: () => ({ sort: "date", status: "upcoming" }) },
  { value: "airing", label: "Airing", shows: true, query: () => ({ sort: "popularity", status: "continuing" }) },
  { value: "season", label: "This season", shows: true, query: () => ({ sort: "popularity", status: "continuing", yearFrom: year, yearTo: year }) },
  { value: "sagas", label: "Sagas", movies: true },
];

const isKind = (value: string | null): value is KindFilter => kinds.some((k) => k.value === value);
const listsFor = (kind: KindFilter) => lists.filter((l) => !(l.shows && kind === "movie") && !(l.movies && kind !== "all" && kind !== "movie"));

function Explore() {
  const params = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const kindParam = params.get("kind");
  const listParam = params.get("list");
  const kind: KindFilter = isKind(kindParam) ? kindParam : "all";
  const list: List = listsFor(kind).find((l) => l.value === listParam)?.value ?? "trending";
  const sagaId = Number(params.get("saga")) || 0;
  const [sagas, setSagas] = useState<CollectionCard[] | null>(null);
  const [foundSagas, setFoundSagas] = useState<{ term: string; cards: CollectionCard[] } | null>(null);
  const genre = params.get("genre") ?? "";
  const picked = Number(params.get("year")) || undefined;
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [genreLists, setGenreLists] = useState<{ kind: KindFilter; genres: string[] } | null>(null);
  const [result, setResult] = useState<{ key: string; items: DiscoveryItem[]; page: number; more: boolean; notes: ExplorePage[] } | null>(null);
  const [error, setError] = useState<string | null>(null);
  // The search palette links here with ?q=; a new q (even while already on Explore) replaces the box's text,
  // unless it is the box's own text coming back from the URL.
  const q = params.get("q") ?? "";
  const [term, setTerm] = useState(q);
  const [lastQ, setLastQ] = useState(q);
  if (q !== lastQ) { setLastQ(q); if (q !== term.trim()) setTerm(q); }
  const typing = useRef<ReturnType<typeof setTimeout>>(undefined);
  const [found, setFound] = useState<{ term: string; items: DiscoveryItem[]; error?: string } | null>(null);
  const searchTerm = term.trim();
  const key = `${kind}|${list}|${genre}|${picked}`;
  // The scroll sentinel can ask again before the next page is in.
  const pending = useRef(false);

  const set = (changes: Record<string, string>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(changes)) {
      if (v) next.set(k, v); else next.delete(k);
    }
    router.replace(`${pathname}?${next}`, { scroll: false });
  };
  // The term and the toggle live in the URL so Back comes back to them; the term after a pause in typing.
  const hideOwned = params.get("hide") === "1";
  const setHideOwned = (on: boolean) => set({ hide: on ? "1" : "" });
  const type = (value: string) => {
    setTerm(value);
    clearTimeout(typing.current);
    typing.current = setTimeout(() => set({ q: value.trim() }), 300);
  };

  useEffect(() => {
    if (kind === "all") return;
    api.exploreGenres(kind).then((genres) => setGenreLists({ kind, genres })).catch(() => undefined);
  }, [kind]);

  const load = useCallback((page: number) => {
    if (list === "sagas" || (page > 1 && pending.current)) return;
    pending.current = true;
    const preset = lists.find((l) => l.value === list) ?? lists[0];
    const targets: Kind[] = kind === "all" ? (preset?.shows ? ["series", "anime"] : ["movie", "series", "anime"]) : [kind];
    Promise.all(targets.map((k) => api.explore(k, { ...preset?.query?.(k),genre: genre || undefined, ...(picked && { yearFrom: picked, yearTo: picked }), page })))
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
  }, [kind, list, genre, picked, key]);

  useEffect(() => load(1), [load]);

  useEffect(() => {
    if (list !== "sagas") return;
    api.collections().then((cards) => { setSagas(cards); setError(null); }).catch((e: unknown) => setError(errorMessage(e)));
  }, [list]);

  // Waits for a pause in typing and drops the answer to an older term.
  useEffect(() => {
    if (!searchTerm) return;
    const abort = new AbortController();
    const timer = setTimeout(() => {
      api.lookup(searchTerm, abort.signal)
        .then((items) => setFound({ term: searchTerm, items }))
        .catch((e: unknown) => { if (!abort.signal.aborted) setFound({ term: searchTerm, items: [], error: errorMessage(e) }); });
      // Matching sagas go first; the top few are read in full for their film counts, one that fails is left out.
      api.lookupCollections(searchTerm, abort.signal)
        .then((summaries) => Promise.all(summaries.slice(0, 4).map((s) => api.collection(s.id).then(sagaCard, () => null))))
        .then((cards) => { if (!abort.signal.aborted) setFoundSagas({ term: searchTerm, cards: cards.filter((c) => c !== null) }); })
        .catch(() => undefined);
    }, 300);
    return () => { clearTimeout(timer); abort.abort(); };
  }, [searchTerm]);

  const current = result?.key === key ? result : null;
  const shown = (current?.items ?? []).filter((i) => !hideOwned || !i.inLibrary);
  const onAdded = (item: DiscoveryItem, id: number | null) => setResult((r) => r && { ...r, items: markAdded(r.items, item, id) });
  const searched = found?.term === searchTerm ? found : null;
  const onFoundAdded = (item: DiscoveryItem, id: number | null) => setFound((f) => f && { ...f, items: markAdded(f.items, item, id) });
  const notes = current?.notes ?? [];
  const sagasFound = foundSagas?.term === searchTerm ? foundSagas.cards : [];
  const onSagaChanged = useCallback((card: CollectionCard) => {
    const swap = (cards: CollectionCard[]) => cards.map((c) => c.id === card.id ? card : c);
    setSagas((s) => s && swap(s));
    setFoundSagas((f) => f && { ...f, cards: swap(f.cards) });
  }, []);
  const sagaGrid = (cards: CollectionCard[]) => (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      {cards.map((c) => <SagaCard key={c.id} saga={c} onOpen={() => set({ saga: String(c.id) })} onChanged={onSagaChanged} />)}
    </div>
  );
  const genres = genreLists?.kind === kind ? genreLists.genres : [];

  const refinements = (genre ? 1 : 0) + (picked ? 1 : 0) + (hideOwned ? 1 : 0);
  const filterFields: readonly [string, ReactNode][] = [
    ...(genres.length > 0 ? [["Genre", (
      <Select key="genre" value={genre} onChange={(e) => set({ genre: e.target.value })} aria-label="Genre">
        <option value="">All genres</option>
        {genres.map((g) => <option key={g} value={g}>{g}</option>)}
      </Select>
    )] as [string, ReactNode]] : []),
    ["Year", (
      <Select key="year" value={picked ?? ""} onChange={(e) => set({ year: e.target.value })} aria-label="Year">
        <option value="">Any year</option>
        {years.map((y) => <option key={y} value={y}>{y}</option>)}
      </Select>
    )],
  ];
  const clear = () => set({ genre: "", list: "", year: "", hide: "" });

  return (
    <>
      <PageHeader title="Explore" />
      <div className="mb-6 flex flex-wrap items-center gap-2">
        <Input icon="search" type="search" value={term} onChange={(e) => type(e.target.value)} placeholder="Search by name" aria-label="Search movies and shows by name" className="min-w-56 flex-1" />
        {!searchTerm && <>
          <Segmented label="Kind" value={kind} onChange={(k) => set({ kind: k === "all" ? "" : k, genre: "" })} options={kinds} />
          <div className="w-40">
            <Select value={list} onChange={(e) => set({ list: e.target.value === "trending" ? "" : e.target.value })} aria-label="List">
              {listsFor(kind).map((l) => <option key={l.value} value={l.value}>{l.label}</option>)}
            </Select>
          </div>
          {/* Inline from md up, inside the Filters sheet on phones; sagas take no filter. */}
          {list !== "sagas" && <>
            {filterFields.map(([label, field]) => <div key={label} className="hidden w-40 md:block">{field}</div>)}
            <Chip className="max-md:hidden" selected={hideOwned} onClick={() => setHideOwned(!hideOwned)}>Hide in library</Chip>
            <Button className="md:hidden" icon="sliders" onClick={() => setFiltersOpen(true)}>
              Filters{refinements > 0 && <span className="text-ink-3 tabular-nums">{refinements}</span>}
            </Button>
          </>}
        </>}
      </div>
      {searchTerm && sagasFound.length > 0 && <div className="mb-6">{sagaGrid(sagasFound)}</div>}
      {list === "sagas" && !searchTerm ? (
        error ? <Alert>{error}</Alert>
          : !sagas ? <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">{[0, 1, 2].map((i) => <Skeleton key={i} className="aspect-video rounded-tile" />)}</div>
          : sagas.length === 0 ? <Empty icon="layers" title="No saga to show." />
          : sagaGrid(sagas)
      ) : searchTerm ? (
        !searched ? (
          <PosterGrid>{Array.from({ length: 12 }, (_, i) => <PosterSkeleton key={i} />)}</PosterGrid>
        ) : searched.error ? (
          <Alert>{searched.error}</Alert>
        ) : searched.items.length === 0 ? (
          <Empty icon="compass" title={`Nothing found for “${searchTerm}”.`} />
        ) : (
          <PosterGrid>
            {searched.items.map((item) => <PosterCard key={`${item.kind}${item.tmdbId}${item.tvdbId}`} item={item} onAdded={onFoundAdded} />)}
          </PosterGrid>
        )
      ) : <>
        {error && <div className="mb-4"><Alert>{error}</Alert></div>}
        {notes.some((n) => n.loading || n.limited) && (
          <div className="mb-4"><Alert tone="info">
            {notes.some((n) => n.loading) && "The full series catalogue is still loading. "}
            {notes.some((n) => n.limited) && <>Movies come from a short list without a TMDB key. <Link href="/settings/#connections" className="font-semibold text-ink underline underline-offset-2">Add a TMDB key</Link> for the full catalogue.</>}
          </Alert></div>
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
      </>}

      {sagaId !== 0 && <SagaModal key={sagaId} id={sagaId} onClose={() => set({ saga: "" })} onChanged={onSagaChanged} />}

      {filtersOpen && (
        <Modal title="Filters" onClose={() => setFiltersOpen(false)}
          footer={<>
            <Button variant="ghost" onClick={clear}>Reset</Button>
            <Button variant="primary" onClick={() => setFiltersOpen(false)}>Done</Button>
          </>}>
          <div className="space-y-4 px-5 pb-5">
            {filterFields.map(([label, field]) => <Field key={label} label={label}>{field}</Field>)}
            <Switch checked={hideOwned} onChange={setHideOwned} label="Hide in library" />
          </div>
        </Modal>
      )}
    </>
  );
}

export default function ExploreRoute() {
  return <Suspense><Explore /></Suspense>;
}
