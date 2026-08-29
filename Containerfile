# Stage 1: Build frontend
FROM node:24 AS frontend-build
WORKDIR /app
RUN corepack enable
COPY SecondDimensionWatcherReDive.Client/ .
RUN yarn install --immutable
RUN yarn build

# Stage 2: Build backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src
COPY SecondDimensionWatcherReDive.slnx .
COPY SecondDimensionWatcherReDive.Framework/ SecondDimensionWatcherReDive.Framework/
COPY SecondDimensionWatcherReDive/ SecondDimensionWatcherReDive/
COPY Plugins/ Plugins/
COPY Share/ Share/
COPY --from=frontend-build /app/dist SecondDimensionWatcherReDive/wwwroot/
RUN dotnet restore SecondDimensionWatcherReDive/SecondDimensionWatcherReDive.csproj
RUN dotnet publish SecondDimensionWatcherReDive/SecondDimensionWatcherReDive.csproj -c Release -o /app --no-restore

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*
COPY --from=backend-build /app .
EXPOSE 8080
# Optional: read-only NFSv4 export (set Nfs:Enabled=true to activate; publish port at run time).
EXPOSE 2049
ENTRYPOINT ["dotnet", "SecondDimensionWatcherReDive.dll"]
