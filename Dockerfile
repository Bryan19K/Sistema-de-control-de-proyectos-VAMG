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
# Se vuelve a restaurar con el código completo: el SDK decide qué recursos de Blazor incluir
# (por ejemplo _framework/blazor.web.js) viendo las páginas .razor, que en el paso anterior no estaban.
RUN dotnet publish src/Tekcom.Gestion.Web/Tekcom.Gestion.Web.csproj \
    --configuration Release --output /app/publish \
    && test -f /app/publish/wwwroot/_framework/blazor.web.js \
    || (echo "ERROR: falta _framework/blazor.web.js en la publicación" && exit 1)

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# libfontconfig1: PDF (QuestPDF/Skia). libgssapi-krb5-2: la pide Npgsql al conectar. tzdata: hora de El Salvador.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 libgssapi-krb5-2 tzdata \
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
