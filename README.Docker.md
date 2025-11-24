# Docker Setup for LoanApi

This project includes Docker configuration for running the .NET API with Angular SPA and PostgreSQL database.

## Project Structure

- **.NET 10.0 API** with ASP.NET Core Identity (scaffolded Razor Pages)
- **Angular v21 SPA** in `WebApi/ClientApp`
- **PostgreSQL** database

## Docker Files

- `WebApi/Dockerfile` - Multi-stage build for .NET API (includes Angular build)
- `WebApi/ClientApp/Dockerfile` - Angular development server container
- `docker-compose.yml` - Development setup with separate Angular container
- `docker-compose.prod.yml` - Production setup (Angular built into .NET)

## Quick Start

### Development Mode (with separate Angular container)

```bash
# Start all services including Angular dev server
docker-compose --profile dev up --build

# Access:
# - .NET API: http://localhost:5041
# - Angular Dev Server: http://localhost:44448
# - PostgreSQL: localhost:5432
```

### Production Mode (Angular built into .NET)

```bash
# Start production services
docker-compose -f docker-compose.prod.yml up --build

# Access:
# - Application: http://localhost:80
# - PostgreSQL: localhost:5432
```

## Services

### Development (`docker-compose.yml`)

1. **postgres** - PostgreSQL 16 database
2. **webapi** - .NET API service (port 5041)
3. **angular** - Angular dev server (port 44448, profile: dev)

### Production (`docker-compose.prod.yml`)

1. **postgres** - PostgreSQL 16 database
2. **webapi** - .NET API with built-in Angular SPA (port 80)

## Environment Variables

You can override database settings using environment variables:

```bash
POSTGRES_DB=LoanDB
POSTGRES_USER=postgres
POSTGRES_PASSWORD=postgres
```

## Building Individual Services

### Build .NET API only:

```bash
docker build -f WebApi/Dockerfile -t loanapi-webapi .
```

### Build Angular dev server only:

```bash
docker build -f WebApi/ClientApp/Dockerfile -t loanapi-angular ./WebApi/ClientApp
```

## Notes

- The Angular proxy configuration (`proxy.conf.js`) automatically detects Docker environment
- In development mode, Angular runs separately with hot reload
- In production mode, Angular is built and served as static files from the .NET API
- Database migrations run automatically on startup in Development mode
- PostgreSQL data is persisted in Docker volumes

## Troubleshooting

### Angular proxy not working in Docker

Ensure the `DOCKER_ENV=true` environment variable is set in the Angular container (already configured in docker-compose.yml).

### Database connection issues

Check that the PostgreSQL container is healthy before the API starts. The compose file includes health checks and dependencies.

### Port conflicts

If ports 80, 443, 44448, or 5432 are already in use, modify the port mappings in docker-compose.yml.

