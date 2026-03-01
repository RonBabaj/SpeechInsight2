# SpeechInsight: Blazor WASM client + ASP.NET Core API in a single container for Render.
# The API serves the client from / and exposes /api/*. Set OPENAI_API_KEY and PORT (Render sets PORT).

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
WORKDIR /app

COPY --from=publish-api /out/api .
COPY --from=publish-client /out/client ./wwwroot

# Render sets PORT at runtime. Program.cs reads PORT and binds to 0.0.0.0:PORT.
EXPOSE 8080

ENTRYPOINT ["dotnet", "SpeechInsight.Api.dll"]
