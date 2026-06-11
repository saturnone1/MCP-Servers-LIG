FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/*.csproj ./
RUN dotnet restore
COPY src/ ./
RUN dotnet publish -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG OFFICECLI_VERSION=v1.0.110
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates antiword && rm -rf /var/lib/apt/lists/* \
    && curl -fsSL "https://github.com/iOfficeAI/OfficeCLI/releases/download/${OFFICECLI_VERSION}/officecli-linux-x64" -o /usr/local/bin/officecli \
    && chmod +x /usr/local/bin/officecli \
    && mkdir -p /workspace
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    MCP_ALLOWED_DIRS=/ \
    MCP_ENABLE_OFFICE_WRITES=true \
    OFFICECLI_PATH=/usr/local/bin/officecli \
    ANTIWORD_PATH=/usr/bin/antiword \
    OFFICECLI_SKIP_UPDATE=1
COPY --from=build /app ./
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 CMD curl -fsS http://localhost:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "McpOffice.dll"]
