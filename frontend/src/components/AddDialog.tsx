"use client";

import { createContext, useCallback, useContext, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import { api, errorMessage, kindOfDiscovery, titleHref } from "@/lib/api";
import type { AddOptions, DiscoveryItem, LibraryItem } from "@/lib/api";
import { defaultsFor, realLanguages, useOptions } from "@/lib/options";
import { titleKey } from "@/components/Activity";
import { Icon } from "@/components/icons";
import { kindLabel } from "@/components/Poster";
import { useToast } from "@/components/Toasts";
import { Alert, Button, Field, Modal, PickList, Rating, Segmented, Select, Spinner, Switch } from "@/components/ui";

// libraryId is null when the add was undone.
export type OnAdded = (item: DiscoveryItem, libraryId: number | null) => void;

const AddContext = createContext<(item: DiscoveryItem, onAdded?: OnAdded) => void>(() => undefined);

// Any page or the search palette opens the same add sheet through this.
export const useAddDialog = () => useContext(AddContext);

export const markAdded = (items: readonly DiscoveryItem[], added: DiscoveryItem, libraryId: number | null) =>
  items.map((i) => (i.kind === added.kind && i.tmdbId === added.tmdbId && i.tvdbId === added.tvdbId ? { ...i, inLibrary: libraryId !== null, libraryId } : i));

// Adds a title and stays on the page: a toast offers to open it, customize the add, or undo it (files are never deleted).
export function useAddTitle() {
  const toast = useToast();
  const openAdd = useAddDialog();
  return useCallback(async (item: DiscoveryItem, options: AddOptions = {}, onAdded?: OnAdded): Promise<LibraryItem> => {
    const added = await api.add(item, options);
    const kind = kindOfDiscovery(item);
    const key = titleKey({ movieId: kind === "movie" ? added.id : null, seriesId: kind === "movie" ? null : added.id }) ?? item.title;
    const searching = options.searchNow !== false && options.monitor !== "None";
    onAdded?.(item, added.id);
    const undo = async () => {
      try {
        await api.remove(kind, added.id, false);
        onAdded?.(item, null);
        return true;
      } catch (e) {
        toast({ key, icon: "alert", tone: "bad", title: item.title, message: `Could not undo: ${errorMessage(e)}` });
        return false;
      }
    };
    toast({
      key,
      icon: searching ? "search" : "check",
      tone: searching ? "seek" : "ok",
      title: item.title,
      message: searching ? "Added. Searching for releases…" : "Added to your library.",
      actions: [
        { label: "Open", href: titleHref(kind, added.id) },
        { label: "Customize", onClick: () => void undo().then((ok) => ok && openAdd(item, onAdded)) },
        { label: "Undo", onClick: () => void undo() },
      ],
    });
    return added;
  }, [toast, openAdd]);
}

export function AddDialogProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState<{ item: DiscoveryItem; onAdded?: OnAdded } | null>(null);
  const show = useCallback((item: DiscoveryItem, onAdded?: OnAdded) => setOpen({ item, onAdded }), []);
  const close = useCallback(() => setOpen(null), []);
  return (
    <AddContext value={show}>
      {children}
      {open && <AddSheet key={`${open.item.kind}${open.item.tmdbId}${open.item.tvdbId}`} item={open.item} onAdded={open.onAdded} onClose={close} />}
    </AddContext>
  );
}

const mainVersions = ["VO", "VF", "VOSTFR", "MULTi"];

const monitors: readonly { value: NonNullable<AddOptions["monitor"]>; label: string }[] = [
  { value: "All", label: "All" },
  { value: "Latest", label: "Latest" },
  { value: "Future", label: "Future only" },
  { value: "First", label: "First" },
];

function AddSheet({ item, onAdded, onClose }: { item: DiscoveryItem; onAdded?: OnAdded; onClose: () => void }) {
  const addTitle = useAddTitle();
  const options = useOptions();
  const kind = kindOfDiscovery(item);
  const defaults = options && defaultsFor(options.media, kind);
  const [version, setVersion] = useState<string | null>(null);
  const [subtitles, setSubtitles] = useState<string[] | null>(null);
  const [monitor, setMonitor] = useState<NonNullable<AddOptions["monitor"]>>("All");
  const [profile, setProfile] = useState("");
  const [searchNow, setSearchNow] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const defaultVersion = defaults?.version ?? options?.media.defaultVersion ?? "VO";
  const versions = [...new Set([defaultVersion, ...mainVersions])].filter((v) => options?.versions.includes(v));

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const added = await addTitle(item, {
        qualityProfileId: profile ? Number(profile) : null,
        version: version && version !== defaultVersion ? version : null,
        searchNow,
        ...(kind !== "movie" && { monitor }),
      }, onAdded);
      if (subtitles) await api.update(kind, added.id, { subtitles });
      onClose();
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
        <Button variant="primary" form="add-form" loading={busy} disabled={!options}>{searchNow ? "Add & search" : "Add"}</Button>
      </>}>
      <form id="add-form" onSubmit={submit} className="space-y-6 px-5 pt-2 pb-5">
        {!options || !defaults ? <div className="flex justify-center py-8 text-ink-2"><Spinner /></div> : (
          <>
            <div className="space-y-2">
              <p className="text-sm font-medium">Version</p>
              <Segmented label="Version" value={version ?? defaultVersion} onChange={setVersion} options={versions.map((v) => ({ value: v, label: v }))} />
            </div>
            <PickList label="Subtitles" values={subtitles ?? defaults.subtitles} choices={realLanguages(options).map((l) => l.name)}
              onChange={setSubtitles} empty="No subtitles" hint="Subtitles never change the audio." />
            {kind !== "movie" && (
              <div className="space-y-2">
                <p className="text-sm font-medium">Seasons</p>
                <Segmented label="Seasons to monitor" value={monitor} onChange={setMonitor} options={monitors} />
                <p className="text-[13px] text-ink-3">Fine-tune single seasons on the title page once it is added.</p>
              </div>
            )}
            <details className="group rounded-ctl">
              <summary className="flex min-h-11 cursor-pointer list-none items-center gap-2 text-sm font-medium text-ink-2 hover:text-ink">
                <Icon name="chevronRight" size={16} className="transition group-open:rotate-90" />More options
              </summary>
              <div className="space-y-4 pt-2">
                <Field label="Quality profile">
                  <Select value={profile} onChange={(e) => setProfile(e.target.value)}>
                    <option value="">Default ({options.profiles.find((p) => p.id === defaults.profileId)?.name ?? "none"})</option>
                    {options.profiles.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                  </Select>
                </Field>
                <Switch checked={searchNow} onChange={setSearchNow} label="Search now" description="Looks for releases as soon as it is added." />
              </div>
            </details>
          </>
        )}
        {error && <Alert>{error}</Alert>}
      </form>
    </Modal>
  );
}
