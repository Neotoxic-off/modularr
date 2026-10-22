"use client";

import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { api, errorMessage, isAnime } from "@/lib/api";
import type { Kind, LibraryItem, LookupItem } from "@/lib/api";
import { Icon } from "@/components/icons";
import { MediaDetail } from "@/components/MediaDetail";
import { Poster, PosterGrid } from "@/components/Poster";
import { Alert, Badge, Button, Empty, Input, PageHeader, Spinner } from "@/components/ui";

const titles: Record<Kind, string> = { movie: "Films", series: "Séries", anime: "Anime" };

const tmdbOrTvdb = (kind: Kind, item: { tmdbId?: number | null; tvdbId?: number | null }) => (kind === "movie" ? item.tmdbId : item.tvdbId);

const belongsTo = (kind: Kind, item: LibraryItem) => kind === "movie" || (kind === "anime") === (item.seriesType === "Anime");

export function MediaPage({ kind }: { kind: Kind }) {
  const [library, setLibrary] = useState<LibraryItem[] | null>(null);
  const [term, setTerm] = useState("");
  const [results, setResults] = useState<LookupItem[] | null>(null);
  const [searching, setSearching] = useState(false);
  const [adding, setAdding] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<LibraryItem | null>(null);

  useEffect(() => {
    api.library(kind)
      .then((items) => setLibrary(items.filter((i) => belongsTo(kind, i))))
      .catch((e: unknown) => setError(errorMessage(e)));
  }, [kind]);

  async function lookup(event: FormEvent) {
    event.preventDefault();
    if (!term.trim()) return;
    setSearching(true);
    setError(null);
    try {
      const found = await api.lookup(kind, term.trim());
      setResults(kind === "anime" ? found.filter((i) => isAnime(i.genres)) : found);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setSearching(false);
    }
  }

  async function add(item: LookupItem) {
    setAdding(tmdbOrTvdb(kind, item) ?? null);
    setError(null);
    try {
      const added = await api.add(kind, item);
      setLibrary((items) => [...(items ?? []).filter((i) => i.id !== added.id), { ...added, poster: added.poster ?? item.poster }]);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setAdding(null);
    }
  }

  const owned = new Set(library?.map((i) => tmdbOrTvdb(kind, i)));

  return (
    <>
      <PageHeader title={titles[kind]}>
        <form onSubmit={lookup} className="flex w-full gap-2 sm:w-96">
          <div className="relative flex-1">
            <Icon name="search" className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-muted" />
            <Input value={term} onChange={(e) => setTerm(e.target.value)} className="pl-9"
              placeholder={`Ajouter ${kind === "movie" ? "un film" : kind === "series" ? "une série" : "un anime"}…`}
              aria-label={`Rechercher ${titles[kind].toLowerCase()}`} />
          </div>
          <Button variant="primary" disabled={searching}>{searching ? <Spinner /> : "Chercher"}</Button>
        </form>
      </PageHeader>

      <div className="space-y-10">
        {error && <Alert>{error}</Alert>}

        {results && (
          <section className="space-y-4">
            <div className="flex items-center justify-between">
              <h2 className="text-sm font-medium tracking-wide text-muted uppercase">Résultats pour « {term} »</h2>
              <Button variant="ghost" onClick={() => setResults(null)}><Icon name="x" />Fermer</Button>
            </div>
            {results.length === 0 ? <Empty>Aucun résultat.</Empty> : (
              <PosterGrid>
                {results.map((item) => {
                  const key = tmdbOrTvdb(kind, item);
                  const has = owned.has(key);
                  return (
                    <Poster key={key} title={item.title} year={item.year} src={item.poster}
                      badge={has ? <Badge tone="green">Ajouté</Badge> : kind !== "anime" && isAnime(item.genres) ? <Badge tone="accent">Anime</Badge> : undefined}
                      action={!has && (
                        <Button variant="primary" className="w-full" onClick={() => add(item)} disabled={adding === key}>
                          {adding === key ? <Spinner /> : <><Icon name="plus" />Ajouter</>}
                        </Button>
                      )} />
                  );
                })}
              </PosterGrid>
            )}
          </section>
        )}

        <section className="space-y-4">
          {results && <h2 className="text-sm font-medium tracking-wide text-muted uppercase">Bibliothèque</h2>}
          {!library ? <Spinner /> : library.length === 0 ? (
            <Empty>Bibliothèque vide. Cherche un titre ci-dessus pour l’ajouter.</Empty>
          ) : (
            <PosterGrid>
              {library.map((item) => (
                <Poster key={item.id} title={item.title} year={item.year} src={item.poster} onClick={() => setSelected(item)}
                  subtitle={item.episodes !== undefined ? `${item.year} · ${item.episodesWithFile ?? 0}/${item.episodes} ép.` : undefined} />
              ))}
            </PosterGrid>
          )}
        </section>
      </div>

      {selected && (
        <MediaDetail kind={kind} item={selected} onClose={() => setSelected(null)}
          onRemoved={() => { setLibrary((items) => items?.filter((i) => i.id !== selected.id) ?? null); setSelected(null); }} />
      )}
    </>
  );
}
