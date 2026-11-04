# One image: the ASP.NET Core API also serves the built Vue app. Listens on 8080.

# ---- Build the Vue app ----
FROM node:22-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
# vite.config.ts writes the build to ../src/Taskloom.Api/wwwroot
RUN npm run build

# ---- Build the API ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY Taskloom.slnx ./
COPY src/Taskloom.Domain/Taskloom.Domain.csproj src/Taskloom.Domain/
COPY src/Taskloom.Application/Taskloom.Application.csproj src/Taskloom.Application/
COPY src/Taskloom.Infrastructure/Taskloom.Infrastructure.csproj src/Taskloom.Infrastructure/
COPY src/Taskloom.Cloud.Azure/Taskloom.Cloud.Azure.csproj src/Taskloom.Cloud.Azure/
COPY src/Taskloom.Api/Taskloom.Api.csproj src/Taskloom.Api/
RUN dotnet restore src/Taskloom.Api/Taskloom.Api.csproj
COPY src/ src/
COPY --from=web /src/src/Taskloom.Api/wwwroot src/Taskloom.Api/wwwroot
RUN dotnet publish src/Taskloom.Api/Taskloom.Api.csproj -c Release -o /app --no-restore \
    # The app runs as a non-root user: make sure it can read everything, whatever the file modes on disk.
    && chmod -R a+rX /app

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Taskloom.Api.dll"]
