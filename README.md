# ChoresWizard
A simple app to be able to divvy up and sort chores for kids.

## Local development

Configure the Web Awesome Pro kit through user secrets or an environment variable before starting the app. The public kit identifier is intentionally not committed:

```bash
export WebAwesome__KitCode="<your-kit-code>"
dotnet run
```

## Chore export (Skylight)

Each month's assigned chores can be exported for a [Skylight](https://www.skylightframe.com/)
family display in three formats (issue #9). All three cover the **full 1st → last
day of the month**, derived from the calendar month itself, not the day the sort ran.

| Format | Endpoint | Auth |
| --- | --- | --- |
| **ICS feed** (primary) | `GET /feed/{token}/chores.ics` | Anonymous, token in URL |
| **PDF** (print/fridge) | `GET /Export/Pdf` | Signed-in user |
| **CSV** (fallback) | `GET /Export/Csv` | Signed-in user |

The PDF and CSV default to the current household month; add `?year=2026&month=1`
to export a specific month.

### Skylight subscription (ICS feed)

Skylight has no public API; the supported ingest path is subscribing to an
anonymous ICS URL. The feed produces **calendar events** (not native checkable
Skylight "Chores"). The assignee and cadence are encoded in each event's title,
e.g. `[ALEX] [DAILY] Feed dog`. Daily chores recur every day; weekly chores recur
every Saturday, each until the month's last day.

Because Skylight cannot authenticate, the feed is protected **only** by an
unguessable token in the route. Configure it out of band — never commit a real value:

```bash
# Development
dotnet user-secrets set "Export:FeedToken" "<long-random-value>"
```

In production (Azure App Service) set an application setting named
`Export__FeedToken`. While the token is empty the feed is **disabled** and returns
`404`. Rotate by changing the setting; subscribers must update their URL.

Then subscribe Skylight to:

```
https://chores.zazzo.com/feed/<your-token>/chores.ics
```

### PDF (secondary path)

The PDF is a printable chore chart that clearly separates each person's **Daily**
and **Weekly (Saturdays)** chores and states the full month span in its header. With
Skylight Calendar Plus it can also be emailed to Sidekick AI, though that ingest is
best-effort, not guaranteed.

