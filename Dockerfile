# One Dockerfile for every service: the project to build is passed with --build-arg PROJECT=<name>
# (docker-compose.yml does it for each service).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG PROJECT
WORKDIR /src
COPY src/Clocks/Clocks.csproj src/Clocks/
COPY src/${PROJECT}/${PROJECT}.csproj src/${PROJECT}/
RUN dotnet restore src/${PROJECT}/${PROJECT}.csproj
COPY src/Clocks/ src/Clocks/
COPY src/${PROJECT}/ src/${PROJECT}/
RUN dotnet publish src/${PROJECT}/${PROJECT}.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
ARG PROJECT
ENV APP_DLL=${PROJECT}.dll
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]
