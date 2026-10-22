FROM node:24-alpine AS web
WORKDIR /web
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY backend/ ./
RUN dotnet publish src/Modularr.Api -c Release -o /app

# The aspnet image already listens on 8080 (ASPNETCORE_HTTP_PORTS).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/out ./wwwroot
EXPOSE 8080
ENTRYPOINT ["dotnet", "Modularr.Api.dll"]
