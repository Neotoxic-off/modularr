"use client";

import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { api, errorMessage } from "@/lib/api";
import type { ImportMode, IndexerList, MetadataSettings, Paths, Role, User } from "@/lib/api";
import { FolderPicker } from "@/components/FolderPicker";
import { Icon } from "@/components/icons";
import { Alert, Badge, Button, Empty, Field, Input, SaveBar, Section, Select, Skeleton, Segmented, useAction } from "@/components/ui";

const pathFields: readonly { key: keyof Paths; label: string; hint: string }[] = [
  { key: "downloads", label: "Downloads", hint: "Where torrents download before they are imported." },
  { key: "movies", label: "Movies", hint: "Your movie library." },
  { key: "series", label: "Series", hint: "Your series library." },
  { key: "anime", label: "Anime", hint: "Your anime library." },
];

const importModes: readonly { value: ImportMode; label: string; hint: string }[] = [
  { value: "Move", label: "Move", hint: "Files leave the downloads folder and the torrent is removed. No space is used twice, nothing seeds." },
  { value: "Hardlink", label: "Hardlink", hint: "Files are linked into the library and the torrent keeps seeding. Across drives they are copied." },
];

const SAVED_MS = 2000;

function Saved({ at }: { at: number }) {
  const [now, setNow] = useState(0);
  useEffect(() => {
    if (!at) return;
    const timer = setTimeout(() => setNow(Date.now()), SAVED_MS);
    return () => clearTimeout(timer);
  }, [at]);
  return at > now ? <span role="status" className="flex animate-fade items-center gap-1 text-[13px] text-ok"><Icon name="check" size={15} />Saved</span> : null;
}

const keySources: Record<MetadataSettings["source"], { label: string; tone: "ok" | "live" | "neutral" }> = {
  Custom: { label: "Custom key", tone: "ok" },
  Environment: { label: "From MODULARR_TMDB_API_KEY", tone: "live" },
  None: { label: "No key: limited Explore", tone: "neutral" },
};

// Each setting saves on its own: the mode as it is picked, the key when the field loses focus.
export function General() {
  const [mode, setMode] = useState<ImportMode | null>(null);
  const [metadata, setMetadata] = useState<MetadataSettings | null>(null);
  const [key, setKey] = useState<string | null>(null);
  const [saved, setSaved] = useState({ mode: 0, key: 0 });
  const [test, setTest] = useState<{ busy: boolean; result: string | null; ok: boolean }>({ busy: false, result: null, ok: false });
  const [error, setError] = useState<string | null>(null);

  const showMetadata = (m: MetadataSettings) => {
    setMetadata(m);
    setKey(m.tmdbApiKey ?? "");
  };

  useEffect(() => {
    api.importMode().then((r) => setMode(r.importMode)).catch((e: unknown) => setError(errorMessage(e)));
    api.metadata().then(showMetadata).catch((e: unknown) => setError(errorMessage(e)));
  }, []);

  async function saveMode(next: ImportMode) {
    const previous = mode;
    setMode(next);
    try {
      setMode((await api.saveImportMode(next)).importMode);
      setSaved((s) => ({ ...s, mode: Date.now() }));
      setError(null);
    } catch (e) {
      setMode(previous);
      setError(errorMessage(e));
    }
  }

  async function saveKey(value: string | null) {
    if ((value?.trim() || null) === (metadata?.tmdbApiKey ?? null)) return;
    try {
      showMetadata(await api.saveMetadata(value?.trim() || null));
      setSaved((s) => ({ ...s, key: Date.now() }));
      setTest({ busy: false, result: null, ok: false });
      setError(null);
    } catch (e) {
      setError(errorMessage(e));
    }
  }

  async function testKey() {
    setTest({ busy: true, result: null, ok: false });
    try {
      const r = await api.testMetadata();
      setTest({ busy: false, result: r.ok ? "TMDB accepts the key." : r.error, ok: r.ok });
    } catch (e) {
      setTest({ busy: false, result: errorMessage(e), ok: false });
    }
  }

  return (
    <div className="space-y-6">
      {error && <Alert>{error}</Alert>}
      <Section title="After a download" description="What happens to finished downloads when they join your library." actions={<Saved at={saved.mode} />}>
        {!mode ? <Skeleton className="h-20" /> : (
          <div className="space-y-3">
            <Segmented label="Import mode" value={mode} onChange={(m) => void saveMode(m)} options={importModes} />
            <p className="text-[13px] text-ink-3">{importModes.find((m) => m.value === mode)?.hint}</p>
          </div>
        )}
      </Section>
      <Section title="TMDB" description="A TMDB key opens the full movie catalogue in Explore, with every filter." actions={<Saved at={saved.key} />}>
        {!metadata || key === null ? <Skeleton className="h-20" /> : (
          <div className="space-y-4">
            <div className="flex flex-wrap items-center gap-3">
              <Badge tone={keySources[metadata.source].tone}>{keySources[metadata.source].label}</Badge>
              {metadata.effective && <span className="font-mono text-xs text-ink-2" aria-label="Key in use, masked">{metadata.effective}</span>}
              <span className="flex-1" />
              <Button size="sm" onClick={() => void testKey()} loading={test.busy} disabled={metadata.source === "None"}>Test</Button>
              {metadata.source === "Custom" && (
                <Button size="sm" variant="ghost" onClick={() => void saveKey(null)}>Use the environment key</Button>
              )}
            </div>
            {test.result && <p role="status" className={`text-[13px] ${test.ok ? "text-ok" : "text-warn"}`}>{test.result}</p>}
            <Field label="Custom API key or read access token" hint="Overrides MODULARR_TMDB_API_KEY. Free from themoviedb.org, under Settings, API. Empty falls back to the environment key, then to the short built-in lists.">
              <Input type="password" value={key} onChange={(e) => setKey(e.target.value)} onBlur={() => void saveKey(key)} autoComplete="off" spellCheck={false} className="font-mono text-xs" />
            </Field>
            <p className="text-xs text-ink-3">This product uses the TMDB API but is not endorsed or certified by TMDB.</p>
          </div>
        )}
      </Section>
    </div>
  );
}

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
    <Section title="Folders" description="Finished downloads are moved here automatically. Paths are on the server: in Docker, inside the container.">
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
      actions={list && <span className="text-sm text-ink-3"><span className="text-ink tabular-nums">{list.enabled}</span> enabled{failing > 0 && <>, <span className="text-warn tabular-nums">{failing}</span> failing</>}</span>}>
      <div className="mb-5 flex flex-wrap gap-2">
        <div className="relative min-w-48 flex-1 sm:max-w-72">
          <Icon name="search" size={16} className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-ink-3" />
          <Input value={term} onChange={(e) => setTerm(e.target.value)} placeholder="Filter indexers" aria-label="Filter indexers" className="pl-9" />
        </div>
        <Segmented label="Indexers" value={filter} onChange={setFilter} options={[
          { value: "enabled", label: "Enabled", count: list?.enabled },
          { value: "all", label: "All", count: list?.total },
        ]} />
      </div>
      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      {list?.lastSyncError && <div className="mb-4"><Alert tone="warn">The last definition sync failed: {list.lastSyncError}</Alert></div>}
      {!list ? <Skeleton className="h-64" /> : shown.length === 0 ? <Empty icon="globe" title="No indexer matches" /> : (
        <ul className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
          {shown.map((i) => (
            <li key={i.id} className="flex items-start gap-3 well rounded-card p-3.5">
              <span title={!i.enabled ? "Disabled" : i.health.failures > 0 ? "Failing" : "Healthy"}
                className={`mt-1.5 size-2 shrink-0 rounded-full ${!i.enabled ? "bg-ink-3" : i.health.failures > 0 ? "bg-warn" : "bg-ok shadow-[0_0_8px] shadow-ok/60"}`} />
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <p className="truncate text-sm font-medium">{i.name}</p>
                  {i.privacy !== "Public" && <Badge>{i.privacy === "Private" ? "Private" : "Semi-private"}</Badge>}
                </div>
                <p className="truncate text-xs text-ink-3">{i.health.activeUrl}</p>
                <p className="text-xs text-ink-3">{i.language}</p>
                {i.health.lastError && <p className="mt-1 truncate text-xs text-warn" title={i.health.lastError}>{i.health.lastError}</p>}
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
        <ul className="mb-6 well divide-y divide-hair-2 overflow-hidden rounded-card">
          {users.map((u) => (
            <li key={u.id} className="flex items-center gap-3 bg-black/20 px-4 py-3">
              <span className="grid size-8 place-items-center rounded-full bg-fill-2 text-sm font-semibold text-ink uppercase">{u.username[0]}</span>
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-medium">{u.username}{u.id === me.id && <span className="font-normal text-ink-3"> (you)</span>}</span>
                <span className="block text-xs text-ink-3">Joined {new Date(u.created).toLocaleDateString("en", { dateStyle: "medium" })}</span>
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
