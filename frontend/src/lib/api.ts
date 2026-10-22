// Typed client for the ASP.NET API (same origin in production, rewritten to the container in dev).

export type Kind = "movie" | "series" | "anime";
export type Role = "Admin" | "User";

export interface User {
  id: number;
  username: string;
  role: Role;
}

export interface AuthState {
  setupRequired: boolean;
  user: User | null;
}

// A movie (tmdbId) or show (tvdbId) from /lookup and /discover.
export interface LookupItem {
  kind: "Movie" | "Series" | "Anime";
  tmdbId: number | null;
  tvdbId: number | null;
  imdbId: string | null;
  title: string;
  year: number | null;
  overview: string | null;
  status: string | null;
  genres: string[];
  poster: string | null;
  backdrop: string | null;
  rating: number | null;
  libraryId: number | null;
  inLibrary: boolean;
}

export interface LibraryItem {
  id: number;
  tvdbId?: number;
  tmdbId?: number;
  title: string;
  year: number;
  seriesType?: "Standard" | "Daily" | "Anime";
  path: string;
  poster?: string | null;
  overview?: string | null;
  episodes?: number;
  episodesWithFile?: number;
}

export interface Release {
  id: string;
  title: string;
  indexer: string;
  size?: number | null;
  seeders?: number | null;
}

export interface Decision {
  release: Release;
  quality: string;
  languages: string[];
  approved: boolean;
  rejections: { reason: string; message: string }[];
}

export interface SearchResult {
  indexersSearched: number;
  releases: number;
  approved: number;
  errors: { name: string; message: string }[];
  decisions: Decision[];
}

export type DownloadState =
  | "Downloading" | "ImportBlocked" | "ImportPending" | "Importing" | "Imported" | "FailedPending" | "Failed" | "Ignored";

export interface Download {
  id: number;
  releaseTitle: string;
  indexer: string;
  state: DownloadState;
  category: "series" | "movies";
  added: string;
  status?: "Queued" | "Paused" | "Downloading" | "Completed" | "Failed" | "Warning" | null;
  message?: string | null;
  progress?: number | null;
  size?: number | null;
  downloadRate?: number | null;
  uploadRate?: number | null;
  eta?: number | null;
}

export interface Indexer {
  id: string;
  name: string;
  language: string;
  privacy: string;
  enabled: boolean;
  health: { activeUrl: string; failures: number; lastError?: string | null };
}

export interface IndexerList {
  total: number;
  enabled: number;
  lastSyncError?: string | null;
  indexers: Indexer[];
}

export interface Paths {
  series: string;
  movies: string;
  anime: string;
  downloads: string;
}

export interface Folder {
  path: string | null;
  parent: string | null;
  directories: { name: string; path: string }[];
}

// Fired on any 401 so the auth gate drops back to the login screen.
export const UNAUTHORIZED = "modularr:unauthorized";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api${path}`, {
    ...init,
    headers: init?.body ? { "Content-Type": "application/json" } : undefined,
  });
  if (response.status === 401 && !path.startsWith("/auth/")) window.dispatchEvent(new Event(UNAUTHORIZED));
  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as { error?: string } | null;
    throw new Error(body?.error ?? `${response.status} ${response.statusText}`);
  }
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

const send = <T>(method: string, path: string, body?: unknown) =>
  request<T>(path, { method, body: body === undefined ? undefined : JSON.stringify(body) });

// Anime lives in the series library; the backend types it from the "Anime" genre.
const apiType = (kind: Kind) => (kind === "movie" ? "movie" : "series");
const libraryPath = (kind: Kind) => (kind === "movie" ? "/movies" : "/series");

export const api = {
  authState: () => request<AuthState>("/auth/state"),
  setup: (username: string, password: string) => send<User>("POST", "/auth/setup", { username, password }),
  login: (username: string, password: string) => send<User>("POST", "/auth/login", { username, password }),
  logout: () => send<void>("POST", "/auth/logout"),
  changePassword: (currentPassword: string, newPassword: string) =>
    send<void>("POST", "/auth/password", { currentPassword, newPassword }),

  users: () => request<User[]>("/users"),
  createUser: (username: string, password: string, role: Role) => send<User>("POST", "/users", { username, password, role }),
  updateUser: (id: number, patch: { role?: Role; password?: string }) => send<User>("PATCH", `/users/${id}`, patch),
  deleteUser: (id: number) => send<void>("DELETE", `/users/${id}`),

  lookup: (kind: Kind, term: string) =>
    request<LookupItem[]>(`/lookup?type=${apiType(kind)}&term=${encodeURIComponent(term)}`),
  library: (kind: Kind) => request<LibraryItem[]>(libraryPath(kind)),
  add: (kind: Kind, item: LookupItem) =>
    send<LibraryItem>("POST", libraryPath(kind), kind === "movie" ? { tmdbId: item.tmdbId } : { tvdbId: item.tvdbId }),
  remove: (kind: Kind, id: number, deleteFiles: boolean) =>
    send<void>("DELETE", `${libraryPath(kind)}/${id}?deleteFiles=${deleteFiles}`),
  search: (kind: Kind, item: LibraryItem) =>
    request<SearchResult>(kind === "movie" ? `/search?type=movie&tmdbId=${item.tmdbId}` : `/search?type=series&tvdbId=${item.tvdbId}`),
  grab: (kind: Kind, item: LibraryItem, release: Release) =>
    send<Download>("POST", "/releases/grab", kind === "movie" ? { release, movieId: item.id } : { release, seriesId: item.id }),

  downloads: () => request<Download[]>("/downloads"),
  pause: (id: number) => send<void>("POST", `/downloads/${id}/pause`),
  resume: (id: number) => send<void>("POST", `/downloads/${id}/resume`),
  removeDownload: (id: number, deleteData: boolean, blocklist: boolean) =>
    send<void>("DELETE", `/downloads/${id}?deleteData=${deleteData}&blocklist=${blocklist}`),

  indexers: () => request<IndexerList>("/indexers"),

  paths: () => request<Paths>("/settings/paths"),
  savePaths: (paths: Paths) => send<Paths>("PUT", "/settings/paths", paths),
  folders: (path: string | null) => request<Folder>(`/filesystem${path ? `?path=${encodeURIComponent(path)}` : ""}`),
};

export const isAnime = (genres: readonly string[] | undefined) => genres?.some((g) => g.toLowerCase() === "anime") ?? false;

const units = ["o", "Ko", "Mo", "Go", "To"];

export function formatSize(bytes?: number | null): string {
  if (bytes == null) return "—";
  const exponent = Math.min(Math.floor(Math.log(Math.max(bytes, 1)) / Math.log(1024)), units.length - 1);
  return `${(bytes / 1024 ** exponent).toFixed(exponent > 1 ? 1 : 0)} ${units[exponent]}`;
}

export function formatEta(seconds?: number | null): string {
  if (seconds == null) return "—";
  if (seconds < 60) return `${Math.round(seconds)} s`;
  if (seconds < 3600) return `${Math.round(seconds / 60)} min`;
  return `${Math.floor(seconds / 3600)} h ${Math.round((seconds % 3600) / 60)} min`;
}

export const errorMessage = (e: unknown) => (e instanceof Error ? e.message : String(e));
