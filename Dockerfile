# syntax=docker/dockerfile:1
# Autopilot ASP.NET Core MVC Application
# Multi-stage build for optimized image size

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Allow an external pre-published directory to be passed via build-arg
ARG PUBLISHED_DIR=
# Version metadata arguments
ARG VERSION=0.0.0
ARG REVISION=unknown

# Copy everything (restore will resolve packages for the whole solution)
COPY . ./

# If a published directory was mounted into the build context, prefer
# using it; otherwise restore and publish as before.
RUN if [ -n "$PUBLISHED_DIR" ] && [ -d "$PUBLISHED_DIR" ]; then \
  echo "Using pre-published output from $PUBLISHED_DIR"; \
  mkdir -p /app/publish && cp -a "$PUBLISHED_DIR"/* /app/publish/; \
  else \
  echo "No pre-published output provided; restoring and publishing"; \
  dotnet restore Autopilot.sln && \
  dotnet publish web/Autopilot.Web.csproj -c Release -o /app/publish --no-restore; \
  fi

# Stage 2: Dev (optional, for docker-compose hot reload)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dev
WORKDIR /src
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
  ASPNETCORE_ENVIRONMENT=Development
CMD ["dotnet", "watch", "run", "--project", "web/Autopilot.Web.csproj", "--urls", "http://0.0.0.0:8080"]

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Version metadata arguments for runtime stage
ARG VERSION=0.0.0
ARG REVISION=unknown

# OCI-compliant labels for version metadata
LABEL org.opencontainers.image.version="${VERSION}"
LABEL org.opencontainers.image.revision="${REVISION}"
LABEL org.opencontainers.image.source="https://github.com/rbmathis/Autopilot"
LABEL org.opencontainers.image.title="Autopilot"

# Install curl for health check
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Create non-root user for security
RUN groupadd -r Autopilot && useradd -r -g Autopilot Autopilot

# Copy published app
COPY --from=build /app/publish .

# Set proper permissions
RUN chown -R Autopilot:Autopilot /app

# Switch to non-root user
USER Autopilot

# Expose port (configurable via environment)
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080 \
  ASPNETCORE_ENVIRONMENT=Production \
  DOTNET_EnableDiagnostics=0

ENTRYPOINT ["dotnet", "Autopilot.Web.dll"]
