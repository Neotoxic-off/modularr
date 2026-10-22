"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { api, errorMessage, kindOfDiscovery, titleHref } from "@/lib/api";
import type { DiscoveryItem, Kind } from "@/lib/api";
import { markAdded, useAddDialog, useAddTitle } from "@/components/AddDialog";
import { Icon } from "@/components/icons";
import { DiscoveryCard, kindLabel, PosterSkeleton } from "@/components/Poster";
import { Button, Rating, Skeleton, Spinner } from "@/components/ui";

const rows: readonly { key: string; title: string; kind: Kind; list: string; href: string }[] = [
  { key: "movies", title: "Trending movies", kind: "movie", list: "trending", href: "/movies/" },
  { key: "series", title: "Popular series", kind: "series", list: "popular", href: "/series/" },
  { key: "anime", title: "Trending anime", kind: "anime", list: "trending", href: "/anime/" },
  { key: "upcoming", title: "Upcoming movies", kind: "movie", list: "upcoming", href: "/movies/" },
];

type Lists = Record<string, { items: DiscoveryItem[]; error: string | null } | undefined>;

export default function DiscoverPage() {
  const [lists, setLists] = useState<Lists>({});
  const openAdd = useAddDialog();

  useEffect(() => {
    for (const row of rows) {
      api.discover(row.kind, row.list)
        .then((list) => setLists((l) => ({ ...l, [row.key]: list })))
        .catch((e: unknown) => setLists((l) => ({ ...l, [row.key]: { items: [], error: errorMessage(e) } })));
    }
  }, []);

  // Every row shows the same title the same way once it is added.
  const added = (item: DiscoveryItem, id: number) =>
    setLists((l) => Object.fromEntries(Object.entries(l).map(([k, v]) => [k, v && { ...v, items: markAdded(v.items, item, id) }])));

  const add = (item: DiscoveryItem) => openAdd(item, added);

  const featured = [...(lists.movies?.items ?? []).slice(0, 3), ...(lists.series?.items ?? []).slice(0, 2), ...(lists.anime?.items ?? []).slice(0, 1)]
    .filter((i) => i.backdrop);

  return (
    <div className="space-y-12">
      <Hero items={featured} loading={!lists.movies} onAdd={add} onAdded={added} />
      {rows.map((row) => (
        <Row key={row.key} title={row.title} href={row.href} list={lists[row.key]} onAdd={add} onAdded={added} />
      ))}
    </div>
  );
}

function Hero({ items, loading, onAdd, onAdded }: {
  items: DiscoveryItem[];
  loading: boolean;
  onAdd: (item: DiscoveryItem) => void;
  onAdded: (item: DiscoveryItem, id: number) => void;
}) {
  const addTitle = useAddTitle();
  const [index, setIndex] = useState(0);
  const [busy, setBusy] = useState(false);
  const item = items[Math.min(index, items.length - 1)];

  if (!item) {
    return (
      <div className="-mx-4 -mt-6 h-[58vh] min-h-[420px] sm:-mx-6 lg:-mx-10 lg:-mt-8">
        {loading ? <Skeleton className="h-full rounded-none" /> : null}
      </div>
    );
  }

  const quickAdd = async () => {
    setBusy(true);
    try {
      onAdded(item, (await addTitle(item)).id);
    } catch {
      onAdd(item);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section aria-label="Featured" className="relative -mx-4 -mt-6 overflow-hidden sm:-mx-6 lg:-mx-10 lg:-mt-8">
      <div className="relative h-[62vh] min-h-[440px] max-h-[640px]">
        {items.map((i, n) => (
          // eslint-disable-next-line @next/next/no-img-element
          <img key={i.backdrop} src={i.backdrop ?? ""} alt="" aria-hidden={n !== index}
            className={`absolute inset-0 h-full w-full object-cover object-[50%_25%] transition-opacity duration-700 ${n === index ? "opacity-100" : "opacity-0"}`} />
        ))}
        <div className="absolute inset-0 bg-gradient-to-r from-background via-background/70 to-background/0 lg:via-background/40" />
        <div className="absolute inset-0 bg-gradient-to-t from-background via-background/10 to-background/40" />

        <div key={item.title} className="absolute inset-x-0 bottom-0 animate-rise px-4 pb-10 sm:px-6 lg:px-10 lg:pb-14">
          <div className="max-w-2xl">
            <div className="flex flex-wrap items-center gap-3 text-sm text-white/75">
              <span className="rounded-md bg-white/10 px-2 py-0.5 text-xs font-medium text-white ring-1 ring-white/15 backdrop-blur">{kindLabel[item.kind]}</span>
              {item.year && <span>{item.year}</span>}
              <Rating value={item.rating} className="text-sm" />
              {item.genres.slice(0, 3).length > 0 && <span className="hidden sm:inline">{item.genres.slice(0, 3).join(", ")}</span>}
            </div>
            <h1 className="mt-3 font-display text-4xl leading-[0.95] font-bold tracking-tight text-balance text-white drop-shadow-lg sm:text-6xl lg:text-7xl">
              {item.title}
            </h1>
            {item.overview && <p className="mt-4 line-clamp-3 max-w-xl text-sm leading-relaxed text-white/75 sm:text-base">{item.overview}</p>}
            <div className="mt-6 flex flex-wrap gap-2.5">
              {item.inLibrary && item.libraryId !== null ? (
                <Link href={titleHref(kindOfDiscovery(item), item.libraryId)}
                  className="inline-flex h-11 items-center gap-2 rounded-lg bg-white px-5 text-sm font-semibold text-black transition hover:bg-white/90">
                  <Icon name="check" size={16} strokeWidth={2.6} />In your library
                </Link>
              ) : (
                <Button variant="primary" size="lg" onClick={quickAdd} disabled={busy}>
                  {busy ? <Spinner /> : <Icon name="plus" size={16} strokeWidth={2.4} />}Add to library
                </Button>
              )}
              {!item.inLibrary && <Button variant="glass" size="lg" onClick={() => onAdd(item)}>Add with options…</Button>}
            </div>
          </div>
        </div>

        {items.length > 1 && (
          <div className="absolute right-4 bottom-10 hidden gap-2 sm:right-6 md:flex lg:right-10 lg:bottom-14" role="tablist" aria-label="Featured titles">
            {items.map((i, n) => (
              <button key={i.backdrop} role="tab" aria-selected={n === index} aria-label={i.title} onClick={() => setIndex(n)}
                className="relative h-14 w-24 cursor-pointer overflow-hidden rounded-lg opacity-50 ring-1 ring-white/20 transition hover:opacity-90 aria-selected:opacity-100 aria-selected:ring-2 aria-selected:ring-white">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img src={i.backdrop ?? ""} alt="" className="h-full w-full object-cover" />
              </button>
            ))}
          </div>
        )}
        {items.length > 1 && (
          <div className="absolute inset-x-0 bottom-4 flex justify-center gap-1.5 md:hidden">
            {items.map((i, n) => (
              <button key={i.backdrop} aria-label={`Show ${i.title}`} onClick={() => setIndex(n)}
                className={`h-1.5 cursor-pointer rounded-full transition-all ${n === index ? "w-5 bg-white" : "w-1.5 bg-white/40"}`} />
            ))}
          </div>
        )}
      </div>
    </section>
  );
}

function Row({ title, href, list, onAdd, onAdded }: {
  title: string;
  href: string;
  list: { items: DiscoveryItem[]; error: string | null } | undefined;
  onAdd: (item: DiscoveryItem) => void;
  onAdded: (item: DiscoveryItem, id: number) => void;
}) {
  const rail = useRef<HTMLDivElement>(null);
  const scroll = (direction: number) => rail.current?.scrollBy({ left: direction * rail.current.clientWidth * 0.85, behavior: "smooth" });
  const card = "w-[38vw] max-w-44 shrink-0 snap-start min-[480px]:w-36 sm:w-40 xl:w-44";

  return (
    <section aria-label={title}>
      <div className="mb-4 flex items-end justify-between gap-4">
        <h2 className="font-display text-xl font-semibold tracking-tight sm:text-2xl">{title}</h2>
        <div className="flex items-center gap-1">
          <Link href={href} className="mr-2 text-sm text-muted hover:text-foreground">Your library</Link>
          <button onClick={() => scroll(-1)} aria-label={`Scroll ${title} back`} className="hidden cursor-pointer rounded-full p-1.5 text-muted ring-1 ring-line hover:bg-raised hover:text-foreground sm:block">
            <Icon name="chevronLeft" size={16} />
          </button>
          <button onClick={() => scroll(1)} aria-label={`Scroll ${title} forward`} className="hidden cursor-pointer rounded-full p-1.5 text-muted ring-1 ring-line hover:bg-raised hover:text-foreground sm:block">
            <Icon name="chevronRight" size={16} />
          </button>
        </div>
      </div>
      {list?.error && list.items.length === 0 ? (
        <p className="rounded-xl border border-dashed border-line px-5 py-8 text-sm text-muted">This list is unavailable right now ({list.error}). Search still works.</p>
      ) : (
        <div ref={rail} className="rail -mx-4 flex gap-4 overflow-x-auto scroll-px-4 px-4 pt-1 pb-2 sm:-mx-6 sm:scroll-px-6 sm:px-6 lg:-mx-10 lg:scroll-px-10 lg:px-10">
          {list
            ? list.items.map((item) => (
              <DiscoveryCard key={`${item.kind}${item.tmdbId}${item.tvdbId}`} item={item} onAddOptions={onAdd} onAdded={onAdded} className={card} />
            ))
            : Array.from({ length: 8 }, (_, i) => <PosterSkeleton key={i} className={card} />)}
        </div>
      )}
    </section>
  );
}
