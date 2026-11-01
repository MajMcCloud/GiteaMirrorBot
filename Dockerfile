# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY GiteaMirrorBot/GiteaMirrorBot.csproj GiteaMirrorBot/
RUN dotnet restore GiteaMirrorBot/GiteaMirrorBot.csproj -a $TARGETARCH

COPY GiteaMirrorBot/ GiteaMirrorBot/
RUN dotnet publish GiteaMirrorBot/GiteaMirrorBot.csproj \
    -c Release -a $TARGETARCH --no-restore --self-contained false \
    -p:UseAppHost=false -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runner
WORKDIR /app
COPY --from=build /app/publish .

ENV Http__Urls=http://0.0.0.0:5080
EXPOSE 5080
USER $APP_UID
ENTRYPOINT ["dotnet", "GiteaMirrorBot.dll"]