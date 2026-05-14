FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/UByMoen.Api/UByMoen.Api.csproj", "src/UByMoen.Api/"]
COPY ["src/UByMoen.Core/UByMoen.Core.csproj", "src/UByMoen.Core/"]
RUN dotnet restore "src/UByMoen.Api/UByMoen.Api.csproj"
COPY . .
WORKDIR "/src/src/UByMoen.Api"
RUN dotnet build "UByMoen.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "UByMoen.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UByMoen.Api.dll"]
