# SAMA Training Center System — Phase One

Web-based system for SAMA Organization to manage training workshops, public student registration, workshop expenses, and staff users.

Built with **ASP.NET Core 8 MVC**, **Entity Framework Core**, **SQL Server**, **ASP.NET Core Identity**, and the **Materio** Bootstrap 5 admin theme (free edition, recolored green).

## Phase One scope

| Module | What it covers | User stories |
| --- | --- | --- |
| Workshop Settings | Expense categories and halls/locations (CRUD, deactivate instead of delete when in use) | US-01, US-02 |
| Workshop Management | Create, edit, search/filter and delete workshops; online or on-site | US-03 – US-07 |
| Status & Registration | Pending → Ongoing → Completed lifecycle with history; open/close registration; public link | US-08 – US-10 |
| Public registration | Bilingual Arabic/English page (RTL for Arabic), validation, capacity, duplicate-email check | US-11 – US-13 |
| Expense Management | Add/edit/delete expenses once a workshop is Ongoing; totals per workshop in the workshop's currency | US-14 – US-16 |
| Countries & currencies | Each workshop runs in a country; its money is in that country's currency (Sudan → SDG, Egypt → EGP) | — |
| Reports | Totals per currency, seats filled, cost per student, expenses by category, registrations by month, Excel (CSV) export | — |
| User Management | Users, roles, editable role permissions, lockout, forced password change | US-17 – US-20 |

## Backlog v2 (US-01 – US-19)

- **Settings:** halls/locations and expense categories have a required Arabic name (halls also an Arabic location), full-width edit screens, and a Delete button on the edit screen that is blocked while the record is in use.
- **Workshops:** rich-text agenda (headings, bold, lists, colours, links; unsafe HTML is stripped), single-day or date-range schedule, registration fee per participant in the workshop's currency (empty/0 = free).
- **History:** status changes (with previous/new status, user, time), registration opened/closed and new links are all recorded.
- **Status:** forward Pending → Ongoing → Completed; users with *Reverse a workshop status* can undo one step (Ongoing → Pending, Completed → Ongoing).
- **Expenses & payments:** can be managed from creation until the workshop is completed; after that they are read-only. Fee payments (collections) are recorded per participant with method and reference; each participant shows Paid / Partially paid / Unpaid.
- **Finance (dashboard, workshop page, reports, CSV):** Expected income = fee × participants · Collected = payments · Outstanding = expected − collected · Net profit = collected − expenses. Always per currency.
- **Registration page:** dates, type, hall and location, fee and currency in the selected language; seats left are hidden; capacity is enforced with a row lock so concurrent submissions can't overbook; a full workshop shows an apology in the applicant's language.
- **Forgot password:** e-mailed reset link (expires after `App:PasswordResetMinutes`, default 60). Needs SMTP settings in `private/appsettings.Production.json`:

```json
"Smtp": { "Host": "smtp.example.com", "Port": 587, "EnableSsl": true, "UserName": "...", "Password": "...", "FromAddress": "no-reply@example.com" }
```

  Without SMTP the page says to ask the administrator, who can still reset passwords from *Users*.

## Project structure

```
SamaTraining.sln
src/SamaTraining.Web/
  Controllers/      Account, Dashboard, Workshops, Expenses, PublicRegistration,
                    ExpenseCategories, Halls, Users, Roles
  Data/             Entities, ApplicationDbContext, DbSeeder, (Migrations)
  Models/           View models and form models
  Security/         Permissions, role defaults, permission policies, sign-in rules
  Services/         WorkshopService (business rules), PublicText (AR/EN text), AppSettings
  Views/            Razor views (Materio layout in Views/Shared/_Layout.cshtml)
  wwwroot/assets/   Materio theme (green), wwwroot/css/site.css for custom styles
tests/SamaTraining.Tests/
  WorkshopServiceTests.cs   business rules
  WebFlowTests.cs           end-to-end HTTP tests (login, permissions, full workshop lifecycle)
```

## Run it locally (Visual Studio 2022)

1. Install **Visual Studio 2022** (ASP.NET and web development workload) and **SQL Server LocalDB** (included with Visual Studio) or SQL Server Express.
2. Open `SamaTraining.sln`.
3. The default connection string in `appsettings.json` uses LocalDB (`(localdb)\MSSQLLocalDB`). Change it if you use another SQL Server.
4. Press **F5**. On first start the app creates the database, the default roles, starter lookup values and the first administrator.
5. Sign in as `admin`. No password is stored in the code: on first start a temporary one is generated into
   `src/private/first-admin-password.txt` (git-ignored). You will be asked to set your own password immediately.

## Security

- **Secrets stay on the server.** The connection string, admin password and SMTP settings live in
  `<site root>/private/` (outside the website and git-ignored). Nothing secret is in GitHub or sent to the browser.
- **Every request is checked on the server**: sign-in, permission per action, and row checks (a payment, expense
  or registration can only be changed through the workshop it belongs to). The browser never talks to the database.
- **Validation on the server** for every form (lengths, ranges, formats), anti-forgery tokens on every post,
  rich text sanitised, CSV cells protected against formula injection.
- **Rate limits per IP** (all pages, sign-in, password reset, public registration form, exports) with HTTP 429,
  plus account lockout after 5 wrong passwords and an invisible robot check on the public form.
- **Paged lists** with cursors: no screen loads a whole table.
- **Browser protection headers**: Content-Security-Policy, HSTS, no framing, no sniffing, no indexing.
- Limits can be tuned in `private/appsettings.Production.json` under `"RateLimit"` (`PerMinute`, `AuthPer5Minutes`,
  `PublicFormPer10Minutes`, `ExportPerMinute`, `MinFormSeconds`). Defaults: 600 requests a minute, 20 sign-in attempts per 5 minutes, 30 registrations per 10 minutes, 10 exports a minute.

## Default roles

| Role | Default permissions |
| --- | --- |
| Administrator | Everything. This role can't lose permissions or be deleted. |
| Workshop Coordinator | Dashboard, view/create/edit workshops, change status, open/close registration, view students, view expenses |
| Finance Officer | Dashboard, view workshops, view and manage expenses |

Administrators can change any role's permissions, or add new roles, from **Administration → Roles & Permissions**. Changes apply to signed-in users within about a minute.

## Configuration

All settings are in `appsettings.json` and can be overridden by environment variables (use `__` for nesting, e.g. `SeedAdmin__Password`).

| Setting | Purpose | Default |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | SQL Server connection | LocalDB |
| `App:OrganizationName`, `App:SystemName` | Names shown in the UI | SAMA Organization / SAMA Training Center |
| `App:Countries` | Countries workshops can run in and each one's currency. Add a country here to support it. | Sudan (SDG), Egypt (EGP) |
| `App:SessionTimeoutMinutes` | Sign-out after inactivity | `30` |
| `Database:Mode` | `Migrate` (apply migrations at start-up), `EnsureCreated`, or `None` | `Migrate` |
| `SeedAdmin:UserName` / `Email` / `FullName` | First administrator account | `admin` |
| `SeedAdmin:Password` | First administrator's temporary password. Only used when no users exist. | empty in production |

## Currencies

A workshop's country decides its currency, and every expense on that workshop is in that currency. Amounts in different currencies are never added together: the dashboard and reports show one total per currency. Once a workshop has expenses, its country can't be changed.

## Arabic and English workshop names

Each workshop has an English name (required) and an Arabic name (required), plus optional Arabic description and agenda. The Arabic registration page shows the Arabic text and the English page shows the English text; when an Arabic description or agenda is empty, the English one is shown.

## Phone numbers and duplicate applicants

- The registration page shows a fixed **00** that students can't change. They type the country code and then the number, without the first 0 (e.g. `249912345678`).
- The number is checked by **country code + exact length** (Sudan 249 + 9 digits, Egypt 20 + 10 digits, plus Gulf and neighbouring countries). Students see a specific message for each mistake and a live hint while typing. A pasted `+249…` or `00249…` is accepted and cleaned.
- Numbers are stored as digits only (`249912345678`) and shown to staff as `00249 912345678`.
- **The same phone number can register only once per workshop**, even with a different email (the same applies to the email). The database enforces this with unique indexes.
- In a workshop's Students tab, a **Returning** badge marks students whose phone number registered for other workshops before.
- Accepted countries and lengths can be changed in `appsettings.json` under `App:PhoneCountries` (`DialCode`, `Name`, `NameAr`, `Digits`, `Example`).

## Feedback in the interface

- Every add, update and delete ends with a toast message on the page you land on, and the affected row is briefly highlighted.
- Deletes and other irreversible actions ask for confirmation in a dialog.
- Buttons show a spinner while saving, so a double click can't save twice.
- Forms that come back with errors show a "Not saved yet" toast and highlight the fields to fix.

## Security rules

- Passwords: at least 8 characters, with upper and lower case letters, a number and a symbol.
- New users and reset passwords are temporary: the user must change them at next login.
- 5 failed sign-ins lock the account for 15 minutes; an administrator can unlock it.
- Deactivated users cannot sign in and are signed out within a minute.
- Every page requires sign-in except the login page and public registration links.
- All forms are protected against cross-site request forgery.
- The public link is a random 32-character token. "Generate a new link" invalidates the old one.

## Deploying to the server (Windows Server 2016 + IIS)

1. On the server, install the **ASP.NET Core 8.0 Hosting Bundle** (from Microsoft's .NET download page) and restart IIS (`iisreset`).
2. Create the SQL Server database login the app will use (it needs permission to create tables on first run).
3. Publish the app: in Visual Studio right-click the project → **Publish** → Folder, or download the `sama-training-web` package from the latest GitHub Actions run.
4. Copy the published folder to the server, e.g. `C:\inetpub\sama-training`.
5. In IIS: create an Application Pool with **.NET CLR version: No Managed Code**, then a website pointing to the folder. Bind your domain and an HTTPS certificate.
6. Set the production settings as **environment variables** on the application pool or in `web.config` (never commit them):
   - `ConnectionStrings__DefaultConnection`
   - `SeedAdmin__Password` — a strong temporary password for the first admin
   - `ASPNETCORE_ENVIRONMENT=Production`
7. Browse to the site, sign in as `admin` with the temporary password, and set a new one. Remove `SeedAdmin__Password` afterwards.

## Hosting on MonsterASP.NET (live: https://samatraining.runasp.net)

- **Website:** `site109722` (free plan, EU datacenter), address `samatraining.runasp.net`. DNS is handled by MonsterASP.
- **Database:** SQL Server `db81602` (shown as *SamaTraining* in the control panel), collation `Arabic_CI_AS`, all text columns `nvarchar`, so Arabic is stored, sorted and searched correctly.
- **Deploy:** MonsterASP control panel → Website → *Git* → *Build .NET app* from this repository's `main` branch, then *Build & publish*.
- **Secrets are not in GitHub.** The app reads `private/appsettings.Production.json` from the site root (MonsterASP's built-in private folder, not reachable from the web), one level above `wwwroot`, so redeploys never overwrite it:

```json
{
  "ConnectionStrings": { "DefaultConnection": "<connection string from the database page>" },
  "SeedAdmin": { "Password": "<temporary admin password>" }
}
```

  Login cookie keys are kept in `private/keys` so users stay signed in across restarts. After the first sign-in as `admin`, remove the `SeedAdmin` block.

## Database migrations

The schema is created with EF Core migrations under `src/SamaTraining.Web/Data/Migrations`. When you change an entity:

```
dotnet ef migrations add <Name> --project src/SamaTraining.Web --output-dir Data/Migrations
```

(or `Add-Migration <Name>` in the Visual Studio Package Manager Console). Migrations are applied automatically at start-up when `Database:Mode` is `Migrate`.

## Tests

```
dotnet test
```

The tests run the real application against an in-memory SQLite database. GitHub Actions runs them on every push and pull request.

## Theme credit

UI based on [Materio — Bootstrap 5 Admin Template (free)](https://github.com/themeselection/materio-bootstrap-html-admin-template-free) by ThemeSelection, MIT License (see `src/SamaTraining.Web/wwwroot/assets/MATERIO-LICENSE.txt`). Primary color changed from purple to green.
