"use client";

import { useEffect, useState } from "react";
import { api, errorMessage, isSeries, kindOf, titleHref } from "@/lib/api";
import type { Kind, LibraryItem } from "@/lib/api";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { LibraryCard, overlayTag, PosterGrid, PosterSkeleton } from "@/components/Poster";
import { Alert, Button, Empty, Input, PageHeader, Select, Spinner, Tabs } from "@/components/ui";

type Filter = "all" | "missing" | "downloaded";
type Sort = "title" | "added" | "year";

const titles: Record<Kind, string> = { movie: "Movies", series: "Series", anime: "Anime" };
const nouns: Record<Kind, string> = { movie: "movie", series: "series", anime: "anime" };

export function LibraryPage({ kind }: { kind: Kind }) {
  const [items, setItems] = useState<LibraryItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>("all");
  const [sort, setSort] = useState<Sort>("added");
  const [term, setTerm] = useState("");
  const [scan, setScan] = useState<{ busy: boolean; notice: string | null }>({ busy: false, notice: null });
  const [reload, setReload] = useState(0);
  const { user } = useSession();

  useEffect(() => {
    (kind === "movie" ? api.movies() : api.series())
      .then((all) => setItems(all.filter((i) => kindOf(i) === kind)))
      .catch((e: unknown) => setError(errorMessage(e)));
  }, [kind, reload]);

  async function rescan() {
    setScan({ busy: true, notice: null });
    setError(null);
    try {
      const result = await api.rescanLibrary();
      setScan({ busy: false, notice: `Scanned ${result.items} titles: ${result.added} new ${result.added === 1 ? "file" : "files"}, ${result.removed} gone from disk.` });
      setReload((n) => n + 1);
    } catch (e) {
      setError(errorMessage(e));
      setScan({ busy: false, notice: null });
    }
  }

  const complete = (item: LibraryItem): boolean | undefined =>
    isSeries(item) ? item.episodes > 0 && item.episodesWithFile >= item.episodes : item.hasFile ?? undefined;

  const needle = term.trim().toLowerCase();
  const shown = (items ?? [])
    .filter((i) => !needle || i.title.toLowerCase().includes(needle))
    .filter((i) => filter === "all" || (filter === "downloaded") === complete(i))
    .sort((a, b) => sort === "title" ? a.title.localeCompare(b.title) : sort === "year" ? b.year - a.year : b.id - a.id);

  const series = (items ?? []).filter(isSeries);
  const episodes = series.reduce((n, s) => n + s.episodes, 0);
  const withFile = series.reduce((n, s) => n + s.episodesWithFile, 0);

  return (
    <>
      <PageHeader title={titles[kind]} description={items && (
        <span className="flex flex-wrap gap-x-4 gap-y-1">
          <span><span className="text-foreground tabular-nums">{items.length}</span> {items.length === 1 ? "title" : "titles"}</span>
          <span><span className="text-foreground tabular-nums">{items.filter((i) => i.monitored).length}</span> monitored</span>
          {kind !== "movie" && <span><span className="text-foreground tabular-nums">{withFile}</span> of {episodes} episodes on disk</span>}
        </span>
      )}>
        {user.role === "Admin" && (
          <Button onClick={rescan} disabled={scan.busy} title="Look for files added or deleted in every library folder">
            {scan.busy ? <Spinner /> : <Icon name="refresh" size={16} />}Rescan library
          </Button>
        )}
      </PageHeader>

      <div className="mb-6 flex flex-wrap items-center gap-3">
        <Tabs label="Filter" value={filter} onChange={setFilter} options={[
          { value: "all", label: "All" },
          { value: "missing", label: "Missing" },
          { value: "downloaded", label: "Downloaded" },
        ]} />
        <div className="relative min-w-40 flex-1 sm:max-w-64">
          <Icon name="search" size={16} className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-muted" />
          <Input value={term} onChange={(e) => setTerm(e.target.value)} placeholder={`Filter ${titles[kind].toLowerCase()}`} aria-label={`Filter ${titles[kind].toLowerCase()}`} className="pl-9" />
        </div>
        <Select value={sort} onChange={(e) => setSort(e.target.value as Sort)} aria-label="Sort by" className="ml-auto w-40">
          <option value="added">Recently added</option>
          <option value="title">Title</option>
          <option value="year">Year</option>
        </Select>
      </div>

      {error && <div className="mb-6"><Alert>{error}</Alert></div>}
      {scan.notice && <p role="status" className="mb-6 flex animate-fade items-center gap-2 rounded-lg bg-accent/10 px-4 py-2.5 text-sm text-accent"><Icon name="check" size={16} />{scan.notice}</p>}

      {!items ? (
        <PosterGrid>{Array.from({ length: 12 }, (_, i) => <PosterSkeleton key={i} />)}</PosterGrid>
      ) : items.length === 0 ? (
        <Empty icon={kind === "movie" ? "film" : kind === "series" ? "tv" : "sparkles"} title={`No ${nouns[kind]} yet`}>
          <p>Find something on Discover or press Ctrl K to search, then add it here.</p>
        </Empty>
      ) : shown.length === 0 ? (
        <Empty icon="search" title="Nothing matches these filters">
          <Button variant="ghost" className="mt-2" onClick={() => { setFilter("all"); setTerm(""); }}>Clear filters</Button>
        </Empty>
      ) : (
        <PosterGrid>
          {shown.map((item) => {
            const done = complete(item);
            return (
              <LibraryCard key={item.id} href={titleHref(kind, item.id)} title={item.title} year={item.year} poster={item.poster} rating={item.rating}
                progress={isSeries(item) && item.episodes > 0 ? item.episodesWithFile / item.episodes : undefined}
                status={!item.monitored ? <span className={`text-white/80 ${overlayTag}`}>Unmonitored</span>
                  : isSeries(item) ? undefined
                  : <span className={`${done ? "text-emerald-300" : "text-amber-200"} ${overlayTag}`}>{done ? "Downloaded" : "Missing"}</span>} />
            );
          })}
        </PosterGrid>
      )}
    </>
  );
}
