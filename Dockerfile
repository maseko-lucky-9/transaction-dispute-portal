# syntax=docker/dockerfile:1
# Multi-target: `docker build --target web .` (nginx + SPA) or `docker build .` (the API, the last stage / default).
# The API image also runs the migration job: `dispute-portal-api --migrate`.

# Portable (framework-dependent) output: the SDK stage runs natively on the build host; the chiseled runtime stage
# is pulled per target platform, so the same Dockerfile builds amd64 and arm64 images.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /src
# Restore on its own layer so a source-only change reuses the cached packages.
COPY global.json Directory.Build.props .editorconfig ./
COPY backend/src/DisputePortal.Domain/DisputePortal.Domain.csproj backend/src/DisputePortal.Domain/packages.lock.json backend/src/DisputePortal.Domain/
COPY backend/src/DisputePortal.Api/DisputePortal.Api.csproj backend/src/DisputePortal.Api/packages.lock.json backend/src/DisputePortal.Api/
RUN dotnet restore backend/src/DisputePortal.Api/DisputePortal.Api.csproj --locked-mode
COPY backend/src/ backend/src/
RUN dotnet publish backend/src/DisputePortal.Api/DisputePortal.Api.csproj -c Release \
    --no-restore -p:UseAppHost=false -o /app/publish

FROM node:26-alpine@sha256:c6ad9e9c1d80f6eb607c26b4b0a132ff7a2344fad1c78ea3baa2a7d4c8de6d94 AS web-build
WORKDIR /web
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --ignore-scripts
COPY frontend/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.31-alpine@sha256:b5047b9b76845eac124358373c6852377c52e4b05834a47647e955dd91cb8f1e AS web
# Pick up fixed OS packages published after the base image was built (the Trivy gate blocks fixable CRITICALs).
USER root
RUN apk upgrade --no-cache
USER nginx
# The image's entrypoint renders /etc/nginx/templates/*.template into conf.d at start. Only DP_* variables are
# substituted (nginx's own $vars stay). The defaults are the compose values; k8s overrides them (ADR-015).
ENV NGINX_ENVSUBST_FILTER=^DP_ \
    DP_RESOLVER=127.0.0.11 \
    DP_API_UPSTREAM=api:8080 \
    DP_KEYCLOAK_UPSTREAM=keycloak:8080
COPY frontend/nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY frontend/nginx/security-headers.conf frontend/nginx/csp-app.conf frontend/nginx/csp-deny.conf /etc/nginx/
COPY --from=web-build /web/dist /usr/share/nginx/html
EXPOSE 8080 8180

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled@sha256:c89a6a61f494f20a5ae302990e814627a8d91b332fd4a8e7c1707c2d4da9555d AS api
WORKDIR /app
COPY --from=build /app/publish .
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "DisputePortal.Api.dll"]
