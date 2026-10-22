// Typed client for the ASP.NET API (same origin in production, rewritten to the container in dev).

export type Kind = "movie" | "series" | "anime";
export type Role = "Admin" | "User";
export type Category = "Movie" | "Series" | "Anime";

export interface User {
  id: number;
  username: string;
  role: Role;
  created: string;
}

export interface AuthState {
  setupRequired: boolean;
  user: User | null;
}

// A movie (tmdbId) or show (tvdbId) from /lookup and /discover.
export interface DiscoveryItem {
  kind: Category;
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

export interface DiscoverList {
  items: DiscoveryItem[];
  error: string | null;
}

interface ItemBase {
  id: number;
  title: string;
  year: number;
  runtime: number;
  monitored: boolean;
  path: string;
  qualityProfileId: number;
  poster: string | null;
  backdrop: string | null;
  overview: string | null;
  rating: number | null;
  genres: string[];
  certification: string | null;
  originalLanguage: string;
  language: string | null;
  wantedLanguage: string;
  lastSearch: { time: string; result: string } | null;
}

export interface Movie extends ItemBase {
  tmdbId: number;
  imdbId: string | null;
}

export interface Series extends ItemBase {
  tvdbId: number;
  seriesType: "Standard" | "Daily" | "Anime";
  added: string;
  seasons: { number: number; monitored: boolean }[];
  episodes: number;
  episodesWithFile: number;
}

export type LibraryItem = Movie | Series;

export interface Episode {
  id: number;
  seasonNumber: number;
  episodeNumber: number;
  absoluteEpisodeNumber: number | null;
  airDate: string | null;
  title: string | null;
  monitored: boolean;
  hasFile: boolean;
}

export interface SeriesDetail {
  series: Series;
  seasons: { number: number; monitored: boolean; episodes: number; episodesWithFile: number }[];
  episodes: Episode[];
}

export interface MovieDetail {
  movie: Movie;
  hasFile: boolean;
}

export interface ItemPatch {
  qualityProfileId?: number;
  monitored?: boolean;
  language?: string | null;
}

export interface AddOptions {
  qualityProfileId?: number | null;
  language?: string | null;
  searchNow?: boolean;
  monitor?: "All" | "Future" | "None";
}

export interface Release {
  id: string;
  title: string;
  indexer: string;
  publishDate: string;
  size: number | null;
  seeders: number | null;
  peers: number | null;
}

export interface Decision {
  release: Release;
  quality: string;
  languages: string[];
  releaseGroup: string | null;
  approved: boolean;
  temporarilyRejected: boolean;
  rejections: { reason: string; message: string; type: string }[];
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
  seriesId: number | null;
  movieId: number | null;
  category: "series" | "movies";
  added: string;
  status: "Queued" | "Paused" | "Downloading" | "Completed" | "Failed" | "Warning" | null;
  message: string | null;
  progress: number | null;
  size: number | null;
  remaining: number | null;
  downloadRate: number | null;
  uploadRate: number | null;
  ratio: number | null;
  eta: number | null;
}

export interface Indexer {
  id: string;
  name: string;
  language: string;
  privacy: string;
  enabled: boolean;
  health: { activeUrl: string; failures: number; lastError: string | null; lastCheck: string | null };
}

export interface IndexerList {
  total: number;
  enabled: number;
  lastSyncError: string | null;
  indexers: Indexer[];
}

export interface Quality {
  id: number;
  name: string;
  source: string;
  resolution: number;
}

export interface Language {
  id: number;
  name: string;
}

// id 0 for a single quality, >= 1000 for a group of qualities.
export interface ProfileItem {
  id: number;
  name: string | null;
  quality: number | null;
  items: ProfileItem[];
  allowed: boolean;
}

export interface Profile {
  id: number;
  name: string;
  upgradeAllowed: boolean;
  cutoff: number;
  items: ProfileItem[];
}

export interface MediaDefaults {
  profileId: number;
  language: string | null;
}

export interface LanguageRule {
  category: Category | null;
  originalLanguage: string | null;
  language: string;
}

export interface MediaSettings {
  movies: MediaDefaults;
  series: MediaDefaults;
  anime: MediaDefaults;
  defaultLanguage: string;
  exceptions: LanguageRule[];
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

// Fired on any 401 so the auth gate drops back to the sign-in screen.
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
  return response.status === 204 || response.status === 202 ? (undefined as T) : ((await response.json()) as T);
}

const send = <T>(method: string, path: string, body?: unknown) =>
  request<T>(path, { method, body: body === undefined ? undefined : JSON.stringify(body) });

// Anime lives in the series library; the backend types it from the "Anime" genre.
export const libraryPath = (kind: Kind) => (kind === "movie" ? "/movies" : "/series");

export const isSeries = (item: LibraryItem): item is Series => "tvdbId" in item;

export const kindOf = (item: LibraryItem): Kind => (!isSeries(item) ? "movie" : item.seriesType === "Anime" ? "anime" : "series");

export const kindOfDiscovery = (item: DiscoveryItem): Kind => (item.kind === "Movie" ? "movie" : item.kind === "Series" ? "series" : "anime");

export const titleHref = (kind: Kind, id: number) => `/title/?type=${kind === "movie" ? "movie" : "series"}&id=${id}`;

export const api = {
  authState: () => request<AuthState>("/auth/state"),
  setup: (username: string, password: string) => send<User>("POST", "/auth/setup", { username, password }),
  login: (username: string, password: string) => send<User>("POST", "/auth/login", { username, password }),
  logout: () => send<void>("POST", "/auth/logout"),
  changePassword: (currentPassword: string, newPassword: string) =>
    send<User>("POST", "/auth/password", { currentPassword, newPassword }),

  users: () => request<User[]>("/users"),
  createUser: (username: string, password: string, role: Role) => send<User>("POST", "/users", { username, password, role }),
  updateUser: (id: number, patch: { role?: Role; password?: string }) => send<User>("PATCH", `/users/${id}`, patch),
  deleteUser: (id: number) => send<void>("DELETE", `/users/${id}`),

  lookup: (term: string, signal?: AbortSignal) =>
    request<DiscoveryItem[]>(`/lookup?type=all&term=${encodeURIComponent(term)}`, { signal }),
  discover: (kind: Kind, list: string) => request<DiscoverList>(`/discover/${kind === "movie" ? "movies" : kind}?list=${list}`),

  movies: () => request<Movie[]>("/movies"),
  movie: (id: number) => request<MovieDetail>(`/movies/${id}`),
  series: () => request<Series[]>("/series"),
  seriesDetail: (id: number) => request<SeriesDetail>(`/series/${id}`),
  add: (item: DiscoveryItem, options: AddOptions = {}) =>
    item.kind === "Movie"
      ? send<Movie>("POST", "/movies", { tmdbId: item.tmdbId, ...options })
      : send<Series>("POST", "/series", { tvdbId: item.tvdbId, ...options }),
  update: (kind: Kind, id: number, patch: ItemPatch) => send<LibraryItem>("PATCH", `${libraryPath(kind)}/${id}`, patch),
  remove: (kind: Kind, id: number, deleteFiles: boolean) =>
    send<void>("DELETE", `${libraryPath(kind)}/${id}?deleteFiles=${deleteFiles}`),
  autoSearch: (kind: Kind, id: number, season?: number) =>
    send<void>("POST", `${libraryPath(kind)}/${id}/search${season === undefined ? "" : `?season=${season}`}`),
  releases: (item: LibraryItem, season?: number) =>
    request<SearchResult>(isSeries(item)
      ? `/search?type=series&tvdbId=${item.tvdbId}${season === undefined ? "" : `&season=${season}`}`
      : `/search?type=movie&tmdbId=${item.tmdbId}`),
  grab: (item: LibraryItem, release: Release) =>
    send<{ id: number }>("POST", "/releases/grab", isSeries(item) ? { release, seriesId: item.id } : { release, movieId: item.id }),

  downloads: () => request<Download[]>("/downloads"),
  pause: (id: number) => send<void>("POST", `/downloads/${id}/pause`),
  resume: (id: number) => send<void>("POST", `/downloads/${id}/resume`),
  removeDownload: (id: number, deleteData: boolean, blocklist: boolean) =>
    send<void>("DELETE", `/downloads/${id}?deleteData=${deleteData}&blocklist=${blocklist}`),

  indexers: () => request<IndexerList>("/indexers"),

  qualities: () => request<Quality[]>("/qualities"),
  languages: () => request<Language[]>("/languages"),
  profiles: () => request<Profile[]>("/profiles"),
  createProfile: (profile: Profile) => send<Profile>("POST", "/profiles", profile),
  saveProfile: (profile: Profile) => send<Profile>("PUT", `/profiles/${profile.id}`, profile),
  deleteProfile: (id: number) => send<void>("DELETE", `/profiles/${id}`),

  media: () => request<MediaSettings>("/settings/media"),
  saveMedia: (media: MediaSettings) => send<MediaSettings>("PUT", "/settings/media", media),
  paths: () => request<Paths>("/settings/paths"),
  savePaths: (paths: Paths) => send<Paths>("PUT", "/settings/paths", paths),
  folders: (path: string | null) => request<Folder>(`/filesystem${path ? `?path=${encodeURIComponent(path)}` : ""}`),
};

const units = ["B", "KB", "MB", "GB", "TB"];

export function formatSize(bytes?: number | null): string {
  if (bytes == null) return "—";
  const exponent = Math.min(Math.floor(Math.log(Math.max(bytes, 1)) / Math.log(1024)), units.length - 1);
  return `${(bytes / 1024 ** exponent).toFixed(exponent > 1 ? 1 : 0)} ${units[exponent]}`;
}

export function formatEta(seconds?: number | null): string {
  if (seconds == null) return "—";
  if (seconds < 60) return `${Math.round(seconds)}s`;
  if (seconds < 3600) return `${Math.round(seconds / 60)}m`;
  return `${Math.floor(seconds / 3600)}h ${Math.round((seconds % 3600) / 60)}m`;
}

export function formatRuntime(minutes: number): string | null {
  if (!minutes) return null;
  return minutes < 60 ? `${minutes}m` : `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
}

const relative = new Intl.RelativeTimeFormat("en", { numeric: "auto" });

export function formatAgo(iso: string): string {
  const minutes = Math.round((new Date(iso).getTime() - Date.now()) / 60000);
  if (Math.abs(minutes) < 60) return relative.format(minutes, "minute");
  if (Math.abs(minutes) < 1440) return relative.format(Math.round(minutes / 60), "hour");
  return relative.format(Math.round(minutes / 1440), "day");
}

export const errorMessage = (e: unknown) => (e instanceof Error ? e.message : String(e));
