FROM node:22.22.0-bookworm-slim AS node-build

WORKDIR /workspace
COPY package.json package-lock.json ./
COPY src/GovernedAgent.Console/package.json src/GovernedAgent.Console/package.json
COPY src/plan-verifier/package.json src/plan-verifier/package.json
COPY tests/plan-verifier-tests/package.json tests/plan-verifier-tests/package.json
RUN npm ci --ignore-scripts --force
COPY src/plan-verifier/ src/plan-verifier/
RUN npm run build --workspace @governed-agent/plan-verifier

FROM node:22.22.0-bookworm-slim AS copilot-cli

WORKDIR /cli
COPY deploy/hosted-copilot/package.json deploy/hosted-copilot/package-lock.json ./
RUN npm ci --ignore-scripts \
    && install -D -m 0555 node_modules/@github/copilot-linux-x64/copilot /opt/copilot/copilot \
    && chmod 0555 /opt/copilot

FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS dotnet-build

COPY --from=node-build /usr/local/ /usr/local/
WORKDIR /workspace
COPY . .
COPY --from=node-build /workspace/src/plan-verifier/dist/ src/plan-verifier/dist/
RUN dotnet restore src/GovernedAgent.Host/GovernedAgent.Host.csproj \
    && dotnet publish src/GovernedAgent.Host/GovernedAgent.Host.csproj \
        --configuration Release \
        --no-restore \
        --output /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0.4-noble AS runtime

ENV ASPNETCORE_URLS=http://0.0.0.0:8088 \
    COPILOT_AUTO_UPDATE=false \
    COPILOT_CLI_PATH=/opt/copilot/copilot \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    NODE_EXECUTABLE=/usr/local/bin/node \
    PORT=8088

COPY --from=node-build /usr/local/ /usr/local/
COPY --from=copilot-cli /opt/copilot/ /opt/copilot/
WORKDIR /app
COPY --from=dotnet-build /out/ ./
RUN chmod -R a-w /opt/copilot /app/Hosted/verifier \
    && test -x /opt/copilot/copilot \
    && test -f /app/Hosted/verifier/cli.js

USER app
EXPOSE 8088
ENTRYPOINT ["dotnet", "GovernedAgent.Host.dll"]
