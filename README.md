# modularr

Series, movies, anime and subtitles in one app: one config, one image.

## Layout

- `backend/` – ASP.NET (.NET 10) API, `Modularr.Core` domain, tests (`dotnet test`)
- `frontend/` – Next.js (TypeScript), statically exported and served by the API
- `Dockerfile` – single image, port 8080

## Run

```sh
docker build -t modularr:dev .
docker run -d --name modularr --restart unless-stopped --stop-timeout 60 \
  -p 8080:8080 -p 6881:6881/tcp -p 6881:6881/udp \
  -v modularr-config:/config \
  -v "$PWD/media:/media" \
  --env-file .env \
  modularr:dev
```

`--stop-timeout 60` gives modularr time to stop the torrents and save their progress; Docker's default 10 s kills it mid-save,
and the next start re-checks every partial download.

`.env` (git- and docker-ignored) holds secrets such as `MODULARR_TMDB_API_KEY`, the default TMDB key for Explore; a key saved
in Settings → General overrides it.

`/config` holds the database, keys and `modularr.json`. `media/` (git-ignored) holds `downloads`, `movies`, `series` and `anime`;
`modularr.json` points `Paths` at `/media/...`. One mount for all four keeps imports on the same filesystem.

The container runs as the non-root user `app` (uid 1654): `media/` must be writable by it. A `modularr-config` volume created
by an older root image needs `docker run --rm -u root -v modularr-config:/config --entrypoint chown modularr:dev -R 1654:1654 /config` once.

HTTPS: put modularr behind a reverse proxy that terminates TLS, or give Kestrel a certificate directly with
`-e ASPNETCORE_HTTPS_PORTS=8443 -e ASPNETCORE_Kestrel__Certificates__Default__Path=/config/cert.pfx -e ASPNETCORE_Kestrel__Certificates__Default__Password=...`
and `-p 8443:8443`. Over HTTPS the session cookie is `Secure` and HSTS is sent.

Accounts: modularr has no first-run setup and starts with no account. linkarr creates every account through
`/api/provision/*` with the API key; until it does, nobody can sign in and modularr is unusable, by design. Set the key with
`-e MODULARR_API_KEY=<at least 32 characters among letters, digits, _ and ->` (the same value given to linkarr): it overrides
the key in `modularr.json`, cannot be regenerated in Settings, and an invalid value stops modularr at startup.

Behind a reverse proxy, set `-e TrustedProxies=<proxy address or CIDR>` (`;`-separated) so modularr reads `X-Forwarded-For`,
`-Proto` and `-Host` from that proxy only: without it the session cookie is not `Secure`, HSTS is not sent, and every client
shares the proxy's address in the sign-in rate limit. The proxy must forward the original `Host` (nginx: `proxy_set_header Host $host`).

## Origin

modularr ports and merges code from [Sonarr](https://github.com/Sonarr/Sonarr) (`b3d4bb8`)
and [Radarr](https://github.com/Radarr/Radarr) (`56f6168`), both GPL-3.0. modularr is therefore GPL-3.0 as well, see [LICENSE](LICENSE).
