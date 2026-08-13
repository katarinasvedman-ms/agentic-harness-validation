FROM node:22.22.0-bookworm-slim AS node

FROM mcr.microsoft.com/dotnet/sdk:10.0.400

COPY --from=node /usr/local/ /usr/local/

ENV CI=true \
    COPILOT_AUTO_UPDATE=false \
    COPILOT_CLI_PATH=/opt/copilot/copilot \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    NUGET_XMLDOC_MODE=skip

WORKDIR /workspace
COPY . .

RUN npm ci --ignore-scripts --force \
    && npm run build \
    && npm ci --ignore-scripts --prefix deploy/hosted-copilot \
    && install -D -m 0555 deploy/hosted-copilot/node_modules/@github/copilot-linux-x64/copilot /opt/copilot/copilot \
    && chmod 0555 /opt/copilot \
    && dotnet restore GovernedAgentDemo.sln \
    && dotnet build GovernedAgentDemo.sln --no-restore --configuration Release \
    && dotnet test tests/GovernedAgent.SecurityTests/GovernedAgent.SecurityTests.csproj --no-build --no-restore --configuration Release \
    && dotnet test tests/GovernedAgent.IntegrationTests/GovernedAgent.IntegrationTests.csproj --no-build --no-restore --configuration Release \
    && sed -i 's/\r$//' deploy/hosted-copilot/smoke.sh \
    && chmod 0555 deploy/hosted-copilot/smoke.sh

USER app
ENTRYPOINT ["bash", "deploy/hosted-copilot/smoke.sh"]
