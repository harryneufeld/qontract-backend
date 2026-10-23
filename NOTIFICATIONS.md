# Recommendations and notifications

A background worker checks every workspace on a schedule and stores findings as
notifications. The app shows them in an inbox; users can read, snooze or dismiss
them. Only the notification kind and its numbers or dates are stored. The app words
the text, so no contract content is copied into notifications.

## Rules

| Kind | Fires when | Default |
|------|-----------|---------|
| `noticeDeadline` | the date by which notice must be sent (end of term minus notice period) is within the lead days. Automatically renewing contracts roll forward by `renewalMonths` until the deadline lies ahead | 30, 14, 3 days |
| `endDate` | an active, non-renewing contract ends within the lead days | 30, 7 days |
| `minimumTerm` | the minimum contract date is within the lead days | 30 days |
| `priceIncrease` | the newest price (effective in the last 60 days) is at least `minPercent` above the previous one in the same billing period | 5 % |
| `insuranceReview` | an insurance contract has not been modified for `afterMonths` | 12 months |
| `missingData` | an active contract older than 7 days has no document or no contractor | off |
| `adminWithout2fa` | a workspace administrator has no two-factor authentication | on |

Draft, expired, terminated, inactive and trashed contracts never produce notifications.
Invoices are not checked because they carry no paid status.

Each finding has a deduplication key (rule, contract, date and lead threshold) that is unique
per user. A reminder is therefore created once per threshold; as the date approaches it escalates
to the next threshold and the older one is marked resolved. When the condition goes away, for
example because the end date changed, the notification is resolved too. Dismissed notifications
stay dismissed. Resolved rows are deleted after 90 days.

## Who is notified

- Tenant contracts: the responsible member. Without one, the workspace administrators
  (`unassignedGoTo`: `"admins"`, or `"nobody"`).
- Personal contracts: the owner.

## Configuration

Server (`appsettings.json` or environment variables):

| Setting | Default |
|---------|---------|
| `Recommendations__Enabled` | `true` |
| `Recommendations__CheckIntervalMinutes` | `360` |
| `Recommendations__DefaultTimeZone` | `Europe/Berlin` (fallback for time zones the server does not know) |

Workspace (administrators), in the `notifications` object of the tenant settings JSON. Every
part is optional and invalid values are rejected when the settings are saved:

```json
{
  "notifications": {
    "timeZone": "Europe/Berlin",
    "unassignedGoTo": "admins",
    "rules": {
      "noticeDeadline":  { "enabled": true, "leadDays": [30, 14, 3] },
      "endDate":         { "enabled": true, "leadDays": [30, 7] },
      "minimumTerm":     { "enabled": true, "leadDays": [30] },
      "priceIncrease":   { "enabled": true, "minPercent": 5 },
      "insuranceReview": { "enabled": true, "afterMonths": 12 },
      "missingData":     { "enabled": false },
      "adminWithout2fa": { "enabled": true }
    }
  }
}
```

Personal contracts use the defaults with the server time zone. Each user can additionally mute kinds
for themselves and choose an email digest (`Off`, `Daily`, `Weekly`). The digest choice is stored;
sending the email is not implemented yet.

## API

All endpoints are scoped to the signed-in user and workspace.

| Endpoint | Purpose |
|----------|---------|
| `GET /api/notifications` | visible notifications, most severe first |
| `GET /api/notifications/summary` | `{ unread, total }` |
| `POST /api/notifications/read-all` | mark everything read |
| `POST /api/notifications/{id}/read` · `/dismiss` · `/snooze` | `snooze` takes `{ "days": 1–90 }` |
| `GET` / `PUT /api/notifications/preferences` | `{ digest, mutedKinds[] }` |

Contracts gained `noticePeriodValue`, `noticePeriodUnit` (`Days`, `Weeks`, `Months`), `autoRenews` and
`renewalMonths`; send `clearNoticePeriod: true` on update to remove the notice period.

## Upgrade

Migration `AddNotificationsAndNoticePeriod` (both migration chains) adds the `Notifications` table, the
contract notice and renewal columns and two user preference columns. Back up before applying it, as for any
migration. The first worker run starts 45 seconds after start-up.
