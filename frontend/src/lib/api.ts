// Typed client for the ASP.NET API (same origin in production, rewritten to the container in dev).

export type Kind = "movie" | "series" | "anime";

// tmdbApiKey is the key saved in settings; effective is the key in use (it may come from MODULARR_TMDB_API_KEY), masked.
export interface MetadataSettings {
  tmdbApiKey: string | null;
  source: "Custom" | "Environment" | "None";
  effective: string | null;
}
export type Role = "Admin" | "User";
export type Category = "Movie" | "Series" | "Anime";

export interface User {
  id: number;
  username: string;
  role: Role;
  created: string;
  // Versioned picture URL, null without one.
  avatar: string | null;
  // The server the account comes from ("linkarr"), null for a local account.
  external: string | null;
}

// The hub (linkarr) that manages the accounts and links to the other apps, when one is set.
export interface Managed {
  name: string;
  url: string;
}

export interface AuthState {
  user: User | null;
  managed: Managed | null;
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
  // Its TMDB collection (saga), null when none.
  collectionTmdbId: number | null;
  collectionName: string | null;
}

// A collection id is signed: a TMDB collection, or a TMDB keyword franchise negated (-180547 is the MCU).
export interface CollectionCard {
  id: number;
  name: string;
  poster: string | null;
  backdrop: string | null;
  followed: boolean;
  monitored: boolean;
  qualityProfileId: number | null;
  // Null when the source could not be read.
  parts: number | null;
  inLibrary: number;
}

export interface CollectionPart {
  tmdbId: number;
  title: string;
  year: number;
  release: string | null;
  poster: string | null;
  released: boolean;
  movieId: number | null;
  hasFile: boolean;
}

export interface CollectionDetail extends Omit<CollectionCard, "parts" | "inLibrary"> {
  overview: string | null;
  parts: CollectionPart[];
}

export interface CollectionSummary {
  id: number;
  name: string;
  poster: string | null;
  backdrop: string | null;
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
  // The title's own wanted subtitle languages; null follows the category default.
  subtitles?: string[] | null;
  searchNow?: boolean;
  monitor?: SeriesMonitor;
  // Read with monitor "Custom" only; picked episodes are the only monitored ones (their seasons are monitored too).
  seasons?: number[];
  episodes?: { season: number; episode: number }[];
}

export interface LookupSeason {
  number: number;
  episodes: { number: number; title: string | null; airDate: string | null }[];
}

export type SeriesMonitor = "All" | "Latest" | "Future" | "First" | "Custom" | "None";
export type ImportMode = "Move" | "Hardlink";

export interface ImportSettings {
  importMode: ImportMode;
  renameFiles: boolean;
  recycleBinPath: string;
}

// Hours between runs, 0 turns one off.
export interface AutomationSettings {
  refreshIntervalHours: number;
  searchIntervalHours: number;
  searchTitlesPerCycle: number;
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

// A release never grabbed again, with why it was refused.
export interface BlocklistItem {
  id: number;
  seriesId: number | null;
  movieId: number | null;
  title: string;
  quality: string;
  indexer: string | null;
  reason: string | null;
  date: string;
}

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
  hasMetadata: boolean | null;
  seeds: number | null;
  peers: number | null;
}

// The public address torrent traffic leaves from (a VPN's exit when there is one), checked every 5 minutes.
export interface DownloadNetwork {
  publicIp: string | null;
  org: string | null;
  country: string | null;
  checkedAt: string | null;
  error: string | null;
  listenPort: number;
  openConnections: number;
}

export interface Indexer {
  id: string;
  name: string;
  language: string;
  privacy: string;
  enabled: boolean;
  // Disabled automatically because its last test failed; re-enabled when a test passes.
  failing: boolean;
  // Its definition reads credentials (login, cookie, API key); configured once they are filled in.
  needsLogin: boolean;
  configured: boolean;
  health: { activeUrl: string; failures: number; lastError: string | null; lastCheck: string | null };
  description: string;
  categories: string[];
  mirrors: string[];
}

export interface IndexerTest {
  releases: number;
  error: string | null;
  health: Indexer["health"];
}

// One field of an indexer's login form, from its definition's settings; a secret's value is never sent, only whether it is set.
export interface IndexerSettingField {
  name: string;
  type: "text" | "password" | "checkbox" | "select" | "info";
  label: string;
  default: string | null;
  options: { value: string; label: string }[] | null;
  secret: boolean;
  required: boolean;
  value: string | null;
  set: boolean;
}

export interface IndexerSettings {
  id: string;
  needsLogin: boolean;
  configured: boolean;
  captcha: boolean;
  fields: IndexerSettingField[];
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

export type NotificationType = "Webhook" | "Discord" | "Plex" | "Jellyfin" | "Emby";

export interface NotificationConnection {
  name: string;
  type: NotificationType;
  url: string;
  token: string | null;
  onGrab: boolean;
  onImport: boolean;
  onFailure: boolean;
}

// Rates in bytes per second (0 is unlimited); durations as .NET TimeSpan strings ("d.hh:mm:ss").
// The other TorrentEngineOptions keys come back too and are sent back unchanged.
export interface TorrentSettings {
  listenPort: number;
  maxConnections: number;
  maxDownloadRate: number;
  maxUploadRate: number;
  dhtEnabled: boolean;
  localPeerDiscoveryEnabled: boolean;
  seedRatioLimit: number | null;
  seedTimeLimit: string | null;
  stallRetries: number;
  maxAutomaticRegrabs: number;
}

// A movie's file or an episode's file.
export type SubtitleTarget ={ movieId: number } | { episodeId: number };

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

// A library title on the Subtitles page: covered counts the files having the language or needing none (audio in it).
export interface TitleSubtitles {
  id: number;
  kind: Category;
  title: string;
  year: number;
  poster: string | null;
  wanted: string[];
  ownLanguages: boolean;
  files: number;
  coverage: { language: string; code: string | null; covered: number }[];
}

export interface LanguageState {
  language: string;
  state: "Present" | "Missing" | "Audio";
  hearingImpaired: boolean;
  forced: boolean;
}

export interface SeasonSubtitles {
  number: number;
  episodes: { id: number; episodeNumber: number; title: string | null; languages: LanguageState[] }[];
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

// One event per episode air day and per movie release day (cinema, digital, physical).
export type CalendarEvent = { date: string; title: string; poster: string | null; hasFile: boolean; monitored: boolean } & (
  | { type: "episode"; seriesId: number; season: number; episode: number; episodeTitle: string | null; kind: "series" | "anime" }
  | { type: "movie"; movieId: number; release: "cinema" | "digital" | "physical" }
);

// Storage sizes cover the whole disks holding the library and downloads; libraryBytes is the imported files only.
export interface Dashboard {
  counts: { movies: number; series: number; anime: number; episodes: number; episodesWithFile: number; missingMovies: number; missingEpisodes: number };
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

// A daily (or admin-made) zip of the database and settings, or the database copy taken before a migration.
export interface Backup {
  name: string;
  size: number;
  created: string;
}

export const backupHref = (name: string) => `/api/backups/${encodeURIComponent(name)}`;

// Fired on any 401 so the auth gate drops back to the sign-in screen.
export const UNAUTHORIZED = "modularr:unauthorized";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api${path}`, {
    ...init,
    // A Blob body (a picture) carries its own type.
    headers: typeof init?.body === "string" ? { "Content-Type": "application/json" } : undefined,
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
  login: (username: string, password: string) => send<User>("POST", "/auth/login", { username, password }),
  logout: () => send<void>("POST", "/auth/logout"),

  uploadAvatar: (image: Blob) => request<User>("/auth/avatar", { method: "PUT", body: image }),
  removeAvatar: () => send<User>("DELETE", "/auth/avatar"),
  endOtherSessions: () => send<void>("POST", "/auth/sessions/end-others"),

  lookup: (term: string, signal?: AbortSignal) =>
    request<DiscoveryItem[]>(`/lookup?type=all&term=${encodeURIComponent(term)}`, { signal }),
  lookupSeasons: (tvdbId: number) => request<LookupSeason[]>(`/lookup/seasons?tvdbId=${tvdbId}`),
  discover: (kind: Kind, list: string) => request<DiscoverList>(`/discover/${kind === "movie" ? "movies" : kind}?list=${list}`),

  explore: (kind: Kind, filter: ExploreQuery) => request<ExplorePage>(`/explore/${kind === "movie" ? "movies" : kind}?${query(filter)}`),
  exploreGenres: (kind: Kind) => request<string[]>(`/explore/${kind === "movie" ? "movies" : kind}/genres`),
  dashboard: () => request<Dashboard>("/dashboard"),
  // At most 62 days, both ends included.
  calendar: (start: string, end: string) => request<CalendarEvent[]>(`/calendar?${query({ start, end })}`),
  // The next 10 events within 6 months.
  upcoming: () => request<CalendarEvent[]>("/calendar/upcoming"),

  movies: () => request<Movie[]>("/movies"),
  movie: (id: number) => request<MovieDetail>(`/movies/${id}`),
  // The followed sagas, those of library movies and the curated popular ones.
  collections: () => request<CollectionCard[]>("/collections?suggested=true"),
  collection: (id: number) => request<CollectionDetail>(`/collections/${id}`),
  lookupCollections: (term: string, signal?: AbortSignal) =>
    request<CollectionSummary[]>(`/collections/lookup?term=${encodeURIComponent(term)}`, { signal }),
  followCollection: (id: number, options: { monitored?: boolean; addMissing?: boolean; qualityProfileId?: number } = {}) =>
    send<CollectionDetail>("POST", "/collections", { ...(id > 0 ? { tmdbId: id } : { keywordId: -id }), ...options }),
  updateCollection: (id: number, patch: { monitored?: boolean; qualityProfileId?: number }) => send<CollectionDetail>("PATCH", `/collections/${id}`, patch),
  unfollowCollection: (id: number) => send<void>("DELETE", `/collections/${id}`),
  addMissing: (id: number) => send<{ added: number }>("POST", `/collections/${id}/add-missing`),
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
  releases: (item: LibraryItem, season?: number, episode?: number) =>
    request<SearchResult>(isSeries(item)
      ? `/search?type=series&tvdbId=${item.tvdbId}${season === undefined ? "" : `&season=${season}`}${episode === undefined ? "" : `&episode=${episode}`}`
      : `/search?type=movie&tmdbId=${item.tmdbId}`),
  grab: (item: LibraryItem, release: Release) =>
    send<{ id: number }>("POST", "/releases/grab", isSeries(item) ? { release, seriesId: item.id } : { release, movieId: item.id }),

  downloads: () => request<Download[]>("/downloads"),
  network: () => request<DownloadNetwork>("/downloads/network"),
  refreshNetwork: () => send<DownloadNetwork>("POST", "/downloads/network/refresh"),
  pause: (id: number) => send<void>("POST", `/downloads/${id}/pause`),
  resume: (id: number) => send<void>("POST", `/downloads/${id}/resume`),
  retryImport: (id: number) => send<void>("POST", `/downloads/${id}/retry`),
  removeDownload: (id: number, deleteData: boolean, blocklist: boolean) =>
    send<void>("DELETE", `/downloads/${id}?deleteData=${deleteData}&blocklist=${blocklist}`),
  blocklist: () => request<BlocklistItem[]>("/blocklist"),
  unblock: (id: number) => send<void>("DELETE", `/blocklist/${id}`),

  activity: (filter: ActivityQuery = {}) =>
    request<Activity[]>(`/activity?${query(filter)}`),
  unreadCount: () => request<{ count: number }>("/activity/unread-count"),
  markRead: (upTo?: number) => send<void>("POST", `/activity/read${upTo === undefined ? "" : `?upTo=${upTo}`}`),

  indexers: () => request<IndexerList>("/indexers"),
  testIndexer: (id: string) => send<IndexerTest>("POST", `/indexers/${encodeURIComponent(id)}/test`),
  indexerSettings: (id: string) => request<IndexerSettings>(`/indexers/${encodeURIComponent(id)}/settings`),
  // An empty secret keeps the stored one.
  saveIndexerSettings: (id: string, values: Record<string, string>) =>
    send<IndexerSettings>("PUT", `/indexers/${encodeURIComponent(id)}/settings`, values),

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
  subtitleOverview: () => request<TitleSubtitles[]>("/subtitles/overview"),
  seriesSubtitles: (id: number) => request<{ seasons: SeasonSubtitles[] }>(`/subtitles/series/${id}`),
  movieSubtitles: (id: number) => request<{ languages: LanguageState[] | null }>(`/subtitles/movies/${id}`),
  searchMissingSubtitles: (kind: Kind, id: number) => send<void>("POST", `/subtitles${libraryPath(kind)}/${id}/search`),
  paths: () => request<Paths>("/settings/paths"),
  savePaths: (paths: Paths) => send<Paths>("PUT", "/settings/paths", paths),
  importSettings: () => request<ImportSettings>("/settings/import"),
  saveImportSettings: (settings: ImportSettings) => send<ImportSettings>("PUT", "/settings/import", settings),
  automation: () => request<AutomationSettings>("/settings/automation"),
  saveAutomation: (settings: AutomationSettings) => send<AutomationSettings>("PUT", "/settings/automation", settings),
  metadata: () => request<MetadataSettings>("/settings/metadata"),
  saveMetadata: (tmdbApiKey: string | null) => send<MetadataSettings>("PUT", "/settings/metadata", { tmdbApiKey }),
  testMetadata: () => send<{ ok: boolean; error: string | null }>("POST", "/settings/metadata/test"),
  apiKey: () => request<{ apiKey: string | null; fromEnvironment: boolean }>("/settings/apikey"),
  regenerateApiKey: () => send<{ apiKey: string }>("POST", "/settings/apikey"),
  notifications: () => request<NotificationConnection[]>("/settings/notifications"),
  saveNotifications: (connections: NotificationConnection[]) => send<NotificationConnection[]>("PUT", "/settings/notifications", connections),
  testNotification: (connection: NotificationConnection) => send<{ ok: boolean; error: string | null }>("POST", "/settings/notifications/test", connection),
  torrentSettings: () => request<TorrentSettings>("/settings/torrents"),
  saveTorrentSettings: (settings: TorrentSettings) => send<TorrentSettings>("PUT", "/settings/torrents", settings),
  folders: (path: string | null) => request<Folder>(`/filesystem${path ? `?path=${encodeURIComponent(path)}` : ""}`),
  backups: () => request<Backup[]>("/backups"),
  createBackup: () => send<Backup>("POST", "/backups"),
  deleteBackup: (name: string) => send<void>("DELETE", `/backups/${encodeURIComponent(name)}`),
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

export { ago as formatAgo } from "@/lib/format";

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
