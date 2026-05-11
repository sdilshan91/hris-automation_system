# Development Instructions (from Project Owner)

## Project Structure
- Frontend and backend are **separate codebases** in separate folders
  - `src/frontend/` — Angular standalone project
  - `src/backend/` — ASP.NET Core standalone solution

## Frontend Requirements
- **Angular 20** (latest, standalone components, signals)
- **UI Frameworks:** Tailwind CSS + Angular Material (NO Bootstrap)
- Must be **100% mobile responsive** (360px to 4K)
- **Rich UI** — modern, polished, Notion-like design aesthetic
  - Clean whitespace, subtle shadows, smooth animations/transitions
  - Minimalist but functional — inspired by Notion, Linear, Vercel dashboard style
  - Tenant-branded: logo + primary color customizable
- Use free/open-source plugins and libraries wherever available
- Use `ngx-bootstrap` only if a specific Bootstrap component is needed (e.g., carousel)

## Backend Requirements
- ASP.NET Core 10 Web API (Clean Architecture)
- **PostgreSQL** (localhost)
  - Host: `localhost`
  - Port: `5432`
  - Username: `hris-developer`
  - Password: `Sanjesi#123`
  - Database: `hris-dev`
  - Connection string: `Host=localhost;Port=5432;Database=hris-dev;Username=hris-developer;Password=Sanjesi#123`
- **Serilog** for structured logging
- **Polly** for resilience (retry, circuit-breaker, fallback)
- **Hangfire** for background jobs, scheduled tasks, notifications
  - Hangfire storage: PostgreSQL (`Hangfire.PostgreSql`)
- **Redis** for caching
  - Host: `localhost`
  - Port: `6379`
  - Setup: `docker run -d --name hris-redis -p 6379:6379 redis`
- Use free/open-source libraries wherever available

## Authentication (Local Dev)
- Admin login via **username + password** only for now
- Social logins (Google/Microsoft/Apple) deferred to later phase
- JWT + refresh token flow

## General Rules
- Prefer existing open-source plugins/libraries over custom implementations
- All libraries must be free and open-source
- Rich, modern UI with smooth animations and transitions
