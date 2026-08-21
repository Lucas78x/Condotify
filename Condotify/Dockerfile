#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER root
RUN mkdir -p /app/data-protection && chown -R app:app /app
COPY --chmod=755 deploy/http-healthcheck.sh /usr/local/bin/condotify-healthcheck
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Condotify/Condotify.csproj", "Condotify/"]
COPY ["Condotify.Contracts/Condotify.Contracts.csproj", "Condotify.Contracts/"]
COPY ["Condotify.ApiClient/Condotify.ApiClient.csproj", "Condotify.ApiClient/"]
COPY ["Condotify.UI/Condotify.UI.csproj", "Condotify.UI/"]
RUN dotnet restore "./Condotify/Condotify.csproj"
COPY ["Condotify/", "Condotify/"]
COPY ["Condotify.Contracts/", "Condotify.Contracts/"]
COPY ["Condotify.ApiClient/", "Condotify.ApiClient/"]
COPY ["Condotify.UI/", "Condotify.UI/"]
WORKDIR "/src/Condotify"
RUN dotnet publish "./Condotify.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Condotify.dll"]
