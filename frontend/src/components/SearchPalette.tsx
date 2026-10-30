"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import type { KeyboardEvent } from "react";
import { createPortal } from "react-dom";
import { api, errorMessage, kindOf, titleHref } from "@/lib/api";
import type { Kind, LibraryItem } from "@/lib/api";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { pages } from "@/components/nav";
import type { NavItem } from "@/components/nav";
import { Rating, Spinner, useDialog } from "@/components/ui";

const MAX_TITLES = 30;
const kindLabel: Record<Kind, string> = { movie: "Movie", series: "Series", anime: "Anime" };

type Option = { page: NavItem } | { title: LibraryItem } | { explore: string };

// Instant search over the library's movies, series and anime, plus the app's pages; adding titles lives in Explore.
export function SearchPalette({ onClose }: { onClose: () => void }) {
  const router = useRouter();
  const { user } = useSession();
  const panel = useRef<HTMLDivElement>(null);
  const [term, setTerm] = useState("");
  const [library, setLibrary] = useState<LibraryItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [active, setActive] = useState(0);
  useDialog(panel, onClose);

  useEffect(() => {
    Promise.all([api.movies(), api.series()])
      .then(([movies, series]) => setLibrary([...movies, ...series]))
      .catch((e: unknown) => setError(errorMessage(e)));
  }, []);

  const needle = term.trim().toLowerCase();
  const matchingPages = pages.filter((p) => (!p.admin || user.role === "Admin") && (!needle || p.label.toLowerCase().startsWith(needle)));
  // Titles starting with the term first, then the ones containing it.
  const titles = !needle ? [] : (library ?? [])
    .filter((i) => i.title.toLowerCase().includes(needle))
    .sort((a, b) => Number(b.title.toLowerCase().startsWith(needle)) - Number(a.title.toLowerCase().startsWith(needle)) || a.title.localeCompare(b.title))
    .slice(0, MAX_TITLES);
  // Nothing in the library: offer the term to Explore, where titles are added.
  const explore = needle && library && titles.length === 0 && user.role === "Admin" ? term.trim() : null;
  const options: Option[] = [...matchingPages.map((page) => ({ page })), ...titles.map((title) => ({ title })), ...(explore ? [{ explore }] : [])];

  function choose(option: Option) {
    onClose();
    router.push("page" in option ? option.page.href
      : "title" in option ? titleHref(kindOf(option.title), option.title.id)
      : `/explore/?q=${encodeURIComponent(option.explore)}`);
  }

  function key(e: KeyboardEvent) {
    if (!options.length) return;
    if (e.key === "ArrowDown") { e.preventDefault(); setActive((a) => (a + 1) % options.length); }
    if (e.key === "ArrowUp") { e.preventDefault(); setActive((a) => (a - 1 + options.length) % options.length); }
    const option = options[active];
    if (e.key === "Enter" && option) { e.preventDefault(); choose(option); }
  }

  const row = "flex min-h-12 cursor-pointer items-center gap-3 rounded-ctl p-2 aria-selected:bg-fill-2";

  return createPortal(
    <div className="fixed inset-0 z-50 flex justify-center bg-overlay px-3 pt-[calc(env(safe-area-inset-top)+12px)] sm:pt-[12vh]" onMouseDown={onClose}>
      <div ref={panel} role="dialog" aria-modal="true" aria-label="Search" tabIndex={-1} onMouseDown={(e) => e.stopPropagation()} onKeyDown={key}
        className="glass-pop flex h-fit max-h-[80dvh] w-full max-w-2xl animate-rise flex-col overflow-hidden rounded-tile outline-none">
        <div className="flex items-center gap-3 border-b border-hair-2 px-5">
          {needle && !library && !error ? <Spinner className="text-ink-2" /> : <Icon name="search" className="text-ink-2" />}
          <input value={term} onChange={(e) => { setTerm(e.target.value); setActive(0); }} role="combobox" aria-expanded={options.length > 0}
            aria-controls="search-results" aria-activedescendant={options.length ? `option-${active}` : undefined}
            placeholder="Search your library" aria-label="Search your library and pages"
            className="h-16 min-w-0 flex-1 bg-transparent text-title outline-none placeholder:text-ink-3 focus-visible:outline-none" />
          <button onClick={onClose} className="h-9 cursor-pointer rounded-pill px-3 text-meta font-medium text-ink-2 hover:bg-fill-2 hover:text-ink">
            <span className="max-sm:hidden">Esc</span><span className="sm:hidden">Cancel</span>
          </button>
        </div>

        <div className="min-h-0 overflow-y-auto">
          <ul id="search-results" role="listbox" aria-label="Results" className="p-2">
            {matchingPages.length > 0 && <li role="presentation" className="caption px-2 pt-2 pb-1.5">Go to</li>}
            {matchingPages.map((page, i) => (
              <li key={page.href} id={`option-${i}`} role="option" aria-selected={i === active}
                onMouseMove={() => setActive(i)} onClick={() => choose({ page })} className={row}>
                <span className="grid size-9 place-items-center rounded-full bg-fill text-ink-2"><Icon name={page.icon} size={16} /></span>
                <span className="flex-1 text-body font-medium">{page.label}</span>
                {page.shortcut && <kbd className="font-sans text-meta text-ink-3">g {page.shortcut}</kbd>}
              </li>
            ))}
            {titles.length > 0 && <li role="presentation" className="caption px-2 pt-3 pb-1.5">In your library</li>}
            {titles.map((item, n) => {
              const i = matchingPages.length + n;
              const kind = kindOf(item);
              return (
                <li key={`${kind}${item.id}`} id={`option-${i}`} role="option" aria-selected={i === active}
                  onMouseMove={() => setActive(i)} onClick={() => choose({ title: item })} className={row}>
                  <span className="h-16 w-11 shrink-0 overflow-hidden rounded-chip bg-fill">
                    {item.poster && (
                      // eslint-disable-next-line @next/next/no-img-element
                      <img src={item.poster} alt="" loading="lazy" className="h-full w-full object-cover" />
                    )}
                  </span>
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-body font-semibold">{item.title}</p>
                    <p className="mt-0.5 flex items-center gap-2.5 text-meta text-ink-3">
                      <span>{kindLabel[kind]}</span>
                      {item.year > 0 && <span>{item.year}</span>}
                      <Rating value={item.rating} className="text-meta" />
                    </p>
                  </div>
                </li>
              );
            })}
            {explore && (
              <li id={`option-${options.length - 1}`} role="option" aria-selected={active === options.length - 1}
                onMouseMove={() => setActive(options.length - 1)} onClick={() => choose({ explore })} className={row}>
                <span className="grid size-9 place-items-center rounded-full bg-fill text-ink-2"><Icon name="compass" size={16} /></span>
                <span className="min-w-0 flex-1 truncate text-body font-medium">Search Explore for “{explore}”</span>
                <Icon name="chevronRight" size={16} className="text-ink-3" />
              </li>
            )}
          </ul>
          {error ? (
            <p className="px-5 pb-6 text-meta text-bad">Search is unavailable right now: {error}</p>
          ) : !needle ? (
            <p className="px-5 pt-1 pb-5 text-meta text-ink-2">Type a movie, series or anime from your library.</p>
          ) : null}
        </div>
        <div className="hidden items-center gap-4 border-t border-hair-2 px-5 py-2.5 text-meta text-ink-3 sm:flex">
          <span><kbd className="font-sans">↑ ↓</kbd> to move</span>
          <span><kbd className="font-sans">Enter</kbd> to open</span>
        </div>
      </div>
    </div>,
    document.body,
  );
}
