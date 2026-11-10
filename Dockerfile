# Multi-stage Dockerfile for MarbookApi (.NET 10 Web API)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore as distinct layer
COPY ["MarbookApi.csproj", "./"]
RUN dotnet restore "MarbookApi.csproj"

# Copy source and publish Release build
COPY . .
RUN dotnet publish "MarbookApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage (Ubuntu 24.04 noble lightweight runtime)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Standard ASP.NET Core port in .NET 8+ containers
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "MarbookApi.dll"]

