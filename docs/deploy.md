# Putting the demo online

The UI is a static site (GitHub Pages) and the API is a Docker web service with a PostgreSQL database.
Everything below has a free tier. You need your own accounts: nothing can be created for you.

| Part | Where | Cost |
|---|---|---|
| UI | GitHub Pages, built by `.github/workflows/pages.yml` | free |
| API | Render web service from `render.yaml` | free (sleeps after 15 min idle, about a minute to wake) |
| Database | Neon PostgreSQL | free |

## 1. Database

Create a Neon project and copy the connection details into Npgsql format:
`Host=<host>;Port=5432;Database=<db>;User ID=<user>;Password=<password>;SSL Mode=Require`

## 2. API

1. In Render choose **New → Blueprint** and pick this repository. Render reads `render.yaml`.
2. Fill the values it asks for: the connection string, and for now any placeholder for the three URLs.
3. After the first deploy copy the service address (for example `https://helpdesk-api.onrender.com`) into
   `App__SelfUrl` and `AuthServer__Authority`, and your Pages address into `App__CorsOrigins`
   (for this repository: `https://kimthanhtran.github.io`). Render redeploys.
4. Open `<service address>/api/abp/application-configuration`: a JSON answer means the API is up.
   The first start creates the tables and the demo users (`Helpdesk__MigrateOnStartup`).

## 3. UI

1. In the repository settings choose **Pages → Source: GitHub Actions**.
2. Add a repository variable `API_URL` with the service address (**Settings → Secrets and variables → Actions →
   Variables**), then push to `main` or run the **Pages** workflow by hand.
3. The site is at `https://<user>.github.io/helpdesk-abp/`.

## Rehearse it first, on your own computer

`docs/rehearsal/docker-compose.yml` is the online setup in miniature: the API alone on `http://localhost:8090`, a plain
PostgreSQL where Neon would be, no separate migration step (`Helpdesk__MigrateOnStartup`), and CORS allowing
`http://localhost:4173`. Serve the UI from that other origin the way Pages would:

```bash
docker compose -f docs/rehearsal/docker-compose.yml -p rehearsal up -d --build
cd frontend && VITE_BASE=/helpdesk-abp/ VITE_API_URL=http://localhost:8090 npm run build -- --outDir ../site/helpdesk-abp
cd ../site && python -m http.server 4173        # open http://localhost:4173/helpdesk-abp/
docker compose -p rehearsal down -v             # when you are done
```

This found two things worth knowing before a real deploy: the first start on an empty database used to log errors
because ABP saves its permission definitions as it starts (the migration now runs before that), and a page on another
origin cannot read a download's file name unless the API exposes `Content-Disposition` (it now does). On Windows,
Git Bash rewrites `VITE_BASE=/helpdesk-abp/` into a disk path; set `MSYS_NO_PATHCONV=1` there (GitHub Actions is not affected).

## Things to know

- The demo accounts (`alice`, `bob`, `agent1`, password `1q2w3E*`) are public. Turn `SeedDemoData` off and create
  real users before you put anything private in this database.
- The browser talks to the API directly, so the API must allow the Pages origin (`App__CorsOrigins`). SignalR sends the
  token in the query string, which can end up in server logs; use a short token lifetime for anything real.
- A sleeping free service drops the live connection; the bell shows a grey dot until the page reconnects.
- Locally you do not need any of this: `docker compose up --build` serves the UI on http://localhost:3000.
