# Nasazení na Oracle Cloud Always Free (ARM, PostgreSQL)

Návod pro provoz BotManageru zdarma na Oracle Cloud Always Free VM (Ampere A1, ARM64).
SQL Server na ARM neběží, proto se používá PostgreSQL (`docker-compose.postgres.yml`).

> Podmínky free tieru se mění – ověř si aktuální limity na https://www.oracle.com/cloud/free/.
> Od června 2026 je limit Ampere A1 pro čistě free účty 2 OCPU / 12 GB RAM, což aplikaci bohatě stačí.

## 1. Účet a VM

1. Založ účet na https://www.oracle.com/cloud/free/ (vyžaduje kartu kvůli ověření, free zdroje se neúčtují).
   Domovský region zvol pečlivě – Always Free zdroje jsou jen v něm.
2. **Compute → Instances → Create instance**
   - Image: **Canonical Ubuntu 24.04** (aarch64)
   - Shape: **VM.Standard.A1.Flex**, 2 OCPU, 12 GB RAM (případně 1 OCPU / 6 GB)
   - Boot volume: 50–100 GB (Always Free obsahuje 200 GB block storage celkem)
   - Networking: veřejná IPv4 adresa, vlož svůj SSH public key
   - **Advanced options → Management → Cloud-init script**: vlož obsah [`cloud-init.yaml`](cloud-init.yaml)
     (nainstaluje Docker + Compose, otevře porty 80/443 v iptables, zapne automatické bezpečnostní aktualizace)
3. Pokud hlásí *Out of capacity*, zkus jinou availability domain nebo později.
4. **Networking → Virtual Cloud Networks → (tvoje VCN) → Security Lists → Default** přidej Ingress pravidla:
   TCP 80 a TCP 443 ze `0.0.0.0/0`.

> **Pozor – reclaim nečinných instancí:** Oracle může Always Free instanci odebrat, pokud je 7 dní „nečinná“
> (nízké vytížení CPU, sítě i paměti). Bot má malou zátěž. Nejjistější ochrana je převést účet na
> *Pay As You Go* (Always Free zdroje zůstávají zdarma, idle reclaim se na PAYG nevztahuje) – nastav si
> v konzoli rozpočet s upozorněním (Budgets) na pár korun, ať tě nic nepřekvapí.

## 2. Doména

Let's Encrypt potřebuje doménu. Zdarma např. subdoména z [DuckDNS](https://www.duckdns.org/) nebo vlastní doména.
Nastav **A záznam** na veřejnou IP VM.

## 3. Instalace aplikace

```bash
ssh ubuntu@<verejna-ip>
sudo mkdir -p /opt/dc-bots-manager && sudo chown ubuntu: /opt/dc-bots-manager
git clone https://github.com/heggy98/dc-bots-manager /opt/dc-bots-manager   # privátní repo: přihlášení / deploy key
cd /opt/dc-bots-manager
cp .env.example .env
```

Vyplň `.env`:

| Proměnná | Hodnota |
|---|---|
| `DB_PASSWORD` | silné heslo k PostgreSQL (bez `;`), např. `openssl rand -base64 24` |
| `JWT_SECRET` | `openssl rand -base64 48` |
| `ADMIN_EMAIL` | tvůj e-mail |
| `ADMIN_PASSWORD_HASH` | BCrypt hash v jednoduchých uvozovkách (viz níže) |
| `DOMAIN`, `ACME_EMAIL` | doména a e-mail pro Let's Encrypt |
| `AUTH_COOKIE_SECURE` | `true` (HTTPS) |

Hash hesla vygeneruj na svém počítači (`dotnet run --project BotManager.Backend.API -- --hash-password "<heslo>"`)
nebo přímo na VM přes Docker:

```bash
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet run --project BotManager.Backend.API --no-launch-profile -- --hash-password "<heslo>" | tail -n1
```

Spuštění (PostgreSQL + HTTPS přes Caddy):

```bash
docker compose -f docker-compose.yml -f docker-compose.postgres.yml -f docker-compose.https.yml up -d --build
docker compose -f docker-compose.yml -f docker-compose.postgres.yml -f docker-compose.https.yml ps
```

Aby nebylo nutné psát `-f` pokaždé, přidej do `.env`:

```
COMPOSE_FILE=docker-compose.yml:docker-compose.postgres.yml:docker-compose.https.yml
```

Aplikace poběží na `https://<DOMAIN>`. Boti označení „auto-start“ se po restartu serveru spustí sami.

Volitelně monitoring (Prometheus + Grafana na `127.0.0.1:3000`, přístup přes SSH tunel
`ssh -L 3000:localhost:3000 ubuntu@<ip>`): přidej `--profile monitoring` a nastav `GRAFANA_ADMIN_PASSWORD`.

## 4. Zálohy

```bash
crontab -e
# denně ve 3:15 UTC, uchovává 14 dní
15 3 * * * /opt/dc-bots-manager/deploy/oracle/backup.sh >> /home/ubuntu/botmanager-backup.log 2>&1
```

Obnova: `gunzip -c zaloha.sql.gz | docker compose exec -T db psql -U botmanager -d botmanager`.
Pro off-site kopii lze použít OCI Object Storage (Always Free 20 GB, viz komentář v `backup.sh`).

## 5. Aktualizace

```bash
cd /opt/dc-bots-manager
git pull
docker compose up -d --build     # s COMPOSE_FILE v .env
docker image prune -f
```

Migrace databáze se aplikují automaticky při startu API.

## 6. Data Protection klíče

Tokeny botů jsou šifrované klíči uloženými v databázi. Na Linuxu nejsou tyto klíče samy o sobě šifrované – pro vyšší
bezpečnost vytvoř certifikát a nastav `DATA_PROTECTION_CERT_PATH` / `DATA_PROTECTION_CERT_PASSWORD`
(soubor připoj do kontejneru `api` jako volume). Při ztrátě databáze i záloh je potřeba tokeny botů zadat znovu.
