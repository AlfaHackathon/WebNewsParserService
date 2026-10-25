FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
# Токен (read:packages) для восстановления AlfaHackaton.* из GitHub Packages
ARG GITHUB_TOKEN
WORKDIR /src
COPY ["Api/Api.csproj", "Api/"]
RUN if [ -n "$GITHUB_TOKEN" ]; then \
        dotnet nuget add source "https://nuget.pkg.github.com/AlfaHackathon/index.json" \
            --name github \
            --username AlfaHackathon \
            --password "$GITHUB_TOKEN" \
            --store-password-in-clear-text; \
    fi
RUN dotnet restore "Api/Api.csproj"
COPY . .
WORKDIR "/src/Api"
RUN dotnet build "./Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
USER root
WORKDIR /app
COPY --from=publish /app/publish .
# Critical: Selenium Manager does not start without execute permission
RUN test -f /app/runtimes/linux-x64/native/selenium-manager && chmod +x /app/runtimes/linux-x64/native/selenium-manager
# Google Chrome для Selenium (headless) — в образе его нет; chromium в Ubuntu это snap-обманка, не годится
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget ca-certificates fonts-liberation \
    && wget -q -O /tmp/chrome.deb https://dl.google.com/linux/direct/google-chrome-stable_current_amd64.deb \
    && apt-get install -y --no-install-recommends /tmp/chrome.deb \
    && rm -f /tmp/chrome.deb /var/lib/apt/lists/* -r \
    && ln -sf /opt/google/chrome/google-chrome /usr/bin/google-chrome
USER $APP_UID
ENTRYPOINT ["dotnet", "Api.dll"]
