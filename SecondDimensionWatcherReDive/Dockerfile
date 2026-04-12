FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY SecondDimensionWatcherReDive.slnx .
COPY SecondDimensionWatcherReDive.Framework/ SecondDimensionWatcherReDive.Framework/
COPY SecondDimensionWatcherReDive/ SecondDimensionWatcherReDive/
COPY Plugins/ Plugins/

RUN dotnet restore SecondDimensionWatcherReDive/SecondDimensionWatcherReDive.csproj
RUN dotnet publish SecondDimensionWatcherReDive/SecondDimensionWatcherReDive.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "SecondDimensionWatcherReDive.dll"]
