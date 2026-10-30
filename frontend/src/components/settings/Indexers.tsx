"use client";

import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { api, errorMessage } from "@/lib/api";
import type { Indexer, IndexerList, IndexerSettings, IndexerTest } from "@/lib/api";
import { dayTime } from "@/lib/format";
import { Icon } from "@/components/icons";
import { Alert, Badge, Button, Empty, Field, Input, Modal, Section, Select, Skeleton, Segmented, Switch, Well, WellList, toneDot, useAction } from "@/components/ui";

export function Indexers() {
  const [list, setList] = useState<IndexerList | null>(null);
  const [filter, setFilter] = useState<"enabled" | "all">("enabled");
  const [term, setTerm] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [tests, setTests] = useState<Readonly<Record<string, IndexerTest | "busy">>>({});
  const [openId, setOpenId] = useState<string | null>(null);
  const open = list?.indexers.find((i) => i.id === openId);

  useEffect(() => {
    api.indexers().then(setList).catch((e: unknown) => setError(errorMessage(e)));
  }, []);

  const needle = term.trim().toLowerCase();
  const shown = (list?.indexers ?? []).filter((i) =>
    (filter === "all" || i.enabled) && (!needle || i.name.toLowerCase().includes(needle) || i.id.includes(needle)));
  const failing = (list?.indexers ?? []).filter((i) => i.enabled && i.health.failures > 0).length;
  const testing = Object.values(tests).includes("busy");

  async function test(id: string) {
    setTests((t) => ({ ...t, [id]: "busy" }));
    let result: IndexerTest;
    try {
      result = await api.testIndexer(id);
      setList((l) => l && { ...l, indexers: l.indexers.map((i) => (i.id === id ? { ...i, health: result.health } : i)) });
    } catch (e) {
      result = { releases: 0, error: errorMessage(e), health: { activeUrl: "", failures: 0, lastError: null, lastCheck: null } };
    }
    setTests((t) => ({ ...t, [id]: result }));
  }

  return (
    <Section title="Indexers" description="Every movie and TV indexer that needs no login is enabled automatically; private ones once their login is filled in. Definitions sync daily."
      actions={list && failing > 0 && <span className="text-meta text-warn tabular-nums">{failing} failing</span>}>
      <div className="mb-5 flex flex-wrap gap-2">
        <Input icon="search" value={term} onChange={(e) => setTerm(e.target.value)} placeholder="Filter indexers" aria-label="Filter indexers" className="min-w-48 flex-1 sm:max-w-72" />
        <Button icon="refresh" loading={testing} disabled={testing || shown.length === 0}
          onClick={() => shown.filter((i) => i.enabled).forEach((i) => void test(i.id))}>Test {needle ? "shown" : "all"}</Button>
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
            <li key={i.id}>
              <Well className="flex h-full items-start gap-3">
                <span title={i.failing ? "Not responding, disabled until a test passes" : !i.enabled ? "Disabled" : i.health.failures > 0 ? "Failing" : "Healthy"}
                  className={`mt-1.5 size-2 shrink-0 rounded-full ${!i.enabled ? toneDot.neutral : i.health.failures > 0 ? toneDot.warn : toneDot.ok}`} />
                <button type="button" onClick={() => setOpenId(i.id)} className="min-w-0 flex-1 cursor-pointer text-left" aria-label={`${i.name} details`}>
                  <div className="flex items-center gap-2">
                    <p className="truncate text-body font-medium">{i.name}</p>
                    {i.privacy !== "Public" && <Badge>{i.privacy === "Private" ? "Private" : "Semi-private"}</Badge>}
                    {i.failing && <Badge tone="warn">Not responding</Badge>}
                    {i.needsLogin && !i.configured && <Badge tone="warn">Needs login</Badge>}
                  </div>
                  <p className="truncate text-meta text-ink-3">{i.health.activeUrl}</p>
                  <p className="text-meta text-ink-3">{i.language}</p>
                  {tests[i.id] && tests[i.id] !== "busy" ? <TestResult result={tests[i.id] as IndexerTest} />
                    : i.health.lastError && <p className="mt-1 line-clamp-3 break-words text-meta text-warn">{i.health.lastError}</p>}
                </button>
                <Button size="sm" variant="ghost" loading={tests[i.id] === "busy"} onClick={() => void test(i.id)} aria-label={`Test ${i.name}`}>Test</Button>
              </Well>
            </li>
          ))}
        </ul>
      )}
      {open && <IndexerDetails indexer={open} test={tests[open.id]} onTest={() => void test(open.id)} onClose={() => setOpenId(null)}
        onSaved={() => api.indexers().then(setList).catch((e: unknown) => setError(errorMessage(e)))} />}
    </Section>
  );
}

function IndexerDetails({ indexer: i, test, onTest, onClose, onSaved }: { indexer: Indexer; test?: IndexerTest | "busy"; onTest: () => void; onClose: () => void; onSaved: () => void }) {
  const status = !i.enabled ? (i.needsLogin && !i.configured ? "Needs login" : "Disabled") : i.failing ? "Not responding" : i.health.failures > 0 ? "Failing" : "Healthy";
  return (
    <Modal onClose={onClose} title={i.name} description={i.description}
      footer={<>
        <Button variant="ghost" onClick={onClose}>Close</Button>
        <Button icon="refresh" loading={test === "busy"} onClick={onTest}>Test</Button>
      </>}>
      <div className="space-y-6 px-5 pt-2 pb-5">
        <dl className="well grid grid-cols-2 gap-px overflow-hidden rounded-card sm:grid-cols-4">
          {[
            ["Status", <span key="s" className={status === "Healthy" ? "text-ok" : status === "Disabled" ? "text-ink-2" : "text-warn"}>{status}</span>],
            ["Access", i.privacy === "SemiPrivate" ? "Semi-private" : i.privacy],
            ["Language", i.language],
            ["Failures", <span key="f" className="tabular-nums">{i.health.failures}</span>],
          ].map(([label, value]) => (
            <div key={label as string} className="px-3.5 py-3">
              <dt className="text-meta text-ink-3">{label}</dt>
              <dd className="mt-0.5 truncate text-body font-medium">{value}</dd>
            </div>
          ))}
        </dl>

        <IndexerLogin id={i.id} onSaved={onSaved} />

        <div className="space-y-2">
          <p className="text-body font-medium">Mirrors</p>
          <WellList>
            {i.mirrors.map((m) => {
              const active = m === i.health.activeUrl;
              return (
                <li key={m} className="flex items-center gap-3 px-3.5 py-2.5">
                  <span className={`size-1.5 shrink-0 rounded-full ${active ? toneDot.ok : "bg-fill-3"}`} />
                  <a href={m} target="_blank" rel="noreferrer noopener" title={m}
                    className={`min-w-0 flex-1 truncate font-mono text-meta hover:underline ${active ? "text-ink" : "text-ink-2"}`}>{m.replace(/^https?:\/\//, "").replace(/\/$/, "")}</a>
                  {active && <Badge tone="ok">Active</Badge>}
                </li>
              );
            })}
          </WellList>
          <p className="text-meta text-ink-3">Every search asks all mirrors and merges their results; the active one answers tests and downloads first.</p>
        </div>

        {(i.health.lastCheck || i.health.lastError || (test && test !== "busy")) && (
          <div className="space-y-1">
            <p className="text-body font-medium">Last check</p>
            {i.health.lastCheck && <p className="text-meta text-ink-2">{dayTime(i.health.lastCheck)}</p>}
            {test && test !== "busy" ? <TestResult result={test} />
              : i.health.lastError && <p className="text-meta break-words text-warn">{i.health.lastError}</p>}
          </div>
        )}

        {i.categories.length > 0 && (
          <div className="space-y-2">
            <p className="text-body font-medium">Categories</p>
            <p className="text-meta text-ink-2">{i.categories.join(" · ")}</p>
          </div>
        )}
      </div>
    </Modal>
  );
}

// A secret is never sent back: empty keeps the stored one.
const valuesOf = (s: IndexerSettings) =>
  Object.fromEntries(s.fields.filter((f) => f.type !== "info").map((f) => [f.name, f.secret ? "" : f.value ?? f.default ?? ""]));

// The login form generated from the definition's settings. Without a login, only its options (checkboxes, lists) are shown.
// Save appears only once something changed; the modal's footer keeps Test.
function IndexerLogin({ id, onSaved }: { id: string; onSaved: () => void }) {
  const [settings, setSettings] = useState<IndexerSettings | null>(null);
  const [values, setValues] = useState<Record<string, string>>({});
  const { busy, error, run, setError } = useAction();

  const load = (s: IndexerSettings) => {
    setSettings(s);
    setValues(valuesOf(s));
  };

  useEffect(() => {
    api.indexerSettings(id).then(load).catch((e: unknown) => setError(errorMessage(e)));
  }, [id]); // eslint-disable-line react-hooks/exhaustive-deps

  const fields = settings?.fields.filter((f) => settings.needsLogin || f.type === "checkbox" || f.type === "select") ?? [];
  if (!settings) return error ? <Alert>{error}</Alert> : null;
  if (fields.every((f) => f.type === "info")) return null;
  const set = (name: string, value: string) => setValues((v) => ({ ...v, [name]: value }));
  const dirty = JSON.stringify(values) !== JSON.stringify(valuesOf(settings));

  return (
    <form className="space-y-4" onSubmit={(e: FormEvent) => {
      e.preventDefault();
      void run(async () => {
        load(await api.saveIndexerSettings(id, values));
        onSaved();
      });
    }}>
      <div className="flex items-center gap-2">
        <p className="text-body font-medium">{settings.needsLogin ? "Login" : "Options"}</p>
        {settings.needsLogin && (settings.configured ? <Badge tone="ok">Configured</Badge> : <Badge tone="warn">Needs login</Badge>)}
      </div>
      {settings.captcha && (
        <p className="text-meta text-ink-3">
          {settings.fields.some((f) => f.name === "cookie")
            ? "This site shows a captcha at login: paste a session cookie from your browser instead."
            : "This site shows a captcha at login, which modularr cannot solve."}
        </p>
      )}
      {fields.map((f) =>
        f.type === "info" ? <p key={f.name} className="text-meta text-ink-3">{(f.default ?? f.label).replace(/<[^>]+>/g, "")}</p>
        : f.type === "checkbox" ? <Switch key={f.name} checked={values[f.name] === "true"} onChange={(on) => set(f.name, on ? "true" : "false")} label={f.label} />
        : (
          <Field key={f.name} label={f.label} hint={f.secret && f.set ? "Saved. Leave empty to keep it." : undefined}>
            {f.type === "select" ? (
              <Select value={values[f.name] ?? ""} onChange={(e) => set(f.name, e.target.value)} aria-label={f.label}>
                {(f.options ?? []).map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
              </Select>
            ) : (
              <Input type={f.secret ? "password" : "text"} autoComplete="off" value={values[f.name] ?? ""} required={f.required && !f.set}
                onChange={(e) => set(f.name, e.target.value)} placeholder={f.secret && f.set ? "••••••••" : undefined} />
            )}
          </Field>
        ))}
      {error && <Alert>{error}</Alert>}
      {(dirty || busy) && <Button type="submit" variant="primary" loading={busy}>Save</Button>}
    </form>
  );
}

function TestResult({ result }: { result: IndexerTest }) {
  return result.error
    ? <p className="mt-1 line-clamp-3 break-words text-meta text-bad">{result.error}</p>
    : <p className="mt-1 flex items-center gap-1 text-meta text-ok"><Icon name="check" size={14} />Works, {result.releases} {result.releases === 1 ? "release" : "releases"}</p>;
}
