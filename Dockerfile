# Builds the API image. Used by the pipeline and for running a Test-like environment locally.
#   docker build -t med-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, so this layer is cached until a project file changes.
COPY Directory.Build.props global.json ./
COPY src/Med.Domain/Med.Domain.csproj   src/Med.Domain/
COPY src/Med.Storage/Med.Storage.csproj src/Med.Storage/
COPY src/Med.Ingest/Med.Ingest.csproj   src/Med.Ingest/
COPY src/Med.Api/Med.Api.csproj         src/Med.Api/
RUN dotnet restore src/Med.Api/Med.Api.csproj

COPY src/ src/
RUN dotnet publish src/Med.Api/Med.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
# Run as the non-root user that ships in the official image.
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Med.Api.dll"]
