FROM mcr.microsoft.com/dotnet/core/runtime:3.1-buster-slim AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
WORKDIR /src
COPY ["EventHubSender.csproj", "EventHubSender/"]
RUN dotnet restore "EventHubSender/EventHubSender.csproj"
COPY . EventHubSender/
WORKDIR "/src/EventHubSender"
RUN dotnet build "EventHubSender.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "EventHubSender.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EventHubSender.dll"]
