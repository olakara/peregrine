FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Peregrine.Api/Peregrine.Api.csproj ./src/Peregrine.Api/
RUN dotnet restore ./src/Peregrine.Api/Peregrine.Api.csproj

COPY . .
RUN dotnet publish ./src/Peregrine.Api/Peregrine.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Peregrine.Api.dll"]
