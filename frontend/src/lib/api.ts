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
  language?: string | null;
  poster: string | null;
  backdrop: string | null;
  rating: number | null;
  libraryId: number | null;
  inLibrary: boolean;
  hasFile?: boolean;
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
  // The item's own overrides; null follows the version rules and the category's subtitles.
  version: string | null;
  subtitles: string[] | null;
  wantedVersion: string;
  wantedSubtitles: string[];
  // What the version rules alone would pick, ignoring the item's own version.
  policyVersion: string;
  lastSearch: { time: string; result: string } | null;
}

export interface Movie extends ItemBase {
  tmdbId: number;
  imdbId: string | null;
  hasFile: boolean;
}

export interface Season {
  number: number;
  monitored: boolean;
  episodes: number;
  episodesWithFile: number;
  // Monitored episodes already aired without a file.
  missing: number;
  hasFile: boolean;
}

export interface Series extends ItemBase {
  tvdbId: number;
  seriesType: "Standard" | "Daily" | "Anime";
  added: string;
  seasons: Season[];
  episodes: number;
  episodesWithFile: number;
  missing: number;
  // Every monitored episode has a file.
  hasFile: boolean;
}

export type LibraryItem = Movie | Series;

export interface Episode {
  id: number;
  seriesId: number;
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
  seasons: Season[];
  episodes: Episode[];
}

export interface MovieDetail {
  movie: Movie;
  hasFile: boolean;
  file: MovieFile | null;
}

// version: the most specific version the file's languages satisfy, null when they name none.
export interface MovieFile {
  quality: string;
  version: string | null;
}

export interface ItemPatch {
  qualityProfileId?: number;
  monitored?: boolean;
  version?: string | null;
  subtitles?: string[] | null;
}

export interface AddOptions {
  qualityProfileId?: number | null;
  version?: string | null;
  searchNow?: boolean;
  monitor?: SeriesMonitor;
}

export type SeriesMonitor = "All" | "Latest" | "Future" | "First" | "None";
export type ImportMode = "Move" | "Hardlink";

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
  subtitles: string[];
  // The most specific version the release satisfies (MULTi, VF, VFQ, VOSTFR, VO or a language), null when none.
  version: string | null;
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
  version: string | null;
  // Most wanted first.
  subtitles: string[];
}

export interface VersionRule {
  category: Category | null;
  originalLanguage: string | null;
  version: string;
}

export interface MediaSettings {
  movies: MediaDefaults;
  series: MediaDefaults;
  anime: MediaDefaults;
  defaultVersion: string;
  exceptions: VersionRule[];
}

export interface SubtitleSettings {
  onlyWhenAudioDiffers: boolean;
  preferHearingImpaired: boolean;
  // Secrets come back null for non-admins.
  openSubtitles: { apiKey: string | null; username: string | null; password: string | null };
}

// A movie's file or an episode's file.
export type SubtitleTarget = { movieId: number } | { episodeId: number };

export interface SubtitleFile {
  language: string;
  hearingImpaired: boolean;
  forced: boolean;
  file: string;
}

export interface SubtitleStatus {
  present: SubtitleFile[];
  missing: string[];
}

export interface SubtitleCandidate {
  provider: string;
  id: string;
  language: string;
  release: string;
  hearingImpaired: boolean;
  forced: boolean;
  hashMatch: boolean;
  downloads: number;
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

// Items is 0 when the title's folder was skipped (missing, linked or outside the library roots).
export interface ScanResult {
  items: number;
  added: number;
  removed: number;
}

export type LibraryRoot = "movies" | "series" | "anime";
export type MatchConfidence = "None" | "Low" | "Medium" | "High";

// A library folder no title owns yet, with the best metadata match for its name.
export interface ImportProposal {
  folder: string;
  parsed: { title: string; year: number | null; idSource: string | null; id: string | null; lookupTerm: string };
  match: DiscoveryItem | null;
  confidence: MatchConfidence;
}

// Total counts every unowned folder; items is the first page of them.
export interface ImportProposals {
  total: number;
  items: ImportProposal[];
}

export interface ImportItem {
  folder: string;
  tmdbId?: number;
  tvdbId?: number;
}

export interface ImportOutcome {
  folder: string;
  seriesId: number | null;
  movieId: number | null;
  files: number;
  error: string | null;
}

export type ActivityKind =
  | "Added" | "SearchStarted" | "SearchCompleted" | "NothingFound" | "SearchFailed" | "Grabbed" | "DownloadStarted"
  | "DownloadStalled" | "DownloadFailed" | "Blocklisted" | "Regrabbed" | "Imported" | "ImportFailed" | "Removed"
  | "SubtitleDownloaded" | "SubtitlesMissing" | "ScanCompleted";

export interface Activity {
  id: number;
  time: string;
  kind: ActivityKind;
  level: "Info" | "Success" | "Warning" | "Error";
  // The item or release name; message says what happened.
  title: string;
  message: string | null;
  seriesId: number | null;
  movieId: number | null;
  downloadId: number | null;
  read: boolean;
}

// One entry of the "progress" server event, sent every 2 s while downloads run.
export interface DownloadProgress {
  id: number;
  progress: number;
  rate: number;
  eta: number | null;
}

export interface AiringEpisode {
  id: number;
  seriesId: number;
  seriesTitle: string;
  poster: string | null;
  seasonNumber: number;
  episodeNumber: number;
  title: string | null;
  airDate: string;
  hasFile: boolean;
  monitored: boolean;
}

// Storage sizes cover the whole disks holding the library and downloads; libraryBytes is the imported files only.
export interface Dashboard {
  counts: { movies: number; series: number; anime: number; episodes: number; episodesWithFile: number; missing: number };
  storage: { usedBytes: number; freeBytes: number; totalBytes: number; libraryBytes: number };
  subtitles: { covered: number; total: number };
  inProgress: Download[];
  // Monday to Sunday of the current week.
  airing: AiringEpisode[];
  recent: Activity[];
}

export type ExploreSort = "popularity" | "rating" | "date" | "title";

export interface ExploreQuery {
  page?: number;
  genre?: string;
  yearFrom?: number;
  yearTo?: number;
  status?: "released" | "upcoming" | "continuing" | "ended";
  sort?: ExploreSort;
}

// Loading: the series index is still filling in the background; limited: movies without a TMDB key.
export interface ExplorePage {
  items: DiscoveryItem[];
  page: number;
  hasMore: boolean;
  limited: boolean;
  loading: boolean;
  error: string | null;
}

export interface ActivityQuery {
  before?: number;
  limit?: number;
  seriesId?: number;
  movieId?: number;
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

const query = (params: object) =>
  new URLSearchParams(Object.entries(params).filter(([, v]) => v !== undefined && v !== "").map(([k, v]) => [k, String(v)])).toString();

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

  explore: (kind: Kind, filter: ExploreQuery) => request<ExplorePage>(`/explore/${kind === "movie" ? "movies" : kind}?${query(filter)}`),
  exploreGenres: (kind: Kind) => request<string[]>(`/explore/${kind === "movie" ? "movies" : kind}/genres`),
  dashboard: () => request<Dashboard>("/dashboard"),

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
  setSeasonMonitored: (seriesId: number, season: number, monitored: boolean) =>
    send<SeriesDetail>("PATCH", `/series/${seriesId}/seasons/${season}`, { monitored }),
  setEpisodeMonitored: (id: number, monitored: boolean) => send<Episode>("PATCH", `/episodes/${id}`, { monitored }),
  autoSearch: (kind: Kind, id: number, season?: number) =>
    send<void>("POST", `${libraryPath(kind)}/${id}/search${season === undefined ? "" : `?season=${season}`}`),
  rescan: (kind: Kind, id: number) => send<ScanResult>("POST", `${libraryPath(kind)}/${id}/rescan`),
  rescanLibrary: () => send<ScanResult>("POST", "/library/rescan"),
  importProposals: (root: LibraryRoot) => request<ImportProposals>(`/library/import?root=${root}`),
  importFolders: (items: ImportItem[]) => send<ImportOutcome[]>("POST", "/library/import", { items }),
  releases: (item: LibraryItem, season?: number) =>
    request<SearchResult>(isSeries(item)
      ? `/search?type=series&tvdbId=${item.tvdbId}${season === undefined ? "" : `&season=${season}`}`
      : `/search?type=movie&tmdbId=${item.tmdbId}`),
  grab: (item: LibraryItem, release: Release) =>
    send<{ id: number }>("POST", "/releases/grab", isSeries(item) ? { release, seriesId: item.id } : { release, movieId: item.id }),

  downloads: () => request<Download[]>("/downloads"),
  pause: (id: number) => send<void>("POST", `/downloads/${id}/pause`),
  resume: (id: number) => send<void>("POST", `/downloads/${id}/resume`),
  retryImport: (id: number) => send<void>("POST", `/downloads/${id}/retry`),
  removeDownload: (id: number, deleteData: boolean, blocklist: boolean) =>
    send<void>("DELETE", `/downloads/${id}?deleteData=${deleteData}&blocklist=${blocklist}`),

  activity: (filter: ActivityQuery = {}) =>
    request<Activity[]>(`/activity?${query(filter)}`),
  unreadCount: () => request<{ count: number }>("/activity/unread-count"),
  markRead: (upTo?: number) => send<void>("POST", `/activity/read${upTo === undefined ? "" : `?upTo=${upTo}`}`),

  indexers: () => request<IndexerList>("/indexers"),

  qualities: () => request<Quality[]>("/qualities"),
  languages: () => request<Language[]>("/languages"),
  versions: () => request<string[]>("/versions"),
  profiles: () => request<Profile[]>("/profiles"),
  createProfile: (profile: Profile) => send<Profile>("POST", "/profiles", profile),
  saveProfile: (profile: Profile) => send<Profile>("PUT", `/profiles/${profile.id}`, profile),
  deleteProfile: (id: number) => send<void>("DELETE", `/profiles/${id}`),

  media: () => request<MediaSettings>("/settings/media"),
  saveMedia: (media: MediaSettings) => send<MediaSettings>("PUT", "/settings/media", media),
  subtitleSettings: () => request<SubtitleSettings>("/settings/subtitles"),
  saveSubtitleSettings: (settings: SubtitleSettings) => send<SubtitleSettings>("PUT", "/settings/subtitles", settings),
  subtitles: (target: SubtitleTarget) => request<SubtitleStatus>(`/subtitles?${query(target)}`),
  searchSubtitles: (target: SubtitleTarget, language?: string) => send<SubtitleCandidate[]>("POST", "/subtitles/search", { ...target, language }),
  downloadSubtitle: (target: SubtitleTarget, candidate: SubtitleCandidate) => send<SubtitleFile>("POST", "/subtitles/download", { ...target, ...candidate }),
  paths: () => request<Paths>("/settings/paths"),
  savePaths: (paths: Paths) => send<Paths>("PUT", "/settings/paths", paths),
  importMode: () => request<{ importMode: ImportMode }>("/settings/import"),
  saveImportMode: (importMode: ImportMode) => send<{ importMode: ImportMode }>("PUT", "/settings/import", { importMode }),
  metadata: () => request<{ tmdbApiKey: string | null }>("/settings/metadata"),
  saveMetadata: (tmdbApiKey: string | null) => send<{ tmdbApiKey: string | null }>("PUT", "/settings/metadata", { tmdbApiKey }),
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

const relative = {
  long: new Intl.RelativeTimeFormat("en", { numeric: "auto" }),
  short: new Intl.RelativeTimeFormat("en", { numeric: "auto", style: "narrow" }),
};

export function formatAgo(iso: string, style: keyof typeof relative = "long"): string {
  const format = relative[style];
  const minutes = Math.round((new Date(iso).getTime() - Date.now()) / 60000);
  if (Math.abs(minutes) < 60) return format.format(minutes, "minute");
  if (Math.abs(minutes) < 1440) return format.format(Math.round(minutes / 60), "hour");
  return format.format(Math.round(minutes / 1440), "day");
}

export const errorMessage = (e: unknown) => (e instanceof Error ? e.message : String(e));

const pad = (n: number) => String(n).padStart(2, "0");

// A local calendar day as the API writes air dates: YYYY-MM-DD.
export const isoDay = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

export const episodeCode = (e: { seasonNumber: number; episodeNumber: number }) => `S${pad(e.seasonNumber)}E${pad(e.episodeNumber)}`;

// Monday to Sunday of the current week, the range the dashboard's airing list covers.
export function currentWeek(): Date[] {
  const monday = new Date();
  monday.setHours(0, 0, 0, 0);
  monday.setDate(monday.getDate() - ((monday.getDay() + 6) % 7));
  return Array.from({ length: 7 }, (_, i) => new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + i));
}

export const libraryHref: Record<Kind, string> = { movie: "/movies/", series: "/series/", anime: "/anime/" };
