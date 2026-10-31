# Two images from one build: the API (target "api") and the one-shot database migrator (target "migrator").
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Helpdesk.HttpApi.Host/Helpdesk.HttpApi.Host.csproj -c Release -o /out/api /p:UseAppHost=false \
 && dotnet publish src/Helpdesk.DbMigrator/Helpdesk.DbMigrator.csproj -c Release -o /out/migrator /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migrator
WORKDIR /app
COPY --from=build /out/migrator .
ENTRYPOINT ["dotnet", "Helpdesk.DbMigrator.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /out/api .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Helpdesk.HttpApi.Host.dll"]
