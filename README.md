# modularr

Series, movies, anime and subtitles in one app: one config, one image.

## Layout

- `backend/` – ASP.NET (.NET 10) API, `Modularr.Core` domain, tests (`dotnet test`)
- `frontend/` – Next.js (TypeScript), statically exported and served by the API
- `Dockerfile` – single image, port 8080

## Origin

modularr ports and merges code from [Sonarr](https://github.com/Sonarr/Sonarr) (`b3d4bb8`)
and [Radarr](https://github.com/Radarr/Radarr) (`56f6168`), both GPL-3.0. modularr is therefore GPL-3.0 as well, see [LICENSE](LICENSE).
