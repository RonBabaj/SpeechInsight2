# SpeechInsight2 alternate image: Api + Client with Caddy in-process.
# Prefer the repo-root Dockerfile + docker-compose.yml for VPS production.
# Build from repo root: docker build -f deploy/speechinsight2/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY ["Api/SpeechInsight.Api.csproj", "Api/"]
RUN dotnet restore "Api/SpeechInsight.Api.csproj"
COPY Api/ Api/
RUN dotnet publish "Api/SpeechInsight.Api.csproj" -c Release -o /app/api --no-restore

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS client-build
WORKDIR /src
COPY ["Client/SpeechInsight.Client.csproj", "Client/"]
RUN dotnet restore "Client/SpeechInsight.Client.csproj"
COPY Client/ Client/
RUN dotnet publish "Client/SpeechInsight.Client.csproj" -c Release -o /app/client --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl \
  && curl -fsSL -o /usr/local/bin/caddy "https://github.com/caddyserver/caddy/releases/download/v2.7.6/caddy_2.7.6_linux_amd64" \
  && chmod +x /usr/local/bin/caddy \
  && rm -rf /var/lib/apt/lists/*

COPY --from=api-build /app/api /app/api
COPY --from=client-build /app/client /app/client

COPY deploy/speechinsight2/Caddyfile /etc/Caddyfile
COPY deploy/speechinsight2/start.sh /app/start.sh
RUN chmod +x /app/start.sh

ENV PORT=8080
EXPOSE 8080
ENTRYPOINT ["/app/start.sh"]
