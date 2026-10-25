# LutzPath frontend

A static HTML/CSS/JavaScript app served by an unprivileged Nginx container. There are no production npm dependencies or bundler.

Run Docker Compose from the repository root and open [localhost:3000](http://localhost:3000). For the Google Sheets connection, use the additional Compose file described in [the sync guide](../docs/google-sheets.md).

## Navigation

- Jobs: tracked postings, Date Found ordering, filters, and 25-row pages.
- Companies: searchable directory and company-specific jobs.
- Applications: submitted applications and outcomes.
- Reminders: upcoming/completed lists, editing, and completion.
- Job details: posting, visa, application, preparation, and reminder information.

Clicking a job opens details rather than an edit form. Edit job details changes posting information; Update application changes personal tracking fields. Existing applications cannot move to another job or profile. A job chooser is available only when creating an application.

Company grouping ignores capitalization and repeated spaces, without merging related brands. Filtering, grouping, sorting, and pagination run in the browser over the active profile's tracked jobs. Missing Date Found values sort last.

## State and persistence

Profiles are workspace selectors, not authentication. Jobs, applications, notes, and reminders are saved through the APIs into SQLite. Only navigation preferences are kept in localStorage/sessionStorage. List context returns after visiting job details or refreshing.

Saving a job also creates a Saved application. If that second request fails, the form retains the job ID and retries without duplicating the posting. Failed saves retain form input. Native dialogs support keyboard focus and Escape; the layout supports mobile navigation and reduced motion.

The optional sync panel shows the last cycle and Sync now. Posting data can refresh automatically; existing application fields and reminders stay protected. The UI checks sync status every 30 seconds while visible and refreshes after a completed cycle without interrupting an editor.

## Source files

| File | Responsibility |
| --- | --- |
| public/index.html | Page shell and dialogs |
| public/styles.css | Styles and responsive layout |
| public/app.js | Views, forms, API calls, and navigation |
| public/icons.js | Inline SVG icons |
| nginx.conf | Static serving and API proxy routes |
| Dockerfile | Static frontend container |
| tests/workspace.spec.js | Browser workflows |

Nginx proxies /api/users, /api/jobs, /api/applications, /api/reminders, and /api/integrations to the appropriate backend. Docker DNS is resolved again when a service container is replaced.

After editing frontend files, rebuild the frontend using the same Compose files as your running setup. For the base setup:

~~~powershell
docker compose up --build --detach --no-deps frontend
~~~

## Browser tests

Install Node.js and Microsoft Edge, then run from the repository root:

~~~powershell
npm.cmd ci --prefix frontend
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Frontend.ps1
~~~

Tests use an isolated Docker project and private databases. They cover job/company navigation, ordering, pagination, profile isolation, application protection, reminders, failed saves, mobile/keyboard behavior, and sheet preview/sync status.

Generated screenshots, traces, logs, and results are ignored by Git and can be recreated.
