#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER root
RUN mkdir -p /app/logs/Error /app/data/private-media /app/data/backups \
    && chown -R app:app /app
COPY --chmod=755 deploy/http-healthcheck.sh /usr/local/bin/condotify-healthcheck
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["CondotifyAPI/CondotifyAPI.csproj", "CondotifyAPI/"]
COPY ["Condotify.Contracts/Condotify.Contracts.csproj", "Condotify.Contracts/"]
COPY ["CondotifyAPI.Infrastructure/CondotifyAPI.Infrastructure.csproj", "CondotifyAPI.Infrastructure/"]
COPY ["CondotifyAPI.Domain/CondotifyAPI.Domain.csproj", "CondotifyAPI.Domain/"]
RUN dotnet restore "./CondotifyAPI/CondotifyAPI.csproj"
COPY ["CondotifyAPI/", "CondotifyAPI/"]
COPY ["Condotify.Contracts/", "Condotify.Contracts/"]
COPY ["CondotifyAPI.Infrastructure/", "CondotifyAPI.Infrastructure/"]
COPY ["CondotifyAPI.Domain/", "CondotifyAPI.Domain/"]
WORKDIR "/src/CondotifyAPI"
RUN dotnet publish "./CondotifyAPI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CondotifyAPI.dll"]
