// Seeds the bots the E2E suite expects, directly via sqlcmd inside the SQL Server container
// (creating bots through the API would need a real, Discord-authorized bot token).
//
// Idempotent: existing E2E bots are left untouched. Run after the API has applied its migrations.
//
// Env:
//   E2E_DB_CONTAINER   docker container running SQL Server (default: botmanager-db-1, the compose db service)
//   E2E_DB_PASSWORD    SA password (falls back to MSSQL_SA_PASSWORD)
//   E2E_DB_NAME        database name (default: BotManagerDb)
//   E2E_ADMIN_EMAIL    owner of the seeded bots (the configured admin email)
import { execFileSync } from 'node:child_process';

import { SEEDED_BOTS } from './seed-data.mjs';

const container = process.env.E2E_DB_CONTAINER ?? 'botmanager-db-1';
const password = process.env.E2E_DB_PASSWORD ?? process.env.MSSQL_SA_PASSWORD;
const database = process.env.E2E_DB_NAME ?? 'BotManagerDb';
const ownerEmail = process.env.E2E_ADMIN_EMAIL;

if (!password || !ownerEmail) {
  console.error('E2E_DB_PASSWORD (or MSSQL_SA_PASSWORD) and E2E_ADMIN_EMAIL must be set.');
  process.exit(1);
}

const sqlString = value => `N'${String(value).replaceAll("'", "''")}'`;

const statements = SEEDED_BOTS.map(bot => `
IF NOT EXISTS (SELECT 1 FROM dbo.Bots WHERE Name = ${sqlString(bot.name)})
  INSERT INTO dbo.Bots (Name, BotToken, OwnerUserId, IsPublic, AutoStart, Status, LastStartedAt, LastStoppedAt)
  VALUES (${sqlString(bot.name)}, ${sqlString(bot.token)}, ${sqlString(ownerEmail)}, ${bot.isPublic ? 1 : 0}, 0, 0, NULL, SYSUTCDATETIME());`);

const query = `SET NOCOUNT ON;${statements.join('')}
SELECT BotId, Name, IsPublic FROM dbo.Bots WHERE Name LIKE N'E2E %' ORDER BY BotId;`;

// The password travels via the SQLCMDPASSWORD env var so it never shows up in the process list.
const output = execFileSync(
  'docker',
  ['exec', '-e', 'SQLCMDPASSWORD', container,
    '/opt/mssql-tools18/bin/sqlcmd', '-S', 'localhost', '-U', 'sa', '-C', '-b', '-d', database, '-Q', query],
  { encoding: 'utf8', env: { ...process.env, SQLCMDPASSWORD: password } });

console.log(output.trim());
