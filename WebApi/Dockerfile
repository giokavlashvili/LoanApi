#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

# Must track the TargetFramework in the .csproj files (net10.0). An SDK image older than the
# target framework cannot restore the projects at all, which is what these tags used to do:
# they sat at 7.0 while the solution moved to net10.0.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
# These match the explicit ASPNETCORE_URLS in docker-compose.override.yml. Note that .NET 8+
# images default to ASPNETCORE_HTTP_PORTS=8080 rather than the old port 80 — the compose file
# overrides it, so these stay correct. Drop that override and the ports below must become 8080.
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Directory.Build.props and BannedSymbols.txt are repo-root files that every .csproj imports —
# they add the BannedApiAnalyzers PackageReference and the RS0030 analyzer input. Restoring
# without them resolves a different package graph than the later build asks for, so the build
# has to silently re-restore and this layer's caching buys nothing.
COPY ["Directory.Build.props", "BannedSymbols.txt", "./"]
COPY ["WebApi/WebApi.csproj", "WebApi/"]
COPY ["Infrastructure/Infrastructure.csproj", "Infrastructure/"]
COPY ["Application/Application.csproj", "Application/"]
COPY ["Domain/Domain.csproj", "Domain/"]
RUN dotnet restore "WebApi/WebApi.csproj"
COPY . .
WORKDIR "/src/WebApi"
# Release, so the NSwag post-build target does not run: it is conditioned on Debug and would
# otherwise need the NSwag tool restorable inside the image for no benefit here.
RUN dotnet build "WebApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WebApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WebApi.dll"]
