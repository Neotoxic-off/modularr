"use client";

import { createContext, useCallback, useContext, useEffect, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import { api, errorMessage, UNAUTHORIZED } from "@/lib/api";
import type { AuthState, User } from "@/lib/api";
import { Ambient } from "@/components/Ambient";
import { Alert, Button, Field, Input, Logo, Spinner } from "@/components/ui";

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

  if (error) return <Centered><Alert>Can’t reach modularr server: {error}</Alert></Centered>;
  if (!state) return <Centered><Spinner className="text-ink-2" /></Centered>;
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
    <main className="grid min-h-dvh place-items-center sm:p-4">
      <Ambient src={null} />
      <form onSubmit={submit} className="glass flex min-h-dvh w-full animate-rise flex-col justify-center gap-5 rounded-none border-0 p-6 pt-[max(24px,env(safe-area-inset-top))] sm:min-h-0 sm:max-w-sm sm:rounded-panel sm:border sm:p-8">
        <div className="space-y-6">
          <Logo />
          <div>
            <h1 className="font-serif text-[40px] leading-none">{setup ? "Make it yours." : "Welcome back."}</h1>
            <p className="mt-2 text-[13px] text-ink-2">{setup ? "Create the admin account. It manages settings, users and the library." : "Sign in to continue to your library."}</p>
          </div>
        </div>
        {error && <Alert>{error}</Alert>}
        <Field label="Username">
          <Input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" autoFocus required />
        </Field>
        <Field label="Password" hint={setup ? "At least 8 characters." : undefined}>
          <Input type="password" value={password} onChange={(e) => setPassword(e.target.value)}
            autoComplete={setup ? "new-password" : "current-password"} minLength={setup ? 8 : undefined} required />
        </Field>
        <Button variant="primary" className="w-full" loading={busy}>{setup ? "Create account" : "Sign in"}</Button>
      </form>
    </main>
  );
}

function Centered({ children }: { children: ReactNode }) {
  return <div className="flex min-h-dvh items-center justify-center p-4"><Ambient src={null} />{children}</div>;
}
