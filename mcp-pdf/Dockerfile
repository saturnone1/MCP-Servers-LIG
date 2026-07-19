FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/*.csproj ./src/
RUN dotnet restore ./src/McpPdf.csproj
COPY src/ ./src/
COPY config/ ./config/
RUN dotnet publish ./src/McpPdf.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates poppler-utils && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    PDF_DATA_DIR=/data \
    MCP_ALLOWED_DIRS=/documents \
    MCP_ENABLE_PDF_WRITES=true \
    DOCLING_MODE=remote \
    DOCLING_SERVICE_URL=http://docling:5001
COPY --from=build /app ./
VOLUME ["/data", "/documents"]
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 CMD curl -fsS http://localhost:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "McpPdf.dll"]
