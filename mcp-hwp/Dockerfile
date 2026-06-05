FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/*.csproj ./
RUN dotnet restore
COPY src/ ./
RUN dotnet publish -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates python3-venv libreoffice-writer libreoffice-calc libreoffice-impress fonts-noto-cjk && rm -rf /var/lib/apt/lists/* \
    && python3 -m venv /opt/pyhwp \
    && /opt/pyhwp/bin/pip install --no-cache-dir pyhwp six lxml olefile \
    && mkdir -p /workspace
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    MCP_ALLOWED_DIRS=/ \
    SOFFICE_PATH=/usr/bin/soffice \
    HWP5TXT_PATH=/opt/pyhwp/bin/hwp5txt
COPY --from=build /app ./
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 CMD curl -fsS http://localhost:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "McpHwp.dll"]
