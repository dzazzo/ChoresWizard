# Azure setup — identities, secrets, and SQL access

This document is the source of truth for the Azure Active Directory / Entra ID
identities the app depends on. Three **distinct** identities are involved and
they are easy to conflate. Getting them mixed up is what produced the
mismatched, non-functional connection string described in issue #12.

Tenant `zazzocom.onmicrosoft.com` = `38a6a66c-ff86-49a1-9072-856d3054160a`
Subscription `Visual Studio Ultimate with MSDN` = `746602ae-0b05-43ee-8fa8-1997597a44ee`
Resource group `chores-app`, region `westus3`.

---

## The three identities

| # | Identity | Type | Used by | Purpose |
|---|----------|------|---------|---------|
| 1 | `oidc-msi-a9f6` | User-assigned managed identity | GitHub Actions | Deploys the app (and infra) via OIDC. No secret. |
| 2 | `bc5c77d1-77a7-4b78-9547-12d3b4c93746` | App registration | `Microsoft.Identity.Web` | User sign-in (interactive OIDC). |
| 3 | `zazzo-chores`'s system-assigned MI (`c1fded72-0c76-4252-aa1a-8dd4baf137f6`) | System-assigned managed identity | The running App Service | Passwordless auth to Azure SQL. |

These never overlap. #1 authenticates the *pipeline*, #2 authenticates *end
users*, #3 authenticates the *app to its database*. None of them share
credentials, and none of them use a client secret or a SQL password.

---

## 1. Deployment identity (GitHub Actions → Azure, OIDC)

A user-assigned managed identity `oidc-msi-a9f6` with a **federated credential**
so GitHub Actions can log in with a short-lived OIDC token — no stored secret.

- clientId: `8f6dba71-13ff-4183-907d-3c2fa4835eeb`
- principalId: `8ca54afc-a049-46fc-a102-06ee82e03bfd`
- Role: **Website Contributor**, scoped to the `zazzo-chores` site.

> **RBAC scope tradeoff (affects `infra.yml`).** This identity holds *only*
> Website Contributor on the single site — enough for the app deploy pipeline
> and the `az webapp config set` runtime pin. It is **not** scoped to the
> `chores-app` resource group, so the manual `infra.yml` workflow will fail on
> first run: a resource-group `what-if` needs at least **Reader on `chores-app`**,
> and an actual `deploy` needs **Contributor** (or narrower write roles) there.
> This is left as a deliberate operator decision rather than widened in the
> template: granting resource-group Contributor to a GitHub-triggered identity
> meaningfully expands its blast radius beyond one web app. The conservative
> default is Reader (what-if works, apply does not); grant write scope only when
> you intend to apply infra from CI. Example (operator runs this):
> ```bash
> # Reader is enough for what-if:
> az role assignment create \
>   --assignee 8ca54afc-a049-46fc-a102-06ee82e03bfd \
>   --role Reader \
>   --scope /subscriptions/746602ae-0b05-43ee-8fa8-1997597a44ee/resourceGroups/chores-app
> ```

### Federated credentials

| Subject | Needed for |
|---------|-----------|
| `repo:dzazzo/ChoresWizard:ref:refs/heads/main` | Already exists (`oidc-credential-9906`). |
| `repo:dzazzo/ChoresWizard:environment:production` | **Applied** as `gh-env-production`. The deploy + infra jobs run in the `production` GitHub Environment, which changes the OIDC subject to the environment form. |

Reproduce / add the environment credential:

```bash
az identity federated-credential create \
  --name oidc-credential-production \
  --identity-name oidc-msi-a9f6 \
  --resource-group chores-app \
  --issuer https://token.actions.githubusercontent.com \
  --subject "repo:dzazzo/ChoresWizard:environment:production" \
  --audiences api://AzureADTokenExchange
```

> The build+test job does not touch Azure, so it needs no federated
> credential. Only the `deploy` and `infra` jobs (both in the `production`
> environment) do.

### GitHub secrets consumed by the workflow

The workflow uses clearly-named secrets (replacing the opaque auto-generated
`AZUREAPPSERVICE_*` names):

| Secret | Value |
|--------|-------|
| `AZURE_CLIENT_ID` | `8f6dba71-13ff-4183-907d-3c2fa4835eeb` |
| `AZURE_TENANT_ID` | `38a6a66c-ff86-49a1-9072-856d3054160a` |
| `AZURE_SUBSCRIPTION_ID` | `746602ae-0b05-43ee-8fa8-1997597a44ee` |

`AZURE_WEBAPP_PUBLISH_PROFILE` and the old opaque secrets must be **deleted**
(see the operator change list in the PR).

---

## 2. Application sign-in identity (user login)

App registration `bc5c77d1-77a7-4b78-9547-12d3b4c93746`, wired up in
`appsettings.json` under `AzureAd` and consumed by `Microsoft.Identity.Web`.

- Redirect URI: `https://chores.zazzo.com/signin-oidc`
- Front-channel logout URL: `https://chores.zazzo.com/signout-callback-oidc`
- No client secret is required for the current interactive sign-in flow.

Verify the redirect URI:

```bash
az ad app show --id bc5c77d1-77a7-4b78-9547-12d3b4c93746 \
  --query "web.redirectUris" -o json
```

If the production redirect URI is missing:

```bash
az ad app update --id bc5c77d1-77a7-4b78-9547-12d3b4c93746 \
  --web-redirect-uris "https://chores.zazzo.com/signin-oidc"
```

---

## 3. Database identity (App Service MI → Azure SQL)

The app authenticates to Azure SQL **passwordless** using the App Service
system-assigned managed identity. This is why the reconciled bicep's
connection string uses `Authentication=Active Directory Default` and contains
**no password**, and why `AZURE_SQL_CONNECTIONSTRING` (SQL auth, with a
password) is being retired.

For this to work the managed identity must be created as a **contained
database user** in the application database and granted roles. This is a
one-time SQL step that only an Entra ID admin of the SQL server can run — the
current AAD admin is the human account `dzazzo@zazzo.com`.

### Grant the App Service MI access

> **Status: already applied in the live database.** As of today the
> system-assigned MI is a working DB user — the app's EF Core migration ran
> successfully against Azure SQL over managed identity. The steps below are
> **reproduction steps for a rebuild**, not an outstanding action.

Connect to the database as the AAD admin (e.g. via `sqlcmd -G`,
Azure Data Studio, or the portal query editor), targeting the
`zazzo-chores-database` database, and run:

```sql
-- Run against zazzo-chores-database (NOT master).
-- The name must match the App Service's identity display name.
CREATE USER [zazzo-chores] FROM EXTERNAL PROVIDER;

ALTER ROLE db_datareader ADD MEMBER [zazzo-chores];
ALTER ROLE db_datawriter ADD MEMBER [zazzo-chores];
-- Required because Program.cs runs EF Core migrations on startup
-- (context.Database.Migrate()), which creates/alters schema.
ALTER ROLE db_ddladmin   ADD MEMBER [zazzo-chores];
```

> `[zazzo-chores]` is the web app's resource name, which is also its
> system-assigned identity name. If you ever recreate the app and the identity
> name changes, drop and recreate the user to match.

Verify afterwards:

```sql
SELECT dp.name, dp.type_desc, r.name AS role_name
FROM sys.database_principals dp
LEFT JOIN sys.database_role_members drm ON drm.member_principal_id = dp.principal_id
LEFT JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id
WHERE dp.name = 'zazzo-chores';
```

---

## Networking posture (SQL)

Production currently has a **contradictory** posture: a private endpoint
*and* `publicNetworkAccess=Enabled` *and* a `0.0.0.0` "allow all Azure IPs"
firewall rule. The reconciled bicep resolves this to **private endpoint only**
(`sqlPublicNetworkAccess=Disabled`, no `0.0.0.0` rule). The app reaches SQL
over its VNet integration + the `privatelink.database.windows.net` private DNS
zone.

Do not flip public access to `Disabled` until you have confirmed the app can
resolve and reach the private endpoint (VNet integration is already in place;
`vnetRouteAllEnabled` is set true in the template). Validate with a
`what-if` and a post-change smoke test.
---

## Always On, health check, and the serverless-DB keep-alive constraint

The plan is **B1 Basic**, which supports both **Always On** and **Health Check**
(the committed template previously assumed Free F1, which supports neither — that
stale assumption is corrected in the reconciled bicep).

- `alwaysOn: true` keeps the app warm so cold starts don't 500.
- `healthCheckPath: '/healthz'` points Azure's health probe at the **DB-free**
  endpoint. `/healthz` returns 200 without touching SQL; `/readyz` and `/` are
  *not* used for the probe because they can hit the database.

**Why this matters for cost:** the database is `GP_S_Gen5` **serverless** with a
60-minute auto-pause. Any keep-alive that touches SQL every few minutes would
prevent auto-pause and bill continuously. The health probe is safe because
`/healthz` is DB-free.

**Constraint to preserve:** Always On itself also issues a periodic keep-alive
request to the site **root `/`** (this is Azure behavior and is not
configurable). Today `/` is DB-free *only incidentally* — the auth middleware
issues a 302 to the sign-in flow for unauthenticated requests **before**
`HomeController.Index` (which touches the DB) runs. If `/` is ever made
anonymous, the Always On keep-alive would begin pinning the serverless database
awake continuously and it would never auto-pause. Keep `/` (and `/healthz`)
DB-free, or revisit Always On.
