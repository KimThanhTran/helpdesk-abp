# Putting the demo online

The UI is a static site (GitHub Pages) and the API is a Docker web service with a PostgreSQL database, all on free plans.
You need your own accounts (Render and GitHub): nothing can be created for you.

| Part | Where | Cost |
|---|---|---|
| UI | GitHub Pages, built by `.github/workflows/pages.yml` | free |
| API + database | Render, from `render.yaml` | free: the API sleeps after 15 minutes idle (about a minute to wake) and the free database is deleted after 30 days |

## 1. API and database

1. In Render choose **New → Blueprint**, connect this repository and apply `render.yaml`. It creates the PostgreSQL and the
   API; nothing has to be typed in. The API reads the database from `DATABASE_URL` and its own address from
   `RENDER_EXTERNAL_URL` (`Helpdesk.Hosting.PlatformSettings`), and creates the tables and the demo users on its first start
   (`Helpdesk__MigrateOnStartup`).
2. Open `<service address>/api/abp/application-configuration` (the address is on the service's page, for example
   `https://helpdesk-api.onrender.com`): a JSON answer means the API is up.

To keep the data longer than 30 days, use a database that lasts (Neon, or a paid Render one) and set
`ConnectionStrings__Default` on the service to its connection string in Npgsql form
(`Host=…;Port=5432;Database=…;User ID=…;Password=…;SSL Mode=Require`). It takes precedence over `DATABASE_URL`.

`render.yaml` also generates `OpenIddict__KeySecret`: the keys that sign and encrypt tokens are derived from it, so people stay
signed in when the free API goes to sleep and wakes. Without it every start makes new keys and signs everyone out.

## 2. UI

1. In the repository settings choose **Pages → Source: GitHub Actions**.
2. Add a repository variable `API_URL` with the service address (**Settings → Secrets and variables → Actions →
   Variables**), then run the **Pages** workflow.
3. The site is at `https://<user>.github.io/helpdesk-abp/`. The API allows that origin through `App__CorsOrigins`
   in `render.yaml` (the user name in lower case, no path); change it there if the repository belongs to someone else.

## Rehearse it first, on your own computer

`docs/rehearsal/docker-compose.yml` is the online setup in miniature: the API alone on `http://localhost:8090`, a plain
PostgreSQL, no separate migration step (`Helpdesk__MigrateOnStartup`), the database and the address given the way Render
gives them (`DATABASE_URL`, `RENDER_EXTERNAL_URL`), and CORS allowing `http://localhost:4173`. Serve the UI from that other origin the way Pages would:

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

## E-mail (optional)

Add the mail settings to the Render service the same way (see [email.md](email.md)): `Helpdesk__Email__Host`, `…__Port`,
`…__User`, `…__Password`, `…__From`, `…__PublicUrl` (your Pages address). Leave them out and no mail is sent.

## Things to know

- The demo accounts (`alice`, `bob`, `agent1`, password `1q2w3E*`) are public. Turn `SeedDemoData` off and create
  real users before you put anything private in this database.
- The browser talks to the API directly, so the API must allow the Pages origin (`App__CorsOrigins`). SignalR sends the
  token in the query string, which can end up in server logs; use a short token lifetime for anything real.
- A sleeping free service drops the live connection; the bell shows a grey dot until the page reconnects.
- Locally you do not need any of this: `docker compose up --build` serves the UI on http://localhost:3000.
