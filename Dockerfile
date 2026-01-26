# --- Base Stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

# [CHANGE] Only expose the ports we are actually using now
EXPOSE 5000
EXPOSE 104

# Define the data path environment variable
ENV RCONNECT_DATA_PATH="/data"

# [FIX] Force ASP.NET Core to listen on port 5000 (Default is 8080 in .NET 8+)
ENV ASPNETCORE_HTTP_PORTS=5000

# --- Build Stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["RadiopaediaConnect.csproj", "."]
RUN dotnet restore "./RadiopaediaConnect.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./RadiopaediaConnect.csproj" -c $BUILD_CONFIGURATION -o /app/build

# --- Publish Stage ---
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./RadiopaediaConnect.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# --- Final Stage ---
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Direct entry point
ENTRYPOINT ["dotnet", "RadiopaediaConnect.dll"]