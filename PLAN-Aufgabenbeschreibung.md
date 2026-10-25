# GiteaBot – Aufgabenbeschreibung / Lastenheft (Entwurf v1)

Status: **ENTWURF – wartet auf Freigabe.** Es wurde bewusst noch kein Code erstellt.

---

## 1. Ziel

Ein Telegram-Bot, der

1. in einer (Forum-/Themen-)Gruppe alle Nachrichten mitliest,
2. GitHub-Links erkennt,
3. entscheidet, ob es sich um einen **Repo-Link** oder einen **Organisations-Link** handelt,
4. auf die Nachricht mit **Inline-Buttons** antwortet und nachfragt, ob das Repo / die Organisation über **Gitea** gesichert werden soll,
5. bei **Ablehnung** die eigene Frage-Nachricht löscht,
6. bei **Zustimmung** die **Gitea-API per HTTP** beauftragt,
7. nach erfolgreichem Auftrag die Frage-Nachricht löscht und den Vorgang mit einer Nachricht bestätigt,
8. dabei **immer im richtigen Thema (Topic/Thread)** der Gruppe sendet.

Zusätzlich betreibt die Anwendung einen **HTTP-Endpunkt**, auf dem Gitea-Notifications per POST eintreffen; der Endpunkt-Link wird beim Start in der Konsole ausgegeben. Die empfangene Nachricht wird in die Gruppe – ins passende GitHub-Thema – weitergeleitet.

---

## 2. Technologie-Entscheidungen

| Punkt | Entscheidung | Begründung |
|---|---|---|
| Runtime | `.NET 10` Konsole (vorhanden) | Projekt besteht bereits |
| Telegram | NuGet **`TelegramBotBase`** (auf `Telegram.Bot` v22) | Vorgabe aus `.lmlocal/SKILL.md` |
| HTTP-Server | **ASP.NET Core Minimal API** (Kestrel) im selben Prozess via `<FrameworkReference Include="Microsoft.AspNetCore.App" />` | Ein Prozess = Bot + Endpunkt; kein separater Web-Host nötig |
| Hosting | `IHostedService` (Bot) + `WebApplication` im selben Host | Muster aus SKILL.md §17 (`BotAndWebApplication`) |
| Konfiguration | `appsettings.json` + Umgebungsvariablen (API-Keys **nicht** im Quellcode) | SKILL.md §2 / §22 |
| Logging | `ILogger<T>` in Konsole | SKILL.md §22 |

---

## 3. Prozessarchitektur

```
┌──────────────────────────────────────────────┐
│ GiteaBot (ein Prozess, .NET 10)              │
│                                              │
│  BotHostedService ── TelegramBotBase ───────▶ Telegram (Long Polling)
│        │                    │
│        │              GroupWatchForm ── LinkErkennung ──▶ BackupService ──▶ Gitea API
│        │                                        ▲
│  Minimal API  POST /api/gitea/webhook ──────────┘
│        └──▶ NotificationRouter ──▶ Gruppe + Topic
└──────────────────────────────────────────────┘
```

---

## 4. Teilaufgaben

### TA-01 – Projektgerüst & Pakete
- `TelegramBotBase` hinzufügen.
- `FrameworkReference Microsoft.AspNetCore.App` in `GiteaBot.csproj`.
- Mappe-Struktur anlegen (SKILL.md §21):
  ```
  GiteaBot/
  ├─ Program.cs
  ├─ appsettings.json (+ .example ohne Secrets)
  ├─ Configuration/AppSettings.cs
  ├─ Forms/StartForm.cs        (SplitterForm: privat / Gruppe)
  ├─ Forms/GroupWatchForm.cs   (GroupForm: liest alle Nachrichten)
  ├─ Telegram/TopicSender.cs   (sendet garantiert ins richtige Topic)
  ├─ Analysis/GitHubLinkAnalyzer.cs   (+ GitHubLink, LinkKind)
  ├─ Gitea/GiteaApiClient.cs   (HTTP-Aufrufe)
  ├─ Services/BackupService.cs (Orchestrierung Repo/Org)
  ├─ Services/PendingRequestStore.cs
  ├─ Web/GiteaWebhookEndpoint.cs
  ├─ Web/Models/GiteaNotification.cs  (Root: title/message/type/timestamp)
  ├─ Services/NotificationRouter.cs
  └─ BotHostedService.cs
  ```

### TA-02 – Konfiguration
Schlüssel (mit Default & Validierung, Fehlermeldung + Abbruch wenn fehlen):

| Schlüssel | Bedeutung |
|---|---|
| `Telegram:ApiKey` | Bot-Token |
| `Telegram:GroupId` | Chat-ID der Zielgruppe (negativ) – Fallback für Push ohne Session |
| `Telegram:TopicName` / `Telegram:TopicId` | Topic, in das GitHub-Meldungen gepostet werden |
| `Gitea:BaseUrl` | z. B. `https://gitea.example.com` |
| `Gitea:Token` | API-Token |
| `Gitea:TargetOwner` | Organisation/Owner in Gitea, in den gesichert wird |
| `Http:BaseUrl` | z. B. `http://0.0.0.0:5080` |
| `Http:ApiKey` | optionaler Shared-Secret-Header für den Webhook |

### TA-03 – Bot-Aufbau
- `BotBaseBuilder.Create().WithAPIKey(...).DefaultMessageLoop(eam).WithStartForm<StartForm>().NoProxy().UseDefaultRequestDispatcher().CustomCommands(...).NoSerialization().UseGerman().UseSingleThread().Build()`
- Events: `bot.Exception`, `bot.UnhandledCall` (SKILL.md §11, §22).
- `Start()` / `Stop()` über `IHostedService`.

### TA-04 – Nachrichten mitlesen (Gruppe)
- `StartForm : SplitterForm` → `OpenGroup/OpenSupergroup` → `GroupWatchForm`.
- `GroupWatchForm : GroupForm` → `OnMessage(MessageResult)` für **jede** Nachricht.
- Bot muss in der Gruppe Admin sein **oder** `/setprivacy → Disable`, damit er fremde Nachrichten sieht (Doku in README).

### TA-05 – GitHub-Erkennung & Parsen
- Regex/Parser in `GitHubLinkAnalyzer`:
  - `https://github.com/{owner}` → **Organisation** (bzw. User-Account)
  - `https://github.com/{owner}/{repo}` → **Repository**
  - Rest-Pfade (`/issues/3`, `/tree/main/...`, `/blob/...`, `/pull/1`) werden abgeschnitten.
  - Auch `github.com` ohne `www`/ohne Schema, Links in `Message.Entities` (Url/TextUrl) **und** im Rohtext.
  - Duplikate pro Nachricht zusammenfassen; Domain-Filter (`github.com`, nicht `gist.github.com`, nicht `gitea.*`).
- Ergebnis-Modell: `GitHubLink { Kind (Repo|Organization), Owner, Repo?, OriginalUrl }`.

### TA-06 – Frage-Nachricht mit Inline-Buttons
- Antwort auf die Ursprungsnachricht (`replyTo`), im **gleichen Topic**.
- Buttons (max. 3 pro Zeile, Callback-Daten ≤ 64 Byte):
  - `Repo sichern` / `Organisation sichern` (nur der passende Button, je nach erkanntem Typ; bei gemischten Links einer pro Link)
  - `Abbrechen`
- Callback-Design: `CallbackData.Create("giteabackup", <kurzeId>)`; die kurzeId (GUID gekürzt) zeigt auf `PendingRequestStore` → enthält Owner/Repo/Typ/ChatId/MessageThreadId/Frage-MessageId. (Verhindert `CallbackDataTooLongException`.)
- Handler in `Action()` mit `await message.ConfirmAction()` und `message.Handled = true`.

### TA-07 – Ablehnen
- `Abbrechen` → `Device.DeleteMessage(frageNachricht)`; Eintrag im `PendingRequestStore` entfernen; keine weitere Nachricht.

### TA-08 – Gitea-Beauftragung  *(umgesetzt, gemäß Freigabe O-1)*
- `GiteaApiClient` (HttpClient mit BaseUrl + API-Schlüssel im Header `x-api-key`):
  - **Repo:** `POST /api/sync/repository` mit `{ "owner": "...", "repo": "..." }`
  - **Organisation:** `POST /api/sync/organization` mit `{ "org": "...", "role": "member" }`
  - Die eigentliche Sicherung übernimmt Gitea; wir senden nur den Auftrag und parsen die Antwort.
- **Spiegel-Job direkt nach der Anlage** (`GiteaCatalogService` + `BackupService`):
  `GET /api/github/repositories` liefert `id` (Repo) bzw. `organization` (Org); damit
  `POST /api/job/mirror-repo` mit `{ "repositoryIds": [...] }` bzw.
  `POST /api/job/mirror-org` mit `{ "organizationIds": [...] }`.
- Fehlerbehandlung: 409 „already exists“ → als „bereits vorhanden“ melden; Netzwerkfehler/4xx/5xx → Fehlermeldung an Gruppe.

### TA-09 – Erfolgsmeldung
- Frage-Nachricht löschen.
- Bestätigungs-Nachricht ins gleiche Topic: „✅ Repo `xyz` wird in Gitea als `TargetOwner/xyz` gesichert (Spiegel-Repo angelegt + Sync gestartet).“
- Bei Organisation: Anzahl gesicherter Repos + Fehlerliste.
- **Während des Vorgangs:** eine „⏳ In Bearbeitung“-Nachricht, die pro Schritt per Edit
  fortgeschrieben wird (`Telegram/BackupProgress.cs`) – der Vorgang läuft über mehrere
  HTTP-Rundläufe und wäre sonst zeitlang ohne sichtbares Zeichen. Am Ende wird dieselbe
  Nachricht durch das Ergebnis ersetzt.

### TA-10 – Topic-Zuordnung (Pflicht, Punkt 8)
- Beim Antworten: `reply_to_message_id` der Ursprungsnachricht → Telegram routet automatisch ins Topic.
- Zusätzlich explizit `message_thread_id` aus der eingehenden Nachricht übernehmen, falls vorhanden.
- Falls `MessageResult`/`IDeviceSession.Send` die ThreadId nicht durchreicht: `Device.Dispatch(c => c.SendTextMessageAsync(chatId, text, messageThreadId: ...))` (SKILL.md §5) – wird in TA-01 verifiziert.
- Für den Webhook-Pfad (TA-12): Ziel-Topic aus Konfiguration (`TopicId`) oder per `GetForumTopicsAsync` anhand `TopicName` auflösen und cachen.

### TA-11 – HTTP-Endpunkt
- Kestrel auf `Http:BaseUrl`, Route **`POST /api/gitea/webhook`** (optional zusätzlich `GET /health`).
- Payload exakt wie vorgegeben:
  ```csharp
  public class Root { public string title; public string message; public string type; public DateTime timestamp; }
  ```
  (als Properties mit JSON-CamelCase-Binding)
- Antwort: `200 OK` + JSON `{ ok = true }`; Validierung; optional Header-Check (`X-Gitea-Token`).
- Beim Start Konsole-Ausgabe:
  `→ Gitea-Webhook-Endpunkt: http://0.0.0.0:5080/api/gitea/webhook`

### TA-12 – Weiterleitung an Gruppe/Topic
- `NotificationRouter`: nimmt `Root` entgegen → formatiert (fett: `title`, dann `message`, Typ + Zeitstempel) → sendet in Gruppe `GroupId` ins Topic „GitHub“.
- Wenn für die Gruppe keine aktive Session existiert: Session über `bot.Sessions.StartSession(chatId)` erzeugen bzw. direkt über `Device.Api/Dispatch` senden (SKILL.md §5, §12.1).

### TA-13 – Robustheit & Abschluss
- `bot.Exception`-Handler, Strukturiertes Logging aller Schritte.
- `README.md` mit Setup (BotFather, `/setprivacy`, Gitea-Token, Webhook-URL, appsettings).
- Build + Smoke-Test.

---

## 5. Reihenfolge der Umsetzung

1. TA-01/02 (Gerüst, Config) → Build grün
2. TA-03/04 (Bot läuft, liest Gruppe)
3. TA-05 (Link-Analyse, unit-testbar)
4. TA-06/07/10 (Buttons + Topic)
5. TA-08/09 (Gitea)
6. TA-11/12 (HTTP-Endpunkt + Weiterleitung)
7. TA-13 (Doku, Feinschliff)

---

## 6. Offene Punkte – bitte vor Freigabe beantworten

| # | Frage | Mein Vorschlag, falls du nichts anderes sagst |
|---|---|---|
| **O-1** | Was genau bedeutet „Sicherung beauftragen“ bei Gitea? | **Spiegel-Repo (mirror) in Gitea anlegen** (`POST /api/v1/user/repos` mit `clone_url` + `mirror=true`) und `mirror-sync` auslösen. Falls ihr einen eigenen Gitea-Backend-Endpunkt habt (z. B. `POST /api/v1/repos/{owner}/{repo}/backup`), nenne mir URL + Payload. |
| **O-2** | In welchen Gitea-Owner soll gesichert werden? | Konfig `Gitea:TargetOwner`; bei Org-Links wird die Org in Gitea 1:1 gespiegelt. |
| **O-3** | Bei Organisations-Links: wirklich **alle** Repos sichern (auch Forks/Archivierte)? | Alle Nicht-Fork-, Nicht-Archiv-Repos, mit Sammel-Report. |
| **O-4** | Topic-Ermittlung für Webhook-Meldungen: feste Topic-ID in Config oder Topic per Name („GitHub“) suchen? | Config `Telegram:TopicId`, sonst Suche per `TopicName`. |
| **O-5** | HTTP-Endpunkt: Authentifizierung nötig? | Optional `X-Api-Key`-Header, abschaltbar. |
| **O-6** | Soll die Ursprungs-Fremdnachricht auch gelöscht werden? | **Nein** – laut Punkt 5 nur die eigene Bot-Nachricht. |
| **O-7** | Port/Bind für Kestrel? | `http://0.0.0.0:5080` (per Config änderbar), Link in Konsole. |

---

## 7. Abnahmekriterien

- [ ] Bot antwortet auf eine Gruppen-Nachricht mit `https://github.com/majmccloud/TelegramBotFramework` mit Buttons „Repo sichern“ / „Abbrechen“ – **im selben Topic**.
- [ ] Bei `https://github.com/majmccloud` heißt es korrekt „Organisation“ und der Button lautet „Organisation sichern“.
- [ ] „Abbrechen“ → Frage-Nachricht ist weg, keine weitere Nachricht.
- [ ] „Sichern“ → Gitea-Aufruf passiert, Frage-Nachricht weg, Bestätigung im selben Topic.
- [ ] `POST /api/gitea/webhook` mit dem `Root`-JSON → Meldung erscheint in der Gruppe im GitHub-Topic.
- [ ] Konsolenstart zeigt den Webhook-Link.
- [ ] Solution baut fehlerfrei.
- [ ] **Chat-Absicherung:** Nachrichten aus anderen Gruppen und privaten Chats lösen keine Rückfrage aus;
      Buttons aus fremden Chats lösen keine Sicherung aus; `/status` antwortet nur in der Zielgruppe.
