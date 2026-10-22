"use client";

import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { api, errorMessage } from "@/lib/api";
import type { IndexerList, Paths, Role, User } from "@/lib/api";
import { FolderPicker } from "@/components/FolderPicker";
import { Icon } from "@/components/icons";
import { Alert, Badge, Button, Empty, Field, Input, SaveBar, Section, Select, Skeleton, Tabs, useAction } from "@/components/ui";

const pathFields: readonly { key: keyof Paths; label: string; hint: string }[] = [
  { key: "downloads", label: "Downloads", hint: "Where torrents download before they are imported." },
  { key: "movies", label: "Movies", hint: "Imported movies end up here." },
  { key: "series", label: "Series", hint: "Imported series end up here." },
  { key: "anime", label: "Anime", hint: "Imported anime end up here." },
];

export function Folders() {
  const [paths, setPaths] = useState<Paths | null>(null);
  const [picking, setPicking] = useState<keyof Paths | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const { busy, error, done, run } = useAction();

  useEffect(() => {
    api.paths().then(setPaths).catch((e: unknown) => setLoadError(errorMessage(e)));
  }, []);

  const save = (event: FormEvent) => {
    event.preventDefault();
    if (paths) void run(async () => setPaths(await api.savePaths(paths)));
  };

  return (
    <Section title="Folders" description="Paths on the server. In Docker, these are paths inside the container, so mount your drives as volumes.">
      {!paths ? (loadError ? <Alert>{loadError}</Alert> : <Skeleton className="h-64" />) : (
        <form onSubmit={save} className="space-y-5">
          <div className="grid gap-5 md:grid-cols-2">
            {pathFields.map(({ key, label, hint }) => (
              <Field key={key} label={label} hint={hint}>
                <div className="flex gap-2">
                  <Input value={paths[key]} onChange={(e) => setPaths({ ...paths, [key]: e.target.value })} className="font-mono text-xs" required />
                  <Button type="button" onClick={() => setPicking(key)} aria-label={`Browse for the ${label.toLowerCase()} folder`}><Icon name="folder" size={16} /></Button>
                </div>
              </Field>
            ))}
          </div>
          <SaveBar busy={busy} error={error} done={done} />
        </form>
      )}
      {paths && picking && (
        <FolderPicker start={paths[picking]} onClose={() => setPicking(null)}
          onPick={(path) => { setPaths({ ...paths, [picking]: path }); setPicking(null); }} />
      )}
    </Section>
  );
}

export function Indexers() {
  const [list, setList] = useState<IndexerList | null>(null);
  const [filter, setFilter] = useState<"enabled" | "all">("enabled");
  const [term, setTerm] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.indexers().then(setList).catch((e: unknown) => setError(errorMessage(e)));
  }, []);

  const needle = term.trim().toLowerCase();
  const shown = (list?.indexers ?? []).filter((i) =>
    (filter === "all" || i.enabled) && (!needle || i.name.toLowerCase().includes(needle) || i.id.includes(needle)));
  const failing = (list?.indexers ?? []).filter((i) => i.enabled && i.health.failures > 0).length;

  return (
    <Section title="Indexers" description="Every public movie and TV indexer is enabled automatically. Definitions sync daily."
      actions={list && <span className="text-sm text-muted"><span className="text-foreground tabular-nums">{list.enabled}</span> enabled{failing > 0 && <>, <span className="text-amber-200 tabular-nums">{failing}</span> failing</>}</span>}>
      <div className="mb-5 flex flex-wrap gap-2">
        <div className="relative min-w-48 flex-1 sm:max-w-72">
          <Icon name="search" size={16} className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-muted" />
          <Input value={term} onChange={(e) => setTerm(e.target.value)} placeholder="Filter indexers" aria-label="Filter indexers" className="pl-9" />
        </div>
        <Tabs label="Indexers" value={filter} onChange={setFilter} options={[
          { value: "enabled", label: "Enabled", count: list?.enabled },
          { value: "all", label: "All", count: list?.total },
        ]} />
      </div>
      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      {list?.lastSyncError && <div className="mb-4"><Alert tone="amber">The last definition sync failed: {list.lastSyncError}</Alert></div>}
      {!list ? <Skeleton className="h-64" /> : shown.length === 0 ? <Empty icon="globe" title="No indexer matches" /> : (
        <ul className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
          {shown.map((i) => (
            <li key={i.id} className="flex items-start gap-3 rounded-xl border border-line bg-background/40 p-3.5">
              <span title={!i.enabled ? "Disabled" : i.health.failures > 0 ? "Failing" : "Healthy"}
                className={`mt-1.5 size-2 shrink-0 rounded-full ${!i.enabled ? "bg-muted/40" : i.health.failures > 0 ? "bg-amber-400" : "bg-emerald-400 shadow-[0_0_8px] shadow-emerald-400/60"}`} />
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <p className="truncate text-sm font-medium">{i.name}</p>
                  {i.privacy !== "Public" && <Badge>{i.privacy === "Private" ? "Private" : "Semi-private"}</Badge>}
                </div>
                <p className="truncate text-xs text-muted">{i.health.activeUrl}</p>
                <p className="text-xs text-muted">{i.language}</p>
                {i.health.lastError && <p className="mt-1 truncate text-xs text-amber-200/80" title={i.health.lastError}>{i.health.lastError}</p>}
              </div>
            </li>
          ))}
        </ul>
      )}
    </Section>
  );
}

export function Users({ me }: { me: User }) {
  const [users, setUsers] = useState<User[] | null>(null);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [role, setRole] = useState<Role>("User");
  const { busy, error, run } = useAction();

  const reload = async () => setUsers(await api.users());

  useEffect(() => {
    api.users().then(setUsers).catch(() => setUsers([]));
  }, []);

  const create = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => {
      await api.createUser(username, password, role);
      setUsername("");
      setPassword("");
      await reload();
    });
  };

  return (
    <Section title="Users" description="Administrators manage settings, accounts and removals. Members browse, add and download.">
      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      {!users ? <Skeleton className="h-32" /> : (
        <ul className="mb-6 divide-y divide-line overflow-hidden rounded-xl border border-line">
          {users.map((u) => (
            <li key={u.id} className="flex items-center gap-3 bg-background/40 px-4 py-3">
              <span className="grid size-8 place-items-center rounded-full bg-accent/15 text-sm font-semibold text-accent uppercase">{u.username[0]}</span>
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-medium">{u.username}{u.id === me.id && <span className="font-normal text-muted"> (you)</span>}</span>
                <span className="block text-xs text-muted">Joined {new Date(u.created).toLocaleDateString("en", { dateStyle: "medium" })}</span>
              </span>
              <Select value={u.role} disabled={u.id === me.id || busy} aria-label={`Role of ${u.username}`} className="w-36"
                onChange={(e) => void run(async () => { await api.updateUser(u.id, { role: e.target.value as Role }); await reload(); })}>
                <option value="User">Member</option>
                <option value="Admin">Administrator</option>
              </Select>
              <Button variant="ghost" size="sm" aria-label={`Delete ${u.username}`} disabled={busy || u.id === me.id}
                onClick={() => confirm(`Delete the account ${u.username}?`) && void run(async () => { await api.deleteUser(u.id); await reload(); })}>
                <Icon name="trash" size={16} />
              </Button>
            </li>
          ))}
        </ul>
      )}
      <form onSubmit={create} className="space-y-3">
        <p className="text-sm font-medium">Add a user</p>
        <div className="grid gap-3 sm:grid-cols-[1fr_1fr_160px_auto]">
          <Input value={username} onChange={(e) => setUsername(e.target.value)} placeholder="Username" aria-label="New username" required />
          <Input type="password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder="Password (8+ characters)" aria-label="New user password" autoComplete="new-password" minLength={8} required />
          <Select value={role} onChange={(e) => setRole(e.target.value as Role)} aria-label="New user role">
            <option value="User">Member</option>
            <option value="Admin">Administrator</option>
          </Select>
          <Button variant="primary" disabled={busy}><Icon name="plus" size={16} />Add user</Button>
        </div>
      </form>
    </Section>
  );
}

export function Account({ me }: { me: User }) {
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const { busy, error, done, run } = useAction();

  const save = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => {
      await api.changePassword(current, next);
      setCurrent("");
      setNext("");
    });
  };

  return (
    <Section title="Your account" description={`Signed in as ${me.username}. Changing your password signs out your other sessions.`}>
      <form onSubmit={save} className="max-w-xl space-y-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Current password">
            <Input type="password" value={current} onChange={(e) => setCurrent(e.target.value)} autoComplete="current-password" required />
          </Field>
          <Field label="New password" hint="At least 8 characters.">
            <Input type="password" value={next} onChange={(e) => setNext(e.target.value)} autoComplete="new-password" minLength={8} required />
          </Field>
        </div>
        <SaveBar busy={busy} error={error} done={done} label="Change password" />
      </form>
    </Section>
  );
}
