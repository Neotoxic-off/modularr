"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback, useEffect, useRef, useState } from "react";
import { api, errorMessage } from "@/lib/api";
import type { DiscoveryItem, ExplorePage, ExploreQuery, Kind } from "@/lib/api";
import { LoadMore } from "@/components/ActivityFeed";
import { markAdded } from "@/components/AddDialog";
import { PosterCard, PosterGrid, PosterSkeleton } from "@/components/Poster";
import { Alert, Button, Chip, Empty, PageHeader, Segmented, Select, Switch } from "@/components/ui";

type KindFilter = "all" | Kind;
type List = "trending" | "popular" | "top" | "upcoming" | "airing" | "season";

const kinds: readonly { value: KindFilter; label: string }[] = [
  { value: "all", label: "All" },
  { value: "movie", label: "Movies" },
  { value: "series", label: "Series" },
  { value: "anime", label: "Anime" },
];

const year = new Date().getFullYear();

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
  const [hideOwned, setHideOwned] = useState(false);
  const [genreLists, setGenreLists] = useState<{ kind: KindFilter; genres: string[] } | null>(null);
  const [result, setResult] = useState<{ key: string; items: DiscoveryItem[]; page: number; more: boolean; notes: ExplorePage[] } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const key = `${kind}|${list}|${genre}`;
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
    Promise.all(targets.map((k) => api.explore(k, { ...preset?.query(k), genre: genre || undefined, page })))
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
  }, [kind, list, genre, key]);

  useEffect(() => load(1), [load]);

  const current = result?.key === key ? result : null;
  const shown = (current?.items ?? []).filter((i) => !hideOwned || !i.inLibrary);
  const onAdded = (item: DiscoveryItem, id: number | null) => setResult((r) => r && { ...r, items: markAdded(r.items, item, id) });
  const notes = current?.notes ?? [];
  const genres = genreLists?.kind === kind ? genreLists.genres : [];

  return (
    <>
      <PageHeader title="Explore" description="Find something new. The + adds a title with your defaults." />
      <div className="mb-6 space-y-4">
        <Segmented label="Kind" value={kind} onChange={(k) => set({ kind: k === "all" ? "" : k, genre: "" })} options={kinds} />
        <div className="flex flex-wrap items-center gap-3">
          <div role="group" aria-label="List" className="rail -mx-4 flex max-w-full gap-2 overflow-x-auto px-4 sm:mx-0 sm:px-0">
            {lists.filter((l) => !l.shows || kind !== "movie").map((l) => (
              <Chip key={l.value} selected={list === l.value} onClick={() => set({ list: l.value === "trending" ? "" : l.value })}>{l.label}</Chip>
            ))}
          </div>
          <div className="flex w-full flex-wrap items-center gap-3 sm:ml-auto sm:w-auto">
            {genres.length > 0 && (
              <Select value={genre} onChange={(e) => set({ genre: e.target.value })} aria-label="Genre" className="min-w-40 flex-1 sm:flex-none">
                <option value="">All genres</option>
                {genres.map((g) => <option key={g} value={g}>{g}</option>)}
              </Select>
            )}
            <div className="w-44"><Switch checked={hideOwned} onChange={setHideOwned} label="Hide in library" /></div>
          </div>
        </div>
      </div>

      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      {notes.some((n) => n.loading) && <p className="well mb-4 rounded-card px-4 py-3 text-[13px] text-ink-2">The full series catalogue is still loading. More titles appear over the next few minutes.</p>}
      {notes.some((n) => n.limited) && <p className="well mb-4 rounded-card px-4 py-3 text-[13px] text-ink-2">Movie filters are limited without a TMDB key. Add one in Settings for the full catalogue.</p>}
      {notes.map((n) => n.error).filter(Boolean).map((e) => <div key={e} className="mb-4"><Alert tone="warn">{e}</Alert></div>)}

      {!current ? (
        <PosterGrid>{Array.from({ length: 18 }, (_, i) => <PosterSkeleton key={i} />)}</PosterGrid>
      ) : shown.length === 0 ? (
        <Empty icon="compass" title="Nothing matches.">
          <Button variant="ghost" className="mt-2" onClick={() => { setHideOwned(false); set({ genre: "", list: "" }); }}>Clear filters</Button>
        </Empty>
      ) : (
        <>
          <PosterGrid>
            {shown.map((item) => <PosterCard key={`${item.kind}${item.tmdbId}${item.tvdbId}`} item={item} onAdded={onAdded} />)}
          </PosterGrid>
          {current.more && <div className="pt-6"><LoadMore onMore={() => load(current.page + 1)} /></div>}
        </>
      )}
    </>
  );
}

export default function ExploreRoute() {
  return <Suspense><Explore /></Suspense>;
}
