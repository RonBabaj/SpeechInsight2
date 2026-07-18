# SpeechInsight: Blazor WASM client + ASP.NET Core API in a single container.
# The API serves the client from / and exposes /api/*. Set OPENAI_API_KEY and PORT.
# Pass --build-arg GIT_SHA=<commit> so /api/health can prove which commit is running.

ARG GIT_SHA=unknown

# -----------------------------------------------------------------------------
# Stage 1: Publish Blazor WASM client
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish-client
WORKDIR /src

COPY Client/SpeechInsight.Client.csproj Client/
RUN dotnet restore Client/SpeechInsight.Client.csproj

COPY Client/ Client/
RUN dotnet publish Client/SpeechInsight.Client.csproj -c Release -o /out/client

# -----------------------------------------------------------------------------
# Stage 2: Publish API
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish-api
WORKDIR /src

COPY Api/SpeechInsight.Api.csproj Api/
RUN dotnet restore Api/SpeechInsight.Api.csproj

COPY Api/ Api/
RUN dotnet publish Api/SpeechInsight.Api.csproj -c Release -o /out/api

# -----------------------------------------------------------------------------
# Stage 3: Runtime image — API + client static files
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG GIT_SHA=unknown
WORKDIR /app

# curl is used by Docker Compose healthchecks; ffmpeg converts mic audio → PCM WAV for diarization.
RUN apt-get update \
  && apt-get install -y --no-install-recommends curl ffmpeg \
  && rm -rf /var/lib/apt/lists/*

COPY --from=publish-api /out/api .
COPY --from=publish-client /out/client/wwwroot ./wwwroot

# Baked into the image so deploy verification can compare running app ↔ git SHA.
ENV GIT_SHA=${GIT_SHA}
LABEL org.opencontainers.image.revision=${GIT_SHA}

# Hosts (VPS / NPM) set PORT at runtime. Program.cs binds to 0.0.0.0:PORT.
EXPOSE 8080

ENTRYPOINT ["dotnet", "SpeechInsight.Api.dll"]
