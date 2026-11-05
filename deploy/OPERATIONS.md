# HireGameDev Operations & Multi-Domain Deployment Runbook (`M0.7` / `ADR-012`)

## 1. Three-Domain Topology
All three portals run cleanly from a single ASP.NET Core 8 deployment (`Portals:DeploymentMode = "All"`) behind Nginx, and can later be split into dedicated deployments by setting `Portals:DeploymentMode` to `Landing`, `Jobs`, or `Studio` per instance with zero controller rewrites:

| Host | Portal Role | Key Routes |
|---|---|---|
| `hiregamedev.com` | Brand Landing, Legal & Admin | `/`, `/Legal/Terms`, `/Legal/Privacy`, `/Legal/Grievance`, `/Admin` |
| `www.hiregamedev.com` | Canonical redirect | `301` -> `https://hiregamedev.com$request_uri` |
| `jobs.hiregamedev.com` | Candidate & Public Job Board | `/`, `/jobs/{slug}`, `/studios/{slug}`, Candidate Auth (`Google` + `GitHub` + Email) |
| `studio.hiregamedev.com` | Studio Hiring & Verification | `/`, Studio Auth (`Google` + Email; GitHub disabled), Studio onboarding/ATS |

### Local Multi-Subdomain Testing
`*.lvh.me` resolves to `127.0.0.1` out of the box without editing `/etc/hosts` (or use `jobs.localhost` / `studio.localhost` on modern browsers):
- Landing: `https://lvh.me:7196` (or `http://lvh.me:5142`)
- Jobs: `https://jobs.lvh.me:7196`
- Studio: `https://studio.lvh.me:7196`

## 2. OAuth Callback URLs to Register
Register exact HTTPS callback URIs per subdomain in Google Cloud Console and GitHub OAuth Apps:
- **Google OAuth** (Candidates + Studios):
  - Production: `https://jobs.hiregamedev.com/signin-google`, `https://studio.hiregamedev.com/signin-google`
  - Local Dev: `https://localhost:7196/signin-google`, `https://jobs.lvh.me:7196/signin-google`, `https://studio.lvh.me:7196/signin-google`
- **GitHub OAuth** (Candidates only — `jobs.`):
  - Production: `https://jobs.hiregamedev.com/signin-github`
  - Local Dev: `https://jobs.lvh.me:7196/signin-github` (or `https://localhost:7196/signin-github` in single-host mode)

## 3. Initial VPS Setup (Ubuntu 24.04 LTS + PostgreSQL 16 + Nginx)
1. **DNS**: Point `A` / `AAAA` records for `@`, `www`, `jobs`, and `studio` to the VPS public IP.
2. **System user & directories**:
   ```bash
   sudo useradd --system --home-dir /opt/hiregamedev --shell /usr/sbin/nologin hiregamedev
   sudo mkdir -p /opt/hiregamedev/releases /var/lib/hiregamedev/keys /var/log/hiregamedev /etc/hiregamedev /var/www/letsencrypt
   sudo chown -R hiregamedev:hiregamedev /var/lib/hiregamedev/keys /var/log/hiregamedev
   sudo chmod 0700 /var/lib/hiregamedev/keys
   ```
3. **TLS Certificate (Let's Encrypt multi-SAN)**:
   ```bash
   sudo certbot certonly --webroot -w /var/www/letsencrypt \
     -d hiregamedev.com -d www.hiregamedev.com \
     -d jobs.hiregamedev.com -d studio.hiregamedev.com
   ```
4. **Nginx & systemd**:
   - Copy `deploy/nginx/hiregamedev.conf` to `/etc/nginx/sites-available/hiregamedev.conf`, symlink into `sites-enabled`, run `sudo nginx -t && sudo systemctl reload nginx`.
   - Copy `deploy/systemd/hiregamedev.env.example` to `/etc/hiregamedev/hiregamedev.env`, populate secrets (`chmod 0600`), install `deploy/systemd/hiregamedev-web.service` into `/etc/systemd/system/`, and run `sudo systemctl daemon-reload && sudo systemctl enable hiregamedev-web`.

## 4. Backups & Restore Drills
- **Daily Cron (`/etc/cron.d/hiregamedev-backup`)**:
  ```cron
  15 21 * * * root RCLONE_REMOTE="r2:hiregamedev-backups/postgres" /opt/hiregamedev/scripts/backup-db.sh >> /var/log/hiregamedev/backup.log 2>&1
  ```
  *(21:15 UTC = 02:45 IST off-peak)*
- **Restore Verification Drill** (run before public launch and at least monthly):
  ```bash
  sudo /opt/hiregamedev/scripts/restore-test.sh /var/backups/hiregamedev/hiregamedev-YYYYMMDDTHHMMSSZ.dump.gpg
  ```

## 5. Activating GitHub Actions CI (`ci.yml`)
GitHub restricts third-party GitHub Apps without `workflows` scope from pushing directly under `.github/workflows/`. Activate the workflow from your local checkout with one command:
```bash
mkdir -p .github/workflows && cp deploy/github-actions/ci.yml .github/workflows/ci.yml
git add .github/workflows/ci.yml && git commit -m "chore(ci): enable GitHub Actions build and efbundle workflow" && git push
```

