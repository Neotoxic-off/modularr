"use client";

import Link from "next/link";
import { useState } from "react";
import type { ReactNode } from "react";
import { api, errorMessage, kindOfDiscovery, titleHref } from "@/lib/api";
import type { DiscoveryItem } from "@/lib/api";
import { Icon } from "@/components/icons";
import { Rating, Skeleton, Spinner } from "@/components/ui";

export const kindLabel = { Movie: "Movie", Series: "Series", Anime: "Anime" } as const;

// Small label laid over artwork, readable on any poster.
export const overlayTag = "rounded-md bg-black/70 px-1.5 py-0.5 text-[10px] font-semibold backdrop-blur";

export function PosterArt({ src, title, className = "" }: { src: string | null; title: string; className?: string }) {
  const [failed, setFailed] = useState(false);
  return (
    <div className={`relative aspect-[2/3] overflow-hidden rounded-xl bg-[radial-gradient(120%_80%_at_30%_10%,var(--color-raised),var(--color-surface))] ring-1 ring-white/[0.06] ${className}`}>
      {src && !failed ? (
        // Remote TMDB/TVDB artwork: next/image optimisation is unavailable in a static export.
        // eslint-disable-next-line @next/next/no-img-element
        <img src={src} alt="" loading="lazy" onError={() => setFailed(true)}
          className="h-full w-full object-cover transition duration-500 group-hover:scale-[1.04]" />
      ) : (
        <span className="absolute inset-0 flex flex-col items-center justify-center gap-2 p-4 text-center font-display text-sm font-medium text-muted">
          <Icon name="film" size={22} className="opacity-50" />{title}
        </span>
      )}
    </div>
  );
}

function CardText({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="min-w-0 px-0.5 pt-2.5">
      <h3 className="truncate text-sm font-medium" title={title}>{title}</h3>
      <div className="flex items-center gap-2 text-xs text-muted">{children}</div>
    </div>
  );
}

// A title from discovery or search: opens the library page once added, the add dialog otherwise.
export function DiscoveryCard({ item, onAddOptions, onAdded, className = "" }: {
  item: DiscoveryItem;
  onAddOptions: (item: DiscoveryItem) => void;
  onAdded: (item: DiscoveryItem, libraryId: number) => void;
  className?: string;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function quickAdd() {
    setBusy(true);
    setError(null);
    try {
      onAdded(item, (await api.add(item)).id);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  const art = (
    <>
      <PosterArt src={item.poster} title={item.title} />
      <span className="pointer-events-none absolute inset-0 rounded-xl bg-gradient-to-t from-black/80 via-black/0 to-black/30 opacity-60 transition group-hover:opacity-100" />
      <span className={`absolute top-2 left-2 text-white/90 ${overlayTag}`}>{kindLabel[item.kind]}</span>
      {item.inLibrary && (
        <span className="absolute top-2 right-2 flex size-6 items-center justify-center rounded-full bg-emerald-400 text-black shadow" title="In library">
          <Icon name="check" size={14} strokeWidth={3} />
        </span>
      )}
    </>
  );

  return (
    <article className={`group relative ${className}`}>
      <div className="relative transition duration-300 group-hover:-translate-y-1">
        {item.inLibrary && item.libraryId !== null ? (
          <Link href={titleHref(kindOfDiscovery(item), item.libraryId)} aria-label={`${item.title}, in library`} className="block rounded-xl">{art}</Link>
        ) : (
          <button onClick={() => onAddOptions(item)} aria-label={`Add ${item.title}…`} className="block w-full cursor-pointer rounded-xl text-left">{art}</button>
        )}
        {!item.inLibrary && (
          <button onClick={quickAdd} disabled={busy} aria-label={`Add ${item.title} with default settings`} title="Add with defaults"
            className="absolute right-2 bottom-2 flex size-9 cursor-pointer items-center justify-center rounded-full bg-accent-strong text-white shadow-lg shadow-black/50 transition hover:scale-105 hover:bg-accent sm:translate-y-1 sm:opacity-0 sm:group-focus-within:translate-y-0 sm:group-focus-within:opacity-100 sm:group-hover:translate-y-0 sm:group-hover:opacity-100">
            {busy ? <Spinner /> : <Icon name="plus" size={18} strokeWidth={2.4} />}
          </button>
        )}
      </div>
      <CardText title={item.title}>
        {item.year && <span>{item.year}</span>}
        <Rating value={item.rating} />
      </CardText>
      {error && <p role="alert" className="mt-1 line-clamp-2 text-xs text-red-300">{error}</p>}
    </article>
  );
}

export function LibraryCard({ href, title, year, poster, rating, status, progress }: {
  href: string;
  title: string;
  year: number;
  poster: string | null;
  rating: number | null;
  status?: ReactNode;
  progress?: number;
}) {
  return (
    <article className="group">
      <Link href={href} className="relative block rounded-xl transition duration-300 group-hover:-translate-y-1">
        <PosterArt src={poster} title={title} />
        {status && <span className="absolute top-2 left-2">{status}</span>}
        {progress !== undefined && (
          <span className="absolute inset-x-2 bottom-2 h-1 overflow-hidden rounded-full bg-black/60 backdrop-blur">
            <span className={`block h-full rounded-full ${progress >= 1 ? "bg-emerald-400" : "bg-accent"}`} style={{ width: `${Math.round(progress * 100)}%` }} />
          </span>
        )}
      </Link>
      <CardText title={title}>
        {year > 0 && <span>{year}</span>}
        <Rating value={rating} />
      </CardText>
    </article>
  );
}

export function PosterGrid({ children }: { children: ReactNode }) {
  return <div className="grid grid-cols-2 gap-x-4 gap-y-7 min-[480px]:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 2xl:grid-cols-8">{children}</div>;
}

export function PosterSkeleton({ className = "" }: { className?: string }) {
  return (
    <div className={className}>
      <Skeleton className="aspect-[2/3] rounded-xl" />
      <Skeleton className="mt-3 h-3.5 w-3/4" />
      <Skeleton className="mt-1.5 h-3 w-1/3" />
    </div>
  );
}
