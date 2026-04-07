FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY TrustMap.slnx ./
COPY global.json ./
COPY src/TrustMap.Domain/TrustMap.Domain.csproj src/TrustMap.Domain/
COPY src/TrustMap.Infrastructure/TrustMap.Infrastructure.csproj src/TrustMap.Infrastructure/
COPY src/TrustMap.Api/TrustMap.Api.csproj src/TrustMap.Api/
RUN dotnet restore src/TrustMap.Api/TrustMap.Api.csproj

COPY src ./src
RUN dotnet publish src/TrustMap.Api/TrustMap.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./
RUN mkdir -p /app/storage/photos

ENTRYPOINT ["dotnet", "TrustMap.Api.dll"]
