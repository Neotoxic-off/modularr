"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import type { KeyboardEvent } from "react";
import { createPortal } from "react-dom";
import { api, errorMessage, kindOfDiscovery, titleHref } from "@/lib/api";
import type { DiscoveryItem } from "@/lib/api";
import { useAddDialog } from "@/components/AddDialog";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { pages } from "@/components/nav";
import type { NavItem } from "@/components/nav";
import { kindLabel } from "@/components/Poster";
import { Rating, Spinner, StatusBadge, useDialog } from "@/components/ui";

const DEBOUNCE_MS = 250;

type Option = { page: NavItem } | { title: DiscoveryItem };

// Search-as-you-type over movies, series and anime, plus the app's pages; Enter opens a title, adds one, or goes to a page.
export function SearchPalette({ onClose }: { onClose: () => void }) {
  const router = useRouter();
  const openAdd = useAddDialog();
  const { user } = useSession();
  const panel = useRef<HTMLDivElement>(null);
  const [term, setTerm] = useState("");
  const [results, setResults] = useState<DiscoveryItem[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [active, setActive] = useState(0);
  useDialog(panel, onClose);

  const query = term.trim();

  useEffect(() => {
    if (query.length < 2) return;
    const abort = new AbortController();
    const timer = setTimeout(() => {
      setLoading(true);
      api.lookup(query, abort.signal)
        .then((r) => { setResults(r); setError(null); setActive(0); })
        .catch((e: unknown) => { if (!abort.signal.aborted) setError(errorMessage(e)); })
        .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    }, DEBOUNCE_MS);
    return () => { clearTimeout(timer); abort.abort(); };
  }, [query]);

  const needle = query.toLowerCase();
  const matchingPages = pages.filter((p) => (!p.admin || user.role === "Admin") && (!needle || p.label.toLowerCase().startsWith(needle)));
  const titles = query.length < 2 ? [] : results ?? [];
  const options: Option[] = [...matchingPages.map((page) => ({ page })), ...titles.map((title) => ({ title }))];

  function choose(option: Option) {
    if ("page" in option) {
      onClose();
      router.push(option.page.href);
    } else if (option.title.inLibrary && option.title.libraryId !== null) {
      onClose();
      router.push(titleHref(kindOfDiscovery(option.title), option.title.libraryId));
    } else {
      onClose();
      openAdd(option.title);
    }
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
    <div className="fixed inset-0 z-50 flex justify-center bg-black/60 px-3 pt-[calc(env(safe-area-inset-top)+12px)] sm:pt-[12vh]" onMouseDown={onClose}>
      <div ref={panel} role="dialog" aria-modal="true" aria-label="Search" tabIndex={-1} onMouseDown={(e) => e.stopPropagation()} onKeyDown={key}
        className="glass-pop flex h-fit max-h-[80dvh] w-full max-w-2xl animate-rise flex-col overflow-hidden rounded-tile outline-none">
        <div className="flex items-center gap-3 border-b border-hair-2 px-5">
          {loading ? <Spinner className="text-ink-2" /> : <Icon name="search" className="text-ink-2" />}
          <input value={term} onChange={(e) => setTerm(e.target.value)} role="combobox" aria-expanded={options.length > 0}
            aria-controls="search-results" aria-activedescendant={options.length ? `option-${active}` : undefined}
            placeholder="Search or add a title" aria-label="Search titles and pages"
            className="h-16 min-w-0 flex-1 bg-transparent text-[17px] outline-none placeholder:text-ink-3 focus-visible:outline-none" />
          <button onClick={onClose} className="h-9 cursor-pointer rounded-pill px-3 text-[13px] font-medium text-ink-2 hover:bg-fill-2 hover:text-ink">
            <span className="max-sm:hidden">Esc</span><span className="sm:hidden">Cancel</span>
          </button>
        </div>

        <div className="min-h-0 overflow-y-auto">
          <ul id="search-results" role="listbox" aria-label="Results" className="p-2">
            {matchingPages.length > 0 && <li role="presentation" className="caption px-2 pt-2 pb-1.5">Go to</li>}
            {matchingPages.map((page, i) => (
              <li key={page.href} id={`option-${i}`} role="option" aria-selected={i === active}
                onMouseMove={() => setActive(i)} onClick={() => choose({ page })} className={row}>
                <span className="grid size-9 place-items-center rounded-full bg-fill text-ink-2"><Icon name={page.icon} size={17} /></span>
                <span className="flex-1 text-sm font-medium">{page.label}</span>
                {page.shortcut && <kbd className="font-sans text-[12px] text-ink-3">g {page.shortcut}</kbd>}
              </li>
            ))}
            {titles.length > 0 && <li role="presentation" className="caption px-2 pt-3 pb-1.5">Titles</li>}
            {titles.map((item, n) => {
              const i = matchingPages.length + n;
              return (
                <li key={`${item.kind}${item.tmdbId}${item.tvdbId}`} id={`option-${i}`} role="option" aria-selected={i === active}
                  onMouseMove={() => setActive(i)} onClick={() => choose({ title: item })} className={row}>
                  <span className="h-16 w-11 shrink-0 overflow-hidden rounded-chip bg-fill">
                    {item.poster && (
                      // eslint-disable-next-line @next/next/no-img-element
                      <img src={item.poster} alt="" loading="lazy" className="h-full w-full object-cover" />
                    )}
                  </span>
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-[15px] font-semibold">{item.title}</p>
                    <p className="mt-0.5 flex items-center gap-2.5 text-[13px] text-ink-3">
                      <span>{kindLabel[item.kind]}</span>
                      {item.year && <span>{item.year}</span>}
                      <Rating value={item.rating} className="text-[12px]" />
                    </p>
                  </div>
                  {item.inLibrary
                    ? <StatusBadge status={{ state: "library" }} />
                    : <span className="flex h-9 items-center gap-1 rounded-pill bg-white px-3.5 text-[13px] font-semibold text-canvas"><Icon name="plus" size={14} strokeWidth={2.4} />Add</span>}
                </li>
              );
            })}
          </ul>
          {query.length < 2 ? (
            <p className="px-5 pt-1 pb-5 text-[13px] text-ink-2">
              Type a title, with a year if you like: <span className="text-ink">dune 2021</span>. Ids work too: <span className="text-ink">tmdb:438631</span>, <span className="text-ink">tvdb:81189</span>.
            </p>
          ) : error ? (
            <p className="px-5 pb-6 text-[13px] text-bad">Search is unavailable right now: {error}</p>
          ) : results === null ? (
            <p className="px-5 pb-6 text-[13px] text-ink-2">Searching…</p>
          ) : results.length === 0 && (
            <p className="px-5 pb-6 text-[13px] text-ink-2">Nothing matches “{query}”. Try another spelling or add the year.</p>
          )}
        </div>
        <div className="hidden items-center gap-4 border-t border-hair-2 px-5 py-2.5 text-[12px] text-ink-3 sm:flex">
          <span><kbd className="font-sans">↑ ↓</kbd> to move</span>
          <span><kbd className="font-sans">Enter</kbd> to open or add</span>
        </div>
      </div>
    </div>,
    document.body,
  );
}
