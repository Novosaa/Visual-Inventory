# Visual Inventory (web) 3.0.3 - how Google builds the app.
# Step 1 builds it with Microsoft's official .NET 10 build image.
# Step 2 copies only the finished app into Microsoft's official .NET 10 runtime image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY VisualInventoryWeb.csproj ./
RUN dotnet restore VisualInventoryWeb.csproj
COPY . ./
RUN dotnet publish VisualInventoryWeb.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    ASPNETCORE_ENVIRONMENT=Production
# Runs as Microsoft's built-in non-admin user, not as root.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "VisualInventoryWeb.dll"]
