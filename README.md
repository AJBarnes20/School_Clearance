# School Clearance

ASP.NET Core MVC application for managing student clearance workflows.

## Prerequisites

- .NET 10 SDK
- MySQL 8

## Local configuration

Do not commit credentials. Configure direct local runs with environment variables,
your IDE, or [.NET user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets).
The deployment `.env.example` uses the flat container variables that `Program.cs`
maps to ASP.NET Core configuration.

Using user secrets:

```powershell
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "server=localhost;port=3306;database=schoolclearance_db;user=YOUR_USER;password=YOUR_PASSWORD;"
dotnet user-secrets set "Email:SmtpHost" "smtp.gmail.com"
dotnet user-secrets set "Email:SmtpPort" "587"
dotnet user-secrets set "Email:SenderEmail" "YOUR_EMAIL"
dotnet user-secrets set "Email:SenderName" "Online Clearance"
dotnet user-secrets set "Email:Password" "YOUR_APP_PASSWORD"
```

`App:BaseUrl` defaults to `http://localhost:5183` in development. Set it to the
canonical application URL in another hosting environment so email links are valid.

## Database

Create an empty MySQL database and import the SQL files in `Dump20260619` if you
need the supplied schema/data snapshot. Importing data is a deliberate local setup
step when running directly. The Docker stack imports these files automatically
only when its MySQL volume is created for the first time.

## Run locally

```powershell
dotnet restore
dotnet run --launch-profile http
```

For automatic reload while developing:

```powershell
dotnet watch --launch-profile http
```

## Docker deployment

The deployment pattern uses Docker Compose with three isolated services:

- MySQL 8.4 with a persistent named volume
- The ASP.NET Core application on internal port 5183
- Nginx as the only host-facing service, on port 8086 by default

The host requires Docker Engine, Docker Compose, Bash, and curl. The repository
does not install host packages, create system services, or open public tunnels.

Create deployment configuration:

```bash
cp .env.example .env
nano .env
```

Set unique database passwords, SMTP credentials, and the canonical
`APP_BASE_URL`. Then deploy:

```bash
chmod +x deploy.sh rollback.sh backup-db.sh docker/mysql/init-db.sh
./deploy.sh
```

Useful commands:

```bash
docker compose ps
docker compose logs -f schoolclearance-app
curl --fail http://localhost:8086/health
./rollback.sh
./backup-db.sh
```

`deploy.sh` saves the current application image as
`schoolclearance-app:previous`, builds the replacement, waits for all health
checks, validates Nginx, and automatically attempts an image rollback on failure.

### Database lifecycle

On a fresh MySQL volume, `docker/mysql/init-db.sh` imports all SQL files from
`Dump20260619` in one session with foreign-key checks temporarily disabled. Once
the named volume exists, restarts and redeployments preserve its data and do not
re-import the snapshot. The initializer also records `Dump20260619` in the
`deployment_seed_history` table and refuses to import when application tables
already exist, preventing duplicate seed rows or accidental data replacement.

To intentionally initialize a new database, use a new Compose project/volume.
Do not delete the existing volume unless its data has been backed up and permanent
loss is intended.

Database backups default to the ignored `backups/` directory and retain seven
days. Override `BACKUP_DIR` and `BACKUP_RETENTION_DAYS` in `.env` when needed.
Restore a backup into the running database with:

```bash
gunzip -c /path/to/backup.sql.gz | \
  docker exec -i schoolclearance-mysql \
  mysql -uroot -p"$DB_ROOT_PASSWORD" "$DB_NAME"
```

For production, schedule `backup-db.sh` with the host's scheduler and store
backups on separate durable storage.
