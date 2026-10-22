"use client";

import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { api, errorMessage } from "@/lib/api";
import type { Profile, ProfileItem, Quality } from "@/lib/api";
import { reloadOptions } from "@/lib/options";
import { Icon } from "@/components/icons";
import { Alert, Button, Chip, Field, Input, SaveBar, Section, Select, Skeleton, Switch, useAction } from "@/components/ui";

// Groups carry their own id (>= 1000); single qualities are keyed by quality id.
const keyOf = (item: ProfileItem) => (item.quality ?? item.id);

export function Profiles() {
  const [profiles, setProfiles] = useState<Profile[] | null>(null);
  const [qualities, setQualities] = useState<ReadonlyMap<number, Quality>>(new Map());
  const [selected, setSelected] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([api.profiles(), api.qualities()])
      .then(([p, q]) => { setProfiles(p); setQualities(new Map(q.map((x) => [x.id, x]))); setSelected(p[0]?.id ?? null); })
      .catch((e: unknown) => setError(errorMessage(e)));
  }, []);

  if (error) return <Alert>{error}</Alert>;
  if (!profiles) return <Skeleton className="h-96 rounded-tile" />;

  const current = profiles.find((p) => p.id === selected) ?? null;

  const replace = (saved: Profile) => {
    setProfiles((list) => list && (list.some((p) => p.id === saved.id) ? list.map((p) => (p.id === saved.id ? saved : p)) : [...list, saved]));
    setSelected(saved.id);
    reloadOptions();
  };

  const taken = new Set(profiles.map((p) => p.name));

  async function duplicate(profile: Profile) {
    setError(null);
    try {
      let name = `${profile.name} copy`;
      for (let n = 2; taken.has(name); n++) name = `${profile.name} copy ${n}`;
      replace(await api.createProfile({ ...profile, id: 0, name }));
    } catch (e) {
      setError(errorMessage(e));
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center gap-2">
        {profiles.map((p) => (
          <Chip key={p.id} selected={p.id === selected} onClick={() => setSelected(p.id)}>{p.name}</Chip>
        ))}
      </div>
      {current && (
        <Editor key={current.id} profile={current} qualities={qualities} onSaved={replace} onDuplicate={() => duplicate(current)}
          onDeleted={() => { setProfiles(profiles.filter((p) => p.id !== current.id)); setSelected(profiles.find((p) => p.id !== current.id)?.id ?? null); reloadOptions(); }} />
      )}
    </div>
  );
}

function Editor({ profile: initial, qualities, onSaved, onDuplicate, onDeleted }: {
  profile: Profile;
  qualities: ReadonlyMap<number, Quality>;
  onSaved: (profile: Profile) => void;
  onDuplicate: () => void;
  onDeleted: () => void;
}) {
  const [profile, setProfile] = useState(initial);
  const [dragging, setDragging] = useState<number | null>(null);
  const { busy, error, done, run } = useAction();

  const name = (item: ProfileItem) => (item.quality !== null ? qualities.get(item.quality)?.name ?? `Quality ${item.quality}` : item.name ?? "Group");
  // Stored worst to best; shown best first, like a ranking.
  const ranked = [...profile.items].reverse();
  const allowed = ranked.filter((i) => i.allowed);

    const move = (from: number, to: number) => {
    if (to < 0 || to >= ranked.length || from === to) return;
    const items = [...ranked];
    items.splice(to, 0, ...items.splice(from, 1));
    setProfile({ ...profile, items: items.reverse() });
  };
  const toggle = (index: number, value: boolean) => {
    const items = ranked.map((item, i) => (i === index ? { ...item, allowed: value } : item));
    const stillAllowed = items.filter((i) => i.allowed);
    // Keeps the cutoff valid when its quality stops being allowed: fall back to the lowest allowed one.
    const lowest = stillAllowed.at(-1);
    const cutoff = !lowest || stillAllowed.some((i) => keyOf(i) === profile.cutoff) ? profile.cutoff : keyOf(lowest);
    setProfile({ ...profile, cutoff, items: items.reverse() });
  };

  const save = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => onSaved(await api.saveProfile(profile)));
  };

  const remove = () => {
    if (!confirm(`Delete the profile “${profile.name}”?`)) return;
    void run(async () => { await api.deleteProfile(profile.id); onDeleted(); });
  };

  return (
    <form onSubmit={save}>
      <Section title={profile.name || "Untitled profile"} description="Releases below an allowed quality are ignored. Once a file reaches the cutoff, upgrades stop."
        actions={<div className="flex gap-1">
          <Button type="button" variant="ghost" size="sm" onClick={onDuplicate}><Icon name="copy" size={14} />Duplicate</Button>
          <Button type="button" variant="ghost" size="sm" onClick={remove}><Icon name="trash" size={14} />Delete</Button>
        </div>}>
        <div className="grid gap-8 lg:grid-cols-[280px_minmax(0,1fr)]">
          <div className="space-y-5">
            <Field label="Name">
              <Input value={profile.name} onChange={(e) => setProfile({ ...profile, name: e.target.value })} required />
            </Field>
            <Switch checked={profile.upgradeAllowed} onChange={(upgradeAllowed) => setProfile({ ...profile, upgradeAllowed })}
              label="Upgrade files" description="Replace a file when a better allowed quality shows up, up to the cutoff." />
            <Field label="Cutoff" hint="Stop upgrading once a file reaches this quality.">
              <Select value={profile.cutoff} disabled={!profile.upgradeAllowed} onChange={(e) => setProfile({ ...profile, cutoff: Number(e.target.value) })}>
                {allowed.map((i) => <option key={keyOf(i)} value={keyOf(i)}>{name(i)}</option>)}
              </Select>
            </Field>
          </div>

          <div>
            <p className="mb-2 text-sm font-medium">Qualities <span className="font-normal text-ink-3">best first, drag or use the arrows to rank</span></p>
            <ol className="well divide-y divide-hair-2 overflow-hidden rounded-card">
              {ranked.map((item, i) => (
                <li key={keyOf(item)} draggable onDragStart={() => setDragging(i)} onDragEnd={() => setDragging(null)}
                  onDragOver={(e) => e.preventDefault()} onDrop={() => { if (dragging !== null) move(dragging, i); setDragging(null); }}
                  className={`flex items-center gap-3 bg-black/20 px-3 py-1 transition ${dragging === i ? "opacity-40" : ""} ${item.allowed ? "" : "text-ink-3"}`}>
                  <span className="cursor-grab text-ink-3 active:cursor-grabbing" aria-hidden="true"><Icon name="list" size={14} /></span>
                  <input type="checkbox" checked={item.allowed} onChange={(e) => toggle(i, e.target.checked)} aria-label={`Allow ${name(item)}`}
                    className="size-4 cursor-pointer accent-white" />
                  <span className="min-w-0 flex-1 truncate text-sm">
                    {name(item)}
                    {item.items.length > 0 && <span className="ml-2 text-xs text-ink-3">{item.items.map(name).join(", ")}</span>}
                  </span>
                  {profile.upgradeAllowed && keyOf(item) === profile.cutoff && <span className="rounded bg-fill-2 px-1.5 text-[11px] text-ink">Cutoff</span>}
                  <span className="flex">
                    <Button type="button" variant="ghost" size="sm" className="!h-7 !px-1.5" disabled={i === 0} onClick={() => move(i, i - 1)} aria-label={`Rank ${name(item)} higher`}><Icon name="up" size={14} /></Button>
                    <Button type="button" variant="ghost" size="sm" className="!h-7 !px-1.5" disabled={i === ranked.length - 1} onClick={() => move(i, i + 1)} aria-label={`Rank ${name(item)} lower`}><Icon name="down" size={14} /></Button>
                  </span>
                </li>
              ))}
            </ol>
          </div>
        </div>
        <div className="mt-6 border-t border-hair pt-4">
          <SaveBar busy={busy} error={error} done={done} disabled={allowed.length === 0} />
          {allowed.length === 0 && <p className="mt-2 text-xs text-warn">Allow at least one quality.</p>}
        </div>
      </Section>
    </form>
  );
}
