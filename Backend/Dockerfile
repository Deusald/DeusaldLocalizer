# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the projects the backend needs (App/MAUI is not built here).
COPY Common/Common.csproj Common/
COPY Backend/Backend.csproj Backend/
RUN dotnet restore Backend/Backend.csproj

COPY Common/ Common/
COPY Backend/ Backend/
RUN dotnet publish Backend/Backend.csproj -c Release -o /app --no-restore

# ── Runtime stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The bot shells out to git.
RUN apt-get update \
    && apt-get install -y --no-install-recommends git ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# Where per-project clones live (mount a volume here to persist them).
ENV Bot__ReposRoot=/data/repos
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Backend.dll"]
