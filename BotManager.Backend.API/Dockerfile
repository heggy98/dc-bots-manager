# Build context: repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY BotManager.Backend.API/BotManager.Backend.API.csproj BotManager.Backend.API/
COPY BotManager.Backend.Bots/BotManager.Backend.Bots.csproj BotManager.Backend.Bots/
COPY BotManager.Backend.Entities/BotManager.Backend.Entities.csproj BotManager.Backend.Entities/
COPY BotManager.Backend.Services/BotManager.Backend.Services.csproj BotManager.Backend.Services/
COPY BotManager.Backend.Shared/BotManager.Backend.Shared.csproj BotManager.Backend.Shared/
RUN dotnet restore BotManager.Backend.API/BotManager.Backend.API.csproj
COPY BotManager.Backend.API/ BotManager.Backend.API/
COPY BotManager.Backend.Bots/ BotManager.Backend.Bots/
COPY BotManager.Backend.Entities/ BotManager.Backend.Entities/
COPY BotManager.Backend.Services/ BotManager.Backend.Services/
COPY BotManager.Backend.Shared/ BotManager.Backend.Shared/
RUN dotnet publish BotManager.Backend.API/BotManager.Backend.API.csproj -c Release -o /app --no-restore \
    -p:SpaRoot= -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD ["dotnet", "BotManager.Backend.API.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "BotManager.Backend.API.dll"]
