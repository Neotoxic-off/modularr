"use client";

import { useCallback, useEffect, useState } from "react";
import { api, errorMessage } from "@/lib/api";
import type { CollectionCard, CollectionDetail, CollectionPart, DiscoveryItem } from "@/lib/api";
import { useSession } from "@/components/AuthGate";
import { PosterCard, PosterGrid, PosterSkeleton } from "@/components/Poster";
import { useToast } from "@/components/Toasts";
import { Alert, Button, Modal, Switch } from "@/components/ui";

// RadarrAPI names end in " Collection"; the card reads better without it.
export const sagaName = (name: string) => name.replace(/ Collection$/i, "");

export const sagaCard = (d: CollectionDetail): CollectionCard => ({
  id: d.id, name: d.name, poster: d.poster, backdrop: d.backdrop, followed: d.followed, monitored: d.monitored, qualityProfileId: d.qualityProfileId,
  parts: d.parts.length, inLibrary: d.parts.filter((p) => p.movieId !== null).length,
});

const asItem = (p: CollectionPart): DiscoveryItem => ({
  kind: "Movie", tmdbId: p.tmdbId, tvdbId: null, imdbId: null, title: p.title, year: p.year || null, overview: null, status: null, genres: [],
  poster: p.poster, backdrop: null, rating: null, libraryId: p.movieId, inLibrary: p.movieId !== null, hasFile: p.hasFile,
});

const films = (n: number) => `${n} ${n === 1 ? "film" : "films"}`;

// Runs a saga action with a toast for the result or the error.
function useSagaAction(onChanged: (card: CollectionCard) => void) {
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  const run = async (id: number, name: string, action: () => Promise<CollectionDetail | void>, done?: (d: CollectionDetail) => string) => {
    setBusy(true);
    try {
      const detail = (await action()) ?? await api.collection(id);
      onChanged(sagaCard(detail));
      if (done) toast({ key: `collection:${id}`, icon: "layers", tone: "seek", title: sagaName(name), message: done(detail) });
      return detail;
    } catch (e) {
      toast({ key: `collection:${id}`, icon: "alert", tone: "bad", title: sagaName(name), message: errorMessage(e) });
    } finally {
      setBusy(false);
    }
  };
  return { busy, run };
}

// Follows the saga (new releases too) and adds every released part not in the library.
const addAll = (id: number) => api.followCollection(id, { monitored: true, addMissing: true });
const addedMessage = (before: number) => (d: CollectionDetail) => `${sagaCard(d).inLibrary - before} movie(s) added and searched`;

export function SagaCard({ saga, onOpen, onChanged }: { saga: CollectionCard; onOpen: () => void; onChanged: (card: CollectionCard) => void }) {
  const admin = useSession().user.role === "Admin";
  const { busy, run } = useSagaAction(onChanged);
  const art = saga.backdrop ?? saga.poster;
  const name = sagaName(saga.name);
  return (
    <article className="group relative flex aspect-video flex-col justify-end overflow-hidden rounded-tile bg-fill p-4 ring-1 ring-hair">
      {art && (
        // eslint-disable-next-line @next/next/no-img-element
        <img src={art} alt="" loading="lazy" className="absolute inset-0 h-full w-full object-cover transition duration-500 group-hover:scale-[1.03]" />
      )}
      <span aria-hidden="true" className="absolute inset-0 scrim-b" />
      <button onClick={onOpen} aria-label={`Open ${name}`} className="absolute inset-0 cursor-pointer" />
      <div className="pointer-events-none relative flex items-end gap-3">
        <div className="min-w-0 flex-1">
          <h2 className="scrim-text truncate text-title font-semibold" title={name}>{name}</h2>
          <p className="text-meta text-ink-2 tabular-nums">
            {saga.parts !== null && films(saga.parts)}{saga.parts !== null && saga.inLibrary > 0 && " · "}{saga.inLibrary > 0 && `${saga.inLibrary} in library`}
          </p>
        </div>
        {admin && saga.parts !== null && saga.inLibrary < saga.parts && (
          <Button variant="glass" size="sm" icon="plus" loading={busy} className="pointer-events-auto"
            onClick={() => run(saga.id, saga.name, () => addAll(saga.id), addedMessage(saga.inLibrary))}>Add all</Button>
        )}
      </div>
    </article>
  );
}

export function SagaModal({ id, onClose, onChanged }: { id: number; onClose: () => void; onChanged: (card: CollectionCard) => void }) {
  const admin = useSession().user.role === "Admin";
  const [detail, setDetail] = useState<CollectionDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  // Follow new releases: live once followed, otherwise applied by Add all missing.
  const [follow, setFollow] = useState(true);
  const { busy, run } = useSagaAction(onChanged);

  const load = useCallback(() => {
    api.collection(id)
      .then((d) => { setDetail(d); if (d.followed) setFollow(d.monitored); onChanged(sagaCard(d)); })
      .catch((e: unknown) => setError(errorMessage(e)));
  }, [id, onChanged]);
  useEffect(load, [load]);

  const act = (action: () => Promise<CollectionDetail | void>, done?: (d: CollectionDetail) => string) =>
    detail && run(id, detail.name, action, done).then((d) => d && setDetail(d));

  const owned = detail ? sagaCard(detail).inLibrary : 0;
  const missing = detail?.parts.filter((p) => p.movieId === null && p.released).length ?? 0;
  const toggle = (on: boolean) => {
    setFollow(on);
    if (detail?.followed) act(() => api.updateCollection(id, { monitored: on }));
  };

  return (
    <Modal size="lg" onClose={onClose} banner={detail?.backdrop}
      title={detail ? sagaName(detail.name) : "Saga"}
      description={detail && <span className="tabular-nums">{films(detail.parts.length)}{owned > 0 && ` · ${owned} in library`}</span>}
      footer={admin && detail && <>
        <div className="mr-auto"><Switch checked={follow} disabled={busy} onChange={toggle} label="Follow new releases" /></div>
        {missing > 0 ? (
          <Button variant="primary" icon="plus" loading={busy}
            onClick={() => act(() => follow ? addAll(id) : api.addMissing(id).then(() => undefined), addedMessage(owned))}>Add all missing</Button>
        ) : !detail.followed && follow && (
          <Button variant="primary" icon="plus" loading={busy}
            onClick={() => act(() => api.followCollection(id, { monitored: true }), () => "Following: new releases will be added")}>Follow</Button>
        )}
      </>}>
      <div className="px-5 pb-5">
        {error ? <Alert>{error}</Alert> : !detail ? (
          <PosterGrid>{[0, 1, 2, 3, 4].map((i) => <PosterSkeleton key={i} />)}</PosterGrid>
        ) : (
          <PosterGrid>
            {detail.parts.map((p) => (
              <div key={p.tmdbId}>
                <PosterCard item={asItem(p)} onAdded={load} />
                {!p.released && <p className="px-0.5 text-meta text-ink-3">{p.release ? `Out ${p.release}` : "Not released"}</p>}
              </div>
            ))}
          </PosterGrid>
        )}
      </div>
    </Modal>
  );
}
