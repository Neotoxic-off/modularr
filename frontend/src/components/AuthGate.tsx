"use client";

import { createContext, useCallback, useContext, useEffect, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import { api, errorMessage, UNAUTHORIZED } from "@/lib/api";
import type { AuthState, User } from "@/lib/api";
import { Alert, Button, Field, Input, Spinner } from "@/components/ui";

interface Session {
  user: User;
  logout: () => Promise<void>;
}

const SessionContext = createContext<Session | null>(null);

export function useSession(): Session {
  const session = useContext(SessionContext);
  if (!session) throw new Error("useSession outside AuthGate");
  return session;
}

// The whole app is behind this gate: the API refuses everything but auth until a session cookie exists.
export function AuthGate({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState | null>(null);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(() => {
    api.authState().then(setState).catch((e: unknown) => setError(errorMessage(e)));
  }, []);

  useEffect(() => {
    refresh();
    window.addEventListener(UNAUTHORIZED, refresh);
    return () => window.removeEventListener(UNAUTHORIZED, refresh);
  }, [refresh]);

  const logout = useCallback(async () => {
    await api.logout();
    setState((s) => s && { ...s, user: null });
  }, []);

  if (error) return <Centered><Alert>API injoignable : {error}</Alert></Centered>;
  if (!state) return <Centered><Spinner /></Centered>;
  if (!state.user) return <Login setup={state.setupRequired} onSignedIn={(user) => setState({ setupRequired: false, user })} />;

  return <SessionContext value={{ user: state.user, logout }}>{children}</SessionContext>;
}

function Login({ setup, onSignedIn }: { setup: boolean; onSignedIn: (user: User) => void }) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      onSignedIn(await (setup ? api.setup : api.login)(username, password));
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Centered>
      <form onSubmit={submit} className="w-full max-w-sm space-y-5 rounded-2xl border border-line bg-surface p-7 shadow-2xl">
        <div>
          <p className="text-xl font-semibold tracking-tight">modularr</p>
          <p className="mt-1 text-sm text-muted">{setup ? "Crée le compte administrateur." : "Connecte-toi pour continuer."}</p>
        </div>
        {error && <Alert>{error}</Alert>}
        <Field label="Utilisateur">
          <Input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" autoFocus required />
        </Field>
        <Field label="Mot de passe" hint={setup ? "8 caractères minimum." : undefined}>
          <Input type="password" value={password} onChange={(e) => setPassword(e.target.value)}
            autoComplete={setup ? "new-password" : "current-password"} minLength={setup ? 8 : undefined} required />
        </Field>
        <Button variant="primary" className="w-full" disabled={busy}>
          {busy ? <Spinner /> : setup ? "Créer le compte" : "Se connecter"}
        </Button>
      </form>
    </Centered>
  );
}

function Centered({ children }: { children: ReactNode }) {
  return <div className="flex min-h-screen items-center justify-center p-4">{children}</div>;
}
