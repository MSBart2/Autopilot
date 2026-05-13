# Health Endpoint

The web app exposes one readiness route.

## `/health/ready`

Returns ASP.NET Core health check readiness status. No custom health checks are registered, so this endpoint reports healthy when the application host is running.
