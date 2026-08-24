# Architecture

## Product boundaries

Theorymancer is a multi-game platform, initially focused on Guild Wars 2. The
public website is separate from each game's tools or integrations; calling a
game component a "mod" does not imply a particular implementation.

The website will be available at `https://theorymancer.com` in production.
Game-specific code belongs under `apps/<game>/`. We will only extract shared
cross-game abstractions when a second game establishes a concrete need.

## Application structure

The public website and API use Node.js and TypeScript:

- `apps/web` is the React/Vite website.
- `apps/api` is the HTTP API.
- `apps/<game>` is a game-specific application namespace.
- `packages/contracts` holds runtime-validated contracts shared by TypeScript
  applications.

Guild Wars 2 ingestion and deterministic analysis intentionally have no chosen
runtime yet. That code is isolated in `apps/guild-wars-2`, allowing a future
Python worker to use scientific and ML tooling without coupling it to the
website or API runtime.

## Google Cloud deployment

Google Cloud hosts the initial deployment. Cost control is the primary
infrastructure constraint, so development and production initially share one
GCP project while remaining logically isolated:

- Terraform has independent `development` and `production` root modules and
  remote-state prefixes.
- Each environment has distinct Cloud Run services, runtime service accounts,
  and Cloud Storage buckets.
- GitHub Environments separate development from production deployment
  permissions. Production should require reviewers before it is used by
  customers.
- GitHub Actions uses Workload Identity Federation; no service-account keys
  are stored in the repository.

Cloud Run hosts the web and API services and scales to zero when unused. Cloud
Storage holds uploaded combat logs and generated artifacts. Artifact Registry
holds deployable container images. Development uses the generated Cloud Run
URLs and therefore requires no second domain. Production is reserved for
`theorymancer.com`; DNS and custom-domain configuration occur after ownership
and verification are available.

The shared development environment is the target for deployed integration
checks. We will not maintain permanent preview environments for each pull
request. Unit and build checks run in CI; development deployments are used for
integration testing when required.

## Persistence

The initial deployment has no database. PostgreSQL is the expected future
system of record for accounts, uploads, analysis history, and coaching
recommendations. We will introduce it only when durable product data is needed,
then document the database topology, migrations, backups, and environment
isolation before provisioning it.
