# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base

# Puppeteer environment setup
ENV PUPPETEER_SKIP_CHROMIUM_DOWNLOAD=true
ENV PUPPETEER_EXECUTABLE_PATH=/usr/bin/chromium
ENV XDG_CONFIG_HOME=/tmp/.chromium
ENV XDG_CACHE_HOME=/tmp/.chromium

# Install Chromium and required libraries
RUN apt-get update \
  && apt-get install -y \
    libx11-6 libx11-xcb1 libatk1.0-0 libgtk-3-0 libcups2 libdrm2 \
    libxkbcommon0 libxcomposite1 libxdamage1 libxrandr2 libgbm1 \
    libpango-1.0-0 libcairo2 libasound2 libxshmfence1 libnss3 chromium \
  && apt-get autopurge -y \
  && apt-get autoclean -y

USER $APP_UID
WORKDIR /app


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["NuGet.config", "."]
COPY ["KamiYomu.CrawlerAgents.ConsoleApp/KamiYomu.CrawlerAgents.ConsoleApp.csproj", "KamiYomu.CrawlerAgents.ConsoleApp/"]
COPY ["KamiYomu.CrawlerAgents.MangaKatana/KamiYomu.CrawlerAgents.MangaKatana.csproj", "KamiYomu.CrawlerAgents.MangaKatana/"]
RUN dotnet restore "./KamiYomu.CrawlerAgents.ConsoleApp/KamiYomu.CrawlerAgents.ConsoleApp.csproj"
COPY . .
WORKDIR "/src/KamiYomu.CrawlerAgents.ConsoleApp"
RUN dotnet build "./KamiYomu.CrawlerAgents.ConsoleApp.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./KamiYomu.CrawlerAgents.ConsoleApp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final

WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "KamiYomu.CrawlerAgents.ConsoleApp.dll"]