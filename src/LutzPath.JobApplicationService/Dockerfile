# Build with the SDK; only published output is copied into the runtime image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Cache dependency restore separately from source changes.
COPY LutzPath.JobApplicationService.csproj ./
RUN dotnet restore LutzPath.JobApplicationService.csproj

COPY . .
RUN dotnet publish LutzPath.JobApplicationService.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# A new named volume inherits this directory's ownership.
RUN mkdir -p /data && chown app:app /data
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "LutzPath.JobApplicationService.dll"]

