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
5. Sign in with the development administrator:
   - Username: `admin`
   - Password: `Sama@Dev2026!` (from `appsettings.Development.json`, development only)
   - You will be asked to set your own password immediately.

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
