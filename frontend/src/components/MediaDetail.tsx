"use client";

import { useState } from "react";
import { api, errorMessage, formatSize } from "@/lib/api";
import type { Kind, LibraryItem, Release, SearchResult } from "@/lib/api";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { Alert, Badge, Button, Modal, Spinner } from "@/components/ui";

export function MediaDetail({ kind, item, onClose, onRemoved }: {
  kind: Kind;
  item: LibraryItem;
  onClose: () => void;
  onRemoved: () => void;
}) {
  const { user } = useSession();
  const [result, setResult] = useState<SearchResult | null>(null);
  const [searching, setSearching] = useState(false);
  const [grabbed, setGrabbed] = useState<ReadonlySet<string>>(new Set());
  const [error, setError] = useState<string | null>(null);

  async function run(action: () => Promise<void>) {
    setError(null);
    try {
      await action();
    } catch (e) {
      setError(errorMessage(e));
    }
  }

  const search = () => run(async () => {
    setSearching(true);
    try {
      setResult(await api.search(kind, item));
    } finally {
      setSearching(false);
    }
  });

  const grab = (release: Release) => run(async () => {
    await api.grab(kind, item, release);
    setGrabbed((ids) => new Set(ids).add(release.id));
  });

  const [confirming, setConfirming] = useState(false);
  const [deleteFiles, setDeleteFiles] = useState(false);

  const remove = () => run(async () => {
    await api.remove(kind, item.id, deleteFiles);
    onRemoved();
  });

  return (
    <Modal wide onClose={onClose} title={<h2 className="truncate font-semibold">{item.title} <span className="font-normal text-muted">{item.year}</span></h2>}>
      <div className="flex flex-col gap-6 p-5 sm:flex-row">
        {item.poster && (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={item.poster} alt="" className="w-32 shrink-0 self-start rounded-lg ring-1 ring-line sm:w-40" />
        )}
        <div className="min-w-0 flex-1 space-y-4">
          {item.overview && <p className="text-sm leading-relaxed text-muted">{item.overview}</p>}
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
            <dt className="text-muted">Dossier</dt>
            <dd className="truncate font-mono text-xs leading-5" title={item.path}>{item.path}</dd>
            {item.episodes !== undefined && (<><dt className="text-muted">Épisodes</dt><dd>{item.episodesWithFile ?? 0} / {item.episodes}</dd></>)}
          </dl>
          <div className="flex flex-wrap gap-2">
            <Button variant="primary" onClick={search} disabled={searching}>
              {searching ? <Spinner /> : <Icon name="search" />}{result ? "Relancer la recherche" : "Chercher des releases"}
            </Button>
            {user.role === "Admin" && !confirming && <Button variant="ghost" onClick={() => setConfirming(true)}><Icon name="trash" />Retirer</Button>}
          </div>
          {confirming && (
            <div className="flex flex-wrap items-center gap-3 rounded-lg border border-red-500/25 bg-red-500/5 p-3 text-sm">
              <label className="flex flex-1 items-center gap-2">
                <input type="checkbox" checked={deleteFiles} onChange={(e) => setDeleteFiles(e.target.checked)} className="accent-red-400" />
                Supprimer aussi les fichiers du disque
              </label>
              <Button variant="ghost" onClick={() => setConfirming(false)}>Annuler</Button>
              <Button variant="danger" onClick={remove}>Retirer</Button>
            </div>
          )}
          {error && <Alert>{error}</Alert>}
        </div>
      </div>

      {searching && !result && <p className="flex items-center gap-2 px-5 pb-5 text-sm text-muted"><Spinner />Recherche sur tous les indexers…</p>}

      {result && (
        <div className="border-t border-line">
          <p className="px-5 py-3 text-xs text-muted">
            {result.approved} acceptées sur {result.releases} · {result.indexersSearched} indexers
            {result.errors.length > 0 && ` · ${result.errors.length} indexers en erreur`}
          </p>
          <ul>
            {result.decisions.map(({ release, quality, languages, approved, rejections }) => (
              <li key={release.id} className={`flex items-center gap-3 border-t border-line/60 px-5 py-3 ${approved ? "" : "opacity-55"}`}>
                <div className="min-w-0 flex-1 space-y-1">
                  <p className="truncate text-sm" title={release.title}>{release.title}</p>
                  <div className="flex flex-wrap items-center gap-1.5 text-xs text-muted">
                    <Badge tone={approved ? "accent" : "neutral"}>{quality}</Badge>
                    <span>{formatSize(release.size)}</span>·<span>{release.seeders ?? "?"} seeds</span>·<span>{release.indexer}</span>
                    {languages.length > 0 && <>·<span>{languages.join(", ")}</span></>}
                  </div>
                  {!approved && rejections[0] && (
                    <p className="truncate text-xs text-amber-300/80" title={rejections.map((r) => r.message).join("\n")}>{rejections[0].message}</p>
                  )}
                </div>
                <Button onClick={() => grab(release)} disabled={grabbed.has(release.id)} aria-label={`Télécharger ${release.title}`}>
                  {grabbed.has(release.id) ? <Icon name="check" /> : <Icon name="download" />}
                </Button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </Modal>
  );
}
