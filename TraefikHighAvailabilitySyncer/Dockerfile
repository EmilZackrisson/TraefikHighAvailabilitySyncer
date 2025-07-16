FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
RUN groupadd -g 996 docker && gpasswd -a $APP_UID docker
USER $APP_UID:996
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["TraefikHighAvailabilitySyncer/TraefikHighAvailabilitySyncer.csproj", "TraefikHighAvailabilitySyncer/"]
RUN dotnet restore "TraefikHighAvailabilitySyncer/TraefikHighAvailabilitySyncer.csproj"
COPY . .
WORKDIR "/src/TraefikHighAvailabilitySyncer"
RUN dotnet build "./TraefikHighAvailabilitySyncer.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./TraefikHighAvailabilitySyncer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TraefikHighAvailabilitySyncer.dll"]
