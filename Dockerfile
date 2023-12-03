#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 5500

USER root
RUN apt-get update && \
    apt-get upgrade -y && \
    apt-get install -y wget git

RUN wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
RUN dpkg -i packages-microsoft-prod.deb
RUN rm packages-microsoft-prod.deb

RUN apt-get update && \
    apt-get upgrade -y && \
    apt-get install -y dotnet-sdk-8.0

USER 1001

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Discord.Net.DebugDeploy/Discord.Net.DebugDeploy.csproj", "Discord.Net.DebugDeploy/"]
RUN dotnet restore "./Discord.Net.DebugDeploy/./Discord.Net.DebugDeploy.csproj"
COPY . .
WORKDIR "/src/Discord.Net.DebugDeploy"
RUN dotnet build "./Discord.Net.DebugDeploy.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Discord.Net.DebugDeploy.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Discord.Net.DebugDeploy.dll"]