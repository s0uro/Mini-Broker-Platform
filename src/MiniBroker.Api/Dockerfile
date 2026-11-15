# Build context: repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so this layer is cached until project files change.
COPY src/MiniBroker.Domain/MiniBroker.Domain.csproj src/MiniBroker.Domain/
COPY src/MiniBroker.Infrastructure/MiniBroker.Infrastructure.csproj src/MiniBroker.Infrastructure/
COPY src/MiniBroker.Api/MiniBroker.Api.csproj src/MiniBroker.Api/
RUN dotnet restore src/MiniBroker.Api/MiniBroker.Api.csproj

COPY src/ src/
RUN dotnet publish src/MiniBroker.Api/MiniBroker.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "MiniBroker.Api.dll"]
