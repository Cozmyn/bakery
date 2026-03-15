# Bakery Line — ETAPA 6 (FINAL) • Industrial Platform (2026 UX)

Acest ZIP este livrabilul **FINAL (ETAPA 6/6)** pe stack-ul S1:
- Backend: **.NET 8 Web API**
- DB: **PostgreSQL** (raw events păstrate)
- Cache (VIS images TTL): **Redis (RAM only, no persistence)**
- Frontend: **React + TypeScript (Vite)**

## Ce include (end-to-end)
- Login + RBAC (Admin / Operator)
- Admin: Products + Segments + Tolerances (incl. weight) + Densities + Recipes/Ingredients + publish gate
- Live Cockpit: P1 / P2 / VIS (defects cu imagini LIVE TTL) / P3 + Quick Actions (minimal typing)
- Multi-batch per run: MixedAt + AddedToLineAt + proofing actual minutes
- Mix Sheet (batch): operator vede rețeta standard, poate ajusta gramaje/adăuga ingrediente; total mixer kg vs standard
- Discard batch (mix aruncat) -> Waste event (MIX_SCRAP) cu D8/D9/D10
- Changeover + End production (Draining WIP) + Downtime/Belt-empty mandatory prompts
- Analytics: heatmap 10-min buckets pe **20 rânduri** (P1 vs P2 + VIS defects + P3 post-freeze)
- OEE per segment (Availability/Performance/Quality) + waterfall losses + monetizare speed loss
- Reports:
  - list runs + run report + CSV export (bucketed OEE)
  - **Trends** pe perioade (shift/day/week) overlap-aware (eveniment-time)
  - **Pareto** downtime (minute + cost) + pareto defecte (cu avg speed seg3)
  - **Explain why** (timeline drill-down: defecte, speed, stops, P2 growth delta, operator events)
  - **Batch & recipe insights** (proofing compliance + recipe variance + calibration stability)
  - **Changeover performance** report
- **Alert Center persistent** (severity/ACK/snooze) + spike detection (defect spike, P2 growth drift)
- Defect taxonomy extins (ars/necopt/fara scoring/deformata/crapata/etc.) ca master data + endpoints Admin pentru editare
- UX heavy-use (2026): **Ctrl+K** quick nav, **F1** downtime, **F2** batch, **Kiosk mode**, skeleton loaders
- Simulator (single instance)

## IMPORTANT (conform cerințelor tale)
- **NU stocăm imagini**: sunt doar în Redis cu TTL (default 120s). DB reține doar metadata + token.
- **NU ștergem date din DB**: evenimentele rămân în tabelele raw (pentru rapoarte ulterioare).
- Timp intern: UTC. UI afișează local (browser).

## DB partitioning (industrial, calendar-quarter)
Acest proiect pornește **direct** cu event tables partitioned în PostgreSQL, pe **trimestre calendaristice** (Ian–Mar, Apr–Iun, Iul–Sep, Oct–Dec).

- Scripturile din `db/init/` creează schema completă + tabelele de events ca `PARTITION BY RANGE (TsUtc)`.
- La inițializare se creează:
  - partiție `*_default` (safety net)
  - trimestrul anterior + următoarele **12 trimestre** (~3 ani)

Aplicația rulează și un **DbMigrationService** la startup (best-effort) care poate crea trimestrul următor dacă DB-ul e deja partitioned.

**IMPORTANT:** scripturile din `db/init/` rulează doar la prima inițializare a volumului Postgres.
Dacă ai rulat proiectul înainte, pornește de la 0 cu:

```bash
docker compose down -v
docker compose up --build
```

---

## Rulare pe Windows 11 (Docker Desktop)

### 1) Prerechizite
- Docker Desktop (cu WSL2)

### 2) Start
În folderul proiectului:
```bash
copy .env.example .env
# editează .env și pune un JWT_SECRET de minim 32 caractere

docker compose up --build
```

### 3) Acces
- Frontend: http://localhost:5173
- Swagger (API): http://localhost:8080/swagger

### 4) Useri seed (DEV)
- Admin: `admin@local` / `Admin123!`
- Operator: `operator@local` / `Operator123!`

