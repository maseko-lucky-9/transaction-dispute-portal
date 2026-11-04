# syntax=docker/dockerfile:1
# Multi-target: `docker build --target web .` (nginx + SPA) or `docker build .` (the API, the last stage / default).
# The API image also runs the migration job: `dispute-portal-api --migrate`.

# Portable (framework-dependent) output: the SDK stage runs natively on the build host; the chiseled runtime stage
# is pulled per target platform, so the same Dockerfile builds amd64 and arm64 images.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore on its own layer so a source-only change reuses the cached packages.
COPY global.json Directory.Build.props .editorconfig ./
COPY src/DisputePortal.Domain/DisputePortal.Domain.csproj src/DisputePortal.Domain/packages.lock.json src/DisputePortal.Domain/
COPY src/DisputePortal.Api/DisputePortal.Api.csproj src/DisputePortal.Api/packages.lock.json src/DisputePortal.Api/
RUN dotnet restore src/DisputePortal.Api/DisputePortal.Api.csproj --locked-mode
COPY src/ src/
RUN dotnet publish src/DisputePortal.Api/DisputePortal.Api.csproj -c Release \
    --no-restore -p:UseAppHost=false -o /app/publish

FROM node:22-alpine AS web-build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --ignore-scripts
COPY web/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.28-alpine AS web
COPY web/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY web/nginx/security-headers.conf web/nginx/csp-app.conf /etc/nginx/
COPY --from=web-build /web/dist /usr/share/nginx/html
EXPOSE 8080 8180
USER nginx

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS api
WORKDIR /app
COPY --from=build /app/publish .
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "DisputePortal.Api.dll"]
