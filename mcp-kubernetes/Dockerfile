FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/*.csproj ./
RUN dotnet restore
COPY src/ ./
RUN dotnet publish -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG KUBECTL_VERSION=v1.30.7
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates && rm -rf /var/lib/apt/lists/* \
    && curl -fsSL "https://dl.k8s.io/release/${KUBECTL_VERSION}/bin/linux/amd64/kubectl" -o /usr/local/bin/kubectl \
    && chmod +x /usr/local/bin/kubectl \
    && mkdir -p /workspace /root/.kube
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    MCP_ALLOWED_DIRS=/ \
    MCP_ENABLE_KUBERNETES_WRITES=true \
    MCP_ENABLE_RAW_KUBECTL=true \
    KUBECTL_PATH=/usr/local/bin/kubectl
COPY --from=build /app ./
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 CMD curl -fsS http://localhost:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "McpKubernetes.dll"]
