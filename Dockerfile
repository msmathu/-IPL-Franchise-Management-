# One image: the Angular app is built and served by the ASP.NET Core API (same origin, strict CSP).

# ---- 1. Web app ----
FROM node:22-alpine AS web
WORKDIR /web
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npx ng build --configuration production

# ---- 2. API ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY backend/nuget.config backend/IplFranchise.sln ./
COPY backend/src/Ipl.Domain/Ipl.Domain.csproj src/Ipl.Domain/
COPY backend/src/Ipl.Application/Ipl.Application.csproj src/Ipl.Application/
COPY backend/src/Ipl.Infrastructure/Ipl.Infrastructure.csproj src/Ipl.Infrastructure/
COPY backend/src/Ipl.Api/Ipl.Api.csproj src/Ipl.Api/
RUN dotnet restore src/Ipl.Api/Ipl.Api.csproj
COPY backend/src/ src/
RUN dotnet publish src/Ipl.Api/Ipl.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# ---- 3. Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# QuestPDF (PDF exports) needs fontconfig on Linux.
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist/web/browser ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0
EXPOSE 8080
USER app
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s CMD wget -qO- http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Ipl.Api.dll"]
