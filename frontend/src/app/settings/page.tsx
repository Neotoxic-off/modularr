"use client";

import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { api, errorMessage } from "@/lib/api";
import type { Paths, Role, User } from "@/lib/api";
import { useSession } from "@/components/AuthGate";
import { FolderPicker } from "@/components/FolderPicker";
import { Icon } from "@/components/icons";
import { Alert, Badge, Button, Card, Field, Input, PageHeader, Spinner } from "@/components/ui";

export default function SettingsPage() {
  const { user } = useSession();
  return (
    <>
      <PageHeader title="Réglages" />
      <div className="max-w-3xl space-y-6">
        {user.role === "Admin" && <PathsCard />}
        <PasswordCard />
        {user.role === "Admin" && <UsersCard me={user} />}
      </div>
    </>
  );
}

// Saves through a shared submit wrapper so every card reports errors and success the same way.
function useAction() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    setDone(false);
    try {
      await action();
      setDone(true);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };
  return { busy, error, done, run };
}

const pathFields: readonly { key: keyof Paths; label: string; hint: string }[] = [
  { key: "downloads", label: "Téléchargements", hint: "Où les torrents sont téléchargés avant import." },
  { key: "movies", label: "Films", hint: "Bibliothèque des films importés." },
  { key: "series", label: "Séries", hint: "Bibliothèque des séries importées." },
  { key: "anime", label: "Anime", hint: "Bibliothèque des anime importés." },
];

function PathsCard() {
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
    <Card title="Dossiers" description="Les chemins sont ceux du serveur (du conteneur en Docker : monte tes disques en volume).">
      {!paths ? (loadError ? <Alert>{loadError}</Alert> : <Spinner />) : (
        <form onSubmit={save} className="space-y-4">
          {pathFields.map(({ key, label, hint }) => (
            <Field key={key} label={label} hint={hint}>
              <div className="flex gap-2">
                <Input value={paths[key]} onChange={(e) => setPaths({ ...paths, [key]: e.target.value })} className="font-mono" required />
                <Button type="button" onClick={() => setPicking(key)} aria-label={`Parcourir pour ${label}`}><Icon name="folder" /></Button>
              </div>
            </Field>
          ))}
          <Footer busy={busy} error={error} done={done} label="Enregistrer" />
        </form>
      )}
      {paths && picking && (
        <FolderPicker start={paths[picking]} onClose={() => setPicking(null)}
          onPick={(path) => { setPaths({ ...paths, [picking]: path }); setPicking(null); }} />
      )}
    </Card>
  );
}

function PasswordCard() {
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
    <Card title="Mon compte">
      <form onSubmit={save} className="grid gap-4 sm:grid-cols-2">
        <Field label="Mot de passe actuel">
          <Input type="password" value={current} onChange={(e) => setCurrent(e.target.value)} autoComplete="current-password" required />
        </Field>
        <Field label="Nouveau mot de passe" hint="8 caractères minimum.">
          <Input type="password" value={next} onChange={(e) => setNext(e.target.value)} autoComplete="new-password" minLength={8} required />
        </Field>
        <div className="sm:col-span-2"><Footer busy={busy} error={error} done={done} label="Changer le mot de passe" /></div>
      </form>
    </Card>
  );
}

function UsersCard({ me }: { me: User }) {
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
    <Card title="Utilisateurs" description="Les administrateurs gèrent les réglages, les comptes et les suppressions.">
      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      {!users ? <Spinner /> : (
        <ul className="mb-5 divide-y divide-line rounded-lg border border-line">
          {users.map((u) => (
            <li key={u.id} className="flex items-center gap-3 px-4 py-2.5">
              <span className="flex size-7 items-center justify-center rounded-full bg-raised text-xs font-semibold uppercase">{u.username[0]}</span>
              <span className="flex-1 truncate text-sm">{u.username}{u.id === me.id && <span className="text-muted"> (toi)</span>}</span>
              <select value={u.role} disabled={u.id === me.id || busy} aria-label={`Rôle de ${u.username}`}
                onChange={(e) => void run(async () => { await api.updateUser(u.id, { role: e.target.value as Role }); await reload(); })}
                className="rounded-md border border-line bg-surface px-2 py-1 text-xs">
                <option value="User">Utilisateur</option>
                <option value="Admin">Administrateur</option>
              </select>
              {u.id === me.id ? <Badge tone="accent">toi</Badge> : (
                <Button variant="ghost" aria-label={`Supprimer ${u.username}`} disabled={busy}
                  onClick={() => confirm(`Supprimer le compte ${u.username} ?`) && void run(async () => { await api.deleteUser(u.id); await reload(); })}>
                  <Icon name="trash" />
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
      <form onSubmit={create} className="grid gap-3 sm:grid-cols-[1fr_1fr_auto_auto]">
        <Input value={username} onChange={(e) => setUsername(e.target.value)} placeholder="Utilisateur" aria-label="Nouvel utilisateur" required />
        <Input type="password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder="Mot de passe" aria-label="Mot de passe" autoComplete="new-password" minLength={8} required />
        <select value={role} onChange={(e) => setRole(e.target.value as Role)} aria-label="Rôle" className="rounded-lg border border-line bg-surface px-3 text-sm">
          <option value="User">Utilisateur</option>
          <option value="Admin">Administrateur</option>
        </select>
        <Button variant="primary" disabled={busy}><Icon name="plus" />Ajouter</Button>
      </form>
    </Card>
  );
}

function Footer({ busy, error, done, label }: { busy: boolean; error: string | null; done: boolean; label: string }) {
  return (
    <div className="space-y-3">
      {error && <Alert>{error}</Alert>}
      <div className="flex items-center gap-3">
        <Button variant="primary" disabled={busy}>{busy ? <Spinner /> : label}</Button>
        {done && !busy && <span className="flex items-center gap-1 text-sm text-emerald-300"><Icon name="check" />Enregistré</span>}
      </div>
    </div>
  );
}
