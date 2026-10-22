"use client";

import { useEffect, useState } from "react";
import { api, errorMessage } from "@/lib/api";
import type { IndexerList } from "@/lib/api";
import { Icon } from "@/components/icons";
import { Alert, Badge, Empty, Input, PageHeader, Spinner, Tabs } from "@/components/ui";

type Filter = "enabled" | "all";

export default function IndexersPage() {
  const [list, setList] = useState<IndexerList | null>(null);
  const [filter, setFilter] = useState<Filter>("enabled");
  const [term, setTerm] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.indexers().then(setList).catch((e: unknown) => setError(errorMessage(e)));
  }, []);

  const needle = term.trim().toLowerCase();
  const shown = (list?.indexers ?? []).filter((i) =>
    (filter === "all" || i.enabled) && (!needle || i.name.toLowerCase().includes(needle) || i.id.includes(needle)));

  return (
    <>
      <PageHeader title="Indexers">
        <div className="flex w-full flex-wrap gap-2 sm:w-auto">
          <div className="relative flex-1 sm:w-64">
            <Icon name="search" className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-muted" />
            <Input value={term} onChange={(e) => setTerm(e.target.value)} placeholder="Filtrer…" aria-label="Filtrer les indexers" className="pl-9" />
          </div>
          <Tabs value={filter} onChange={setFilter} options={[
            { value: "enabled", label: "Actifs", count: list?.enabled },
            { value: "all", label: "Tous", count: list?.total },
          ]} />
        </div>
      </PageHeader>
      <p className="mb-5 text-sm text-muted">
        Tous les indexers publics films/séries sont activés automatiquement. Les définitions sont synchronisées chaque jour.
      </p>
      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      {list?.lastSyncError && <div className="mb-4"><Alert>Dernière synchronisation échouée : {list.lastSyncError}</Alert></div>}
      {!list ? <Spinner /> : shown.length === 0 ? <Empty>Aucun indexer.</Empty> : (
        <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {shown.map((i) => (
            <li key={i.id} className="flex items-start gap-3 rounded-xl border border-line bg-surface p-4">
              <span className={`mt-1.5 size-2 shrink-0 rounded-full ${!i.enabled ? "bg-muted/40" : i.health.failures > 0 ? "bg-amber-400" : "bg-emerald-400"}`} />
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <p className="truncate text-sm font-medium">{i.name}</p>
                  {i.privacy !== "Public" && <Badge>{i.privacy === "Private" ? "Privé" : "Semi-privé"}</Badge>}
                </div>
                <p className="truncate text-xs text-muted">{i.health.activeUrl} · {i.language}</p>
                {i.health.lastError && <p className="mt-1 truncate text-xs text-amber-300/80" title={i.health.lastError}>{i.health.lastError}</p>}
              </div>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
