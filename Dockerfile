# Imagen de producción de TEK-COM Gestión. Se construye en GitHub Actions y se publica en ghcr.io.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primero solo los archivos de proyecto, para aprovechar la caché de restauración de paquetes.
COPY Directory.Build.props Directory.Packages.props Tekcom.Gestion.sln ./
COPY src/Tekcom.Gestion.Domain/*.csproj src/Tekcom.Gestion.Domain/
COPY src/Tekcom.Gestion.Application/*.csproj src/Tekcom.Gestion.Application/
COPY src/Tekcom.Gestion.Infrastructure/*.csproj src/Tekcom.Gestion.Infrastructure/
COPY src/Tekcom.Gestion.Web/*.csproj src/Tekcom.Gestion.Web/
COPY tests/Tekcom.Gestion.Domain.Tests/*.csproj tests/Tekcom.Gestion.Domain.Tests/
COPY tests/Tekcom.Gestion.Application.Tests/*.csproj tests/Tekcom.Gestion.Application.Tests/
RUN dotnet restore Tekcom.Gestion.sln

COPY src/ src/
RUN dotnet publish src/Tekcom.Gestion.Web/Tekcom.Gestion.Web.csproj \
    --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# libfontconfig1: necesaria para generar PDF (QuestPDF/Skia). tzdata: zona horaria de El Salvador.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 tzdata \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# Datos que deben sobrevivir a los reinicios: archivos subidos y claves de sesión.
RUN mkdir -p /data/uploads /data/keys && chown -R app:app /data
VOLUME /data

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Storage__Path=/data/uploads \
    DataProtection__Path=/data/keys

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tekcom.Gestion.Web.dll"]
