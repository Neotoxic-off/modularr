"use client";

import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { api, errorMessage } from "@/lib/api";
import type { NotificationConnection, NotificationType } from "@/lib/api";
import { useDraft } from "@/components/settings/draft";
import { Alert, Button, Chip, Empty, Field, Input, SaveBar, Section, Select, Skeleton, Well, useAction } from "@/components/ui";

const events = [
  { key: "onGrab", label: "Grab" },
  { key: "onImport", label: "Import" },
  { key: "onFailure", label: "Failure" },
] as const;

const types: readonly { value: NotificationType; url: string }[] = [
  { value: "Webhook", url: "https://example.com/hook" },
  { value: "Discord", url: "https://discord.com/api/webhooks/…" },
  { value: "Plex", url: "http://192.168.1.10:32400" },
  { value: "Jellyfin", url: "http://192.168.1.10:8096" },
  { value: "Emby", url: "http://192.168.1.10:8096" },
];

const isMediaServer = (type: NotificationType) => type === "Plex" || type === "Jellyfin" || type === "Emby";

export function NotificationSettings() {
  const [connections, setConnections, load, dirty] = useDraft<NotificationConnection[]>();
  const [loadError, setLoadError] = useState<string | null>(null);
  const [tests, setTests] = useState<Record<number, { busy: boolean; ok: boolean; result: string | null }>>({});
  const { busy, error, run } = useAction();

  useEffect(() => {
    api.notifications().then(load).catch((e: unknown) => setLoadError(errorMessage(e)));
  }, [load]);

  if (!connections) return loadError ? <Alert>{loadError}</Alert> : <Skeleton className="h-96 rounded-tile" />;

  const update = (index: number, change: Partial<NotificationConnection>) =>
    setConnections(connections.map((c, i) => (i === index ? { ...c, ...change } : c)));
  const add = () => setConnections([...connections, { name: "", type: "Webhook", url: "", token: null, onGrab: true, onImport: true, onFailure: true }]);
  const remove = (index: number) => {
    setConnections(connections.filter((_, i) => i !== index));
    setTests({});
  };
  const test = async (index: number, connection: NotificationConnection) => {
    if (!connection.url || (isMediaServer(connection.type) && !connection.token)) {
      setTests((t) => ({ ...t, [index]: { busy: false, ok: false, result: isMediaServer(connection.type) ? "Add the URL and API token first." : "Add the URL first." } }));
      return;
    }

    setTests((t) => ({ ...t, [index]: { busy: true, ok: false, result: null } }));
    let state: { busy: boolean; ok: boolean; result: string | null };
    try {
      const r = await api.testNotification(connection);
      state = { busy: false, ok: r.ok, result: r.ok ? "Sent." : r.error };
    } catch (e) {
      state = { busy: false, ok: false, result: errorMessage(e) };
    }
    setTests((t) => ({ ...t, [index]: state }));
  };
  const save = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => load(await api.saveNotifications(connections)));
  };

  return (
    <form onSubmit={save} className="space-y-6">
      <Section title="Notifications" description="Post grabs, imports and failures to a webhook or Discord, and refresh Plex, Jellyfin or Emby after each import."
        actions={<Button size="sm" icon="plus" type="button" onClick={add}>Add</Button>}>
        {connections.length === 0 ? <Empty icon="bell" title="No connections" /> : (
          <div className="space-y-4">
            {connections.map((c, i) => (
              <Well key={i} className="space-y-4">
                <div className="grid gap-4 md:grid-cols-[1fr_180px]">
                  <Field label="Name">
                    <Input value={c.name} onChange={(e) => update(i, { name: e.target.value })} maxLength={100} required />
                  </Field>
                  <Field label="Type">
                    <Select value={c.type} onChange={(e) => update(i, { type: e.target.value as NotificationType })} aria-label="Type">
                      {types.map((t) => <option key={t.value} value={t.value}>{t.value}</option>)}
                    </Select>
                  </Field>
                </div>
                <div className={`grid gap-4 ${isMediaServer(c.type) ? "md:grid-cols-2" : ""}`}>
                  <Field label="URL" hint={c.type === "Discord" ? "The channel's webhook URL, under Integrations." : undefined}>
                    <Input type="url" value={c.url} onChange={(e) => update(i, { url: e.target.value })} placeholder={types.find((t) => t.value === c.type)?.url}
                      autoComplete="off" spellCheck={false} required />
                  </Field>
                  {isMediaServer(c.type) && (
                    <Field label="API token" hint={c.type === "Plex" ? "Your X-Plex-Token." : "An API key from the dashboard, under API Keys."}>
                      <Input type="password" value={c.token ?? ""} onChange={(e) => update(i, { token: e.target.value || null })} autoComplete="off" className="font-mono text-meta" required />
                    </Field>
                  )}
                </div>
                {isMediaServer(c.type)
                  ? <p className="text-meta text-ink-3">Libraries are refreshed after each import.</p>
                  : (
                    <div role="group" aria-label="Send on" className="flex flex-wrap items-center gap-2">
                      <span className="mr-1 text-body font-medium">Send on</span>
                      {events.map((e) => <Chip key={e.key} selected={c[e.key]} onClick={() => update(i, { [e.key]: !c[e.key] })}>{e.label}</Chip>)}
                    </div>
                  )}
                <div className="flex flex-wrap items-center gap-3 border-t border-hair-2 pt-4">
                  <Button size="sm" type="button" icon="bell" onClick={() => void test(i, c)} loading={tests[i]?.busy}>Send a test</Button>
                  {tests[i]?.result && <p role="status" className={`text-meta ${tests[i].ok ? "text-ok" : "text-warn"}`}>{tests[i].result}</p>}
                  <Button size="sm" type="button" variant="ghost" icon="trash" onClick={() => remove(i)} className="ml-auto">Remove</Button>
                </div>
              </Well>
            ))}
          </div>
        )}
      </Section>

      <SaveBar busy={busy} error={error} dirty={dirty} />
    </form>
  );
}
