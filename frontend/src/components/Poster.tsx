"use client";

import Link from "next/link";
import { useState } from "react";
import type { ReactNode } from "react";
import { errorMessage, kindOfDiscovery, titleHref } from "@/lib/api";
import type { DiscoveryItem } from "@/lib/api";
import { useAddDialog, useAddTitle } from "@/components/AddDialog";
import type { OnAdded } from "@/components/AddDialog";
import { Icon } from "@/components/icons";
import { Rating, Skeleton, Spinner, StatusBadge } from "@/components/ui";
import type { Status } from "@/components/ui";

export const kindLabel = { Movie: "Movie", Series: "Series", Anime: "Anime" } as const;

export function PosterArt({ src, title, className = "" }: { src: string | null; title: string; className?: string }) {
  const [failed, setFailed] = useState(false);
  return (
    <div className={`relative aspect-[2/3] overflow-hidden rounded-card bg-fill ring-1 ring-white/8 ${className}`}>
      {src && !failed ? (
        // Remote TMDB/TVDB artwork: next/image optimisation is unavailable in a static export.
        // eslint-disable-next-line @next/next/no-img-element
        <img src={src} alt="" loading="lazy" onError={() => setFailed(true)}
          className="h-full w-full object-cover transition duration-500 group-hover:scale-[1.03]" />
      ) : (
        <span className="absolute inset-0 flex flex-col items-center justify-center gap-2 p-3 text-center text-[13px] font-medium text-ink-3">
          <Icon name="film" size={22} />{title}
        </span>
      )}
    </div>
  );
}

type CardProps =
  | { item: DiscoveryItem; onAdded: OnAdded }
  | { title: string; year: number | null; rating: number | null; poster: string | null; href: string; status?: Status; fraction?: string };

// One card for every poster: a library title links to its page, a discovered one offers a quick add and the add sheet.
export function PosterCard({ className = "", ...props }: CardProps & { className?: string }) {
  const addTitle = useAddTitle();
  const openAdd = useAddDialog();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const item = "item" in props ? props.item : null;
  const title = item?.title ?? ("title" in props ? props.title : "");
  const href = item ? (item.inLibrary && item.libraryId !== null ? titleHref(kindOfDiscovery(item), item.libraryId) : null) : "href" in props ? props.href : null;
  const status: Status | undefined = item ? (item.inLibrary ? { state: "library" } : undefined) : "status" in props ? props.status : undefined;
  const year = item ? item.year : "year" in props ? props.year : null;
  const rating = item ? item.rating : "rating" in props ? props.rating : null;
  const fraction = "fraction" in props ? props.fraction : undefined;

  async function quickAdd() {
    if (!("item" in props)) return;
    setBusy(true);
    setError(null);
    try {
      await addTitle(props.item, {}, props.onAdded);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  const art = (
    <>
      <PosterArt src={item ? item.poster : "poster" in props ? props.poster : null} title={title} className={item?.inLibrary ? "ring-2 ring-ok/70" : ""} />
      <span aria-hidden="true" className="pointer-events-none absolute inset-0 rounded-card bg-gradient-to-t from-black/80 via-black/0 to-black/25" />
    </>
  );

  return (
    <article className={`group relative min-w-0 ${className}`}>
      <div className="relative">
        {href ? (
          <Link href={href} aria-label={`${title}${item ? ", in library" : ""}`} className="block rounded-card">{art}</Link>
        ) : (
          <button onClick={() => item && "onAdded" in props && openAdd(item, props.onAdded)} aria-label={`Add ${title}…`} className="block w-full cursor-pointer rounded-card text-left">{art}</button>
        )}
        {status && <StatusBadge status={status} className="pointer-events-none absolute top-2 left-2" />}
        {status?.state === "downloading" && (
          <span aria-hidden="true" className="absolute inset-x-0 bottom-0 h-[3px] overflow-hidden rounded-b-card bg-black/50">
            <span className="block h-full bg-live transition-[width] duration-700" style={{ width: `${status.progress * 100}%` }} />
          </span>
        )}
        {item && !item.inLibrary && (
          <button onClick={quickAdd} disabled={busy} aria-label={`Add ${title} with your defaults`} title="Add with your defaults"
            className="absolute right-2 bottom-2 grid size-10 cursor-pointer pointer-coarse:size-11 place-items-center rounded-full bg-white text-canvas shadow-lg shadow-black/50 transition hover:scale-105 sm:pointer-fine:opacity-0 sm:pointer-fine:group-focus-within:opacity-100 sm:pointer-fine:group-hover:opacity-100">
            {busy ? <Spinner /> : <Icon name="plus" size={20} strokeWidth={2.4} />}
          </button>
        )}
      </div>
      <h3 className="mt-2.5 truncate px-0.5 text-sm font-semibold" title={title}>{title}</h3>
      <p className="flex items-center gap-2 px-0.5 text-[13px] text-ink-3">
        {!!year && <span>{year}</span>}
        <Rating value={rating} className="text-[12px]" />
        {fraction && <span className="ml-auto tabular-nums">{fraction}</span>}
      </p>
      {error && <p role="alert" className="mt-1 line-clamp-2 text-[12px] text-bad">{error}</p>}
    </article>
  );
}

export function PosterGrid({ children }: { children: ReactNode }) {
  return <div className="grid grid-cols-[repeat(auto-fill,minmax(112px,1fr))] gap-x-4 gap-y-5 min-[480px]:grid-cols-[repeat(auto-fill,minmax(132px,1fr))]">{children}</div>;
}

export function PosterSkeleton({ className = "" }: { className?: string }) {
  return (
    <div className={className}>
      <Skeleton className="aspect-[2/3] rounded-card" />
      <Skeleton className="mt-3 h-3.5 w-3/4" />
      <Skeleton className="mt-1.5 h-3 w-1/3" />
    </div>
  );
}
