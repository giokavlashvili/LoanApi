#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
# .NET 8+ images listen on 8080/8081 by default and run as a non-root user, so the
# old 80/443 pair would need root to bind.
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Directory.Build.props adds the BannedApiAnalyzers PackageReference to every csproj, so it
# has to be present before restore — otherwise restore resolves a different graph than the
# build does and the restore layer is invalidated on the next COPY.
COPY ["Directory.Build.props", "BannedSymbols.txt", "./"]
COPY ["WebApi/WebApi.csproj", "WebApi/"]
COPY ["Infrastructure/Infrastructure.csproj", "Infrastructure/"]
COPY ["Application/Application.csproj", "Application/"]
COPY ["Domain/Domain.csproj", "Domain/"]
RUN dotnet restore "WebApi/WebApi.csproj"

COPY . .
WORKDIR "/src/WebApi"

# Publish implies build, so there is no separate build stage. NSwag only runs on Debug
# builds, so a Release publish never needs the tool.
RUN dotnet publish "WebApi.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "WebApi.dll"]
