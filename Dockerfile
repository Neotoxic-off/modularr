# Base images are pinned by digest so a rebuild cannot silently pick up a different image; bump the digests to take updates.
FROM node:24-alpine@sha256:06d7adf3e0ef61a13f1d28682726366f97a15aa1888c934d3ecec86c33918b53 AS web
WORKDIR /web
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS api
WORKDIR /src
COPY backend/ ./
RUN dotnet publish src/Modularr.Api -c Release -o /app

# The aspnet image already listens on 8080 (ASPNETCORE_HTTP_PORTS).
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:8af87eba5e7cdafb1514d7388be287365da97c132509e84dfe252ffd552398ca
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/out ./wwwroot
# modularr.json and engine state live here; mount it as a volume. A new volume takes this folder's owner.
ENV MODULARR_CONFIG_DIR=/config
RUN mkdir /config && chown $APP_UID:$APP_UID /config
VOLUME /config
# Not root: a flaw in the app or the torrent engine stays inside this account and the mounted folders.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Modularr.Api.dll"]
