"use client";

import { useRouter } from "next/navigation";
import { createContext, useCallback, useContext, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import { api, errorMessage, kindOfDiscovery, titleHref } from "@/lib/api";
import type { AddOptions, DiscoveryItem, LibraryItem } from "@/lib/api";
import { defaultsFor, languageLabel, useOptions } from "@/lib/options";
import { titleKey } from "@/components/Activity";
import { kindLabel, PosterArt } from "@/components/Poster";
import { useToast } from "@/components/Toasts";
import { Alert, Button, Field, Modal, Rating, Select, Spinner, Switch } from "@/components/ui";

type OnAdded = (item: DiscoveryItem, libraryId: number) => void;

const AddContext = createContext<(item: DiscoveryItem, onAdded?: OnAdded) => void>(() => undefined);

// Any page or the search palette opens the same "Add…" dialog through this.
export const useAddDialog = () => useContext(AddContext);

export const markAdded = (items: readonly DiscoveryItem[], added: DiscoveryItem, libraryId: number) =>
  items.map((i) => (i.kind === added.kind && i.tmdbId === added.tmdbId && i.tvdbId === added.tvdbId ? { ...i, inLibrary: true, libraryId } : i));

// Adds a title and says so at once; the search results follow as live notifications.
export function useAddTitle() {
  const toast = useToast();
  return useCallback(async (item: DiscoveryItem, options: AddOptions = {}): Promise<LibraryItem> => {
    const added = await api.add(item, options);
    const kind = kindOfDiscovery(item);
    const searching = options.searchNow !== false && options.monitor !== "None";
    toast({
      key: titleKey({ movieId: kind === "movie" ? added.id : null, seriesId: kind === "movie" ? null : added.id }) ?? item.title,
      icon: searching ? "search" : "plus",
      tone: "accent",
      title: item.title,
      message: searching ? "Added. Searching for releases…" : "Added to your library",
      href: titleHref(kind, added.id),
    });
    return added;
  }, [toast]);
}

export function AddDialogProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState<{ item: DiscoveryItem; onAdded?: OnAdded } | null>(null);
  const show = useCallback((item: DiscoveryItem, onAdded?: OnAdded) => setOpen({ item, onAdded }), []);
  const close = useCallback(() => setOpen(null), []);
  return (
    <AddContext value={show}>
      {children}
      {open && <AddDialog key={`${open.item.kind}${open.item.tmdbId}${open.item.tvdbId}`} item={open.item} onAdded={open.onAdded} onClose={close} />}
    </AddContext>
  );
}

function AddDialog({ item, onAdded, onClose }: { item: DiscoveryItem; onAdded?: OnAdded; onClose: () => void }) {
  const router = useRouter();
  const addTitle = useAddTitle();
  const options = useOptions();
  const kind = kindOfDiscovery(item);
  const [profile, setProfile] = useState("");
  const [language, setLanguage] = useState("");
  const [monitor, setMonitor] = useState<NonNullable<AddOptions["monitor"]>>("All");
  const [searchNow, setSearchNow] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const defaults = options && defaultsFor(options.media, kind);
  const defaultProfile = options?.profiles.find((p) => p.id === defaults?.profileId)?.name;

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const added = await addTitle(item, {
        qualityProfileId: profile ? Number(profile) : null,
        language: language || null,
        searchNow,
        ...(kind !== "movie" && { monitor }),
      });
      onAdded?.(item, added.id);
      onClose();
      router.push(titleHref(kind, added.id));
    } catch (e) {
      setError(errorMessage(e));
      setBusy(false);
    }
  }

  return (
    <Modal onClose={onClose} banner={item.backdrop} title={`Add ${item.title}`}
      description={<span className="flex items-center gap-3">{kindLabel[item.kind]}{item.year && <span>{item.year}</span>}<Rating value={item.rating} /></span>}
      footer={<>
        <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
        <Button variant="primary" form="add-form" disabled={busy || !options}>{busy ? <Spinner /> : "Add to library"}</Button>
      </>}>
      <form id="add-form" onSubmit={submit} className="flex gap-5 px-5 pt-2 pb-5">
        <PosterArt src={item.poster} title={item.title} className="hidden w-28 shrink-0 self-start shadow-xl shadow-black/50 sm:block" />
        <div className="min-w-0 flex-1 space-y-4">
          {item.overview && <p className="line-clamp-3 text-sm leading-relaxed text-muted">{item.overview}</p>}
          {!options ? <div className="flex justify-center py-8 text-muted"><Spinner /></div> : (
            <>
              <div className="grid gap-4 sm:grid-cols-2">
                <Field label="Quality profile">
                  <Select value={profile} onChange={(e) => setProfile(e.target.value)}>
                    <option value="">Default ({defaultProfile ?? "none"})</option>
                    {options.profiles.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                  </Select>
                </Field>
                <Field label="Language">
                  <Select value={language} onChange={(e) => setLanguage(e.target.value)}>
                    <option value="">Default</option>
                    {options.languages.map((l) => <option key={l.id} value={l.name}>{languageLabel(l.name)}</option>)}
                  </Select>
                </Field>
                {kind !== "movie" && (
                  <Field label="Monitor">
                    <Select value={monitor} onChange={(e) => setMonitor(e.target.value as typeof monitor)}>
                      <option value="All">All episodes</option>
                      <option value="Future">Future episodes only</option>
                      <option value="None">Nothing for now</option>
                    </Select>
                  </Field>
                )}
              </div>
              <Switch checked={searchNow && monitor !== "None"} onChange={setSearchNow} disabled={monitor === "None"}
                label="Start searching right away" description="Looks for releases on every indexer as soon as it is added." />
            </>
          )}
          {error && <Alert>{error}</Alert>}
        </div>
      </form>
    </Modal>
  );
}
