# RDP Monitor - installation and operation

Two programs:

- **Server** (`RdpMonitor.Server.zip`) - installed **once**, on the central machine. Receives data, sends e-mails, serves the web dashboard.
- **Agent** (`RdpMonitor.Agent.zip`) - installed on **every Windows Server** you want to monitor (it can also run on the central server itself).

All commands are for **PowerShell run as administrator**.

---

## 0. Before you start (important)

1. Copy the zip files to the target machine, for example to `C:\`.
2. **Before extracting**, remove the "downloaded from the internet" mark. Without this, some protection software (ASR/Defender) slows down or blocks the start:
   ```powershell
   Unblock-File C:\RdpMonitor.Server.zip      # or RdpMonitor.Agent.zip
   ```
3. If your organization uses Application Control/AppLocker/ASR, allow `C:\RdpMonitor.Server` and `C:\RdpMonitor.Agent`. The packages are not code-signed.

---

## 1. Central server (once)

1. Extract and remove the internet mark from the files:
   ```powershell
   Expand-Archive C:\RdpMonitor.Server.zip C:\ -Force
   Get-ChildItem C:\RdpMonitor.Server -Recurse | Unblock-File
   ```
2. Open `C:\RdpMonitor.Server\appsettings.json` and change:

   | Setting | Meaning |
   |---|---|
   | `Admin:Username` / `Admin:Password` | dashboard login. **Required:** while the password still starts with `CHANGE-ME` nobody can sign in. Use at least 12 characters. The same password protects "Delete events". |
   | `Agent:EnrollmentToken` | secret code the agents use to register. **Required:** while it starts with `CHANGE-ME` no new agent can register. Use at least 16 random characters. |
   | `Alerts:MinSeverityToEmail` | global threshold: events below it never send an e-mail (`Info`, `Low`, `Medium`, `High`, `Critical`). Recommended: `Medium`. |
   | `Smtp:Host`, `Port`, `UseSsl`, `Username`, `Password`, `FromAddress` | mail server. For an internal relay without authentication: `Port 25`, `UseSsl false`, empty `Username/Password`. **`Host` and `FromAddress` are required**, otherwise nothing is sent. |
   | `Kestrel:Endpoints:Https:Url` | address and port, default `https://0.0.0.0:5443` |

3. Install as a service (this also opens port 5443/TCP in the firewall):
   ```powershell
   cd C:\RdpMonitor.Server
   .\install.ps1
   Get-Service RdpMonitorServer        # should be: Running
   ```
   The installer also **locks the program folder** to SYSTEM and Administrators (a folder created directly under `C:\` is writable by every user by default, which would let a normal user replace a program that runs as SYSTEM, or read the passwords in `appsettings.json`).
4. Open `https://SERVER-NAME:5443/` and sign in. The browser will warn about the certificate - on first start the server creates a self-signed one (normal for an internal network). Its fingerprint, which the agents use to pin it, is written to `C:\ProgramData\RdpMonitor\server-cert-sha256.txt`:
   ```powershell
   Get-Content C:\ProgramData\RdpMonitor\server-cert-sha256.txt
   ```

---

## 2. Agent (on every monitored server)

1. Extract:
   ```powershell
   Expand-Archive C:\RdpMonitor.Agent.zip C:\ -Force
   Get-ChildItem C:\RdpMonitor.Agent -Recurse | Unblock-File
   ```
2. Fill in the settings and install. Change the first three lines: the token is the one from the server's `appsettings.json`, the fingerprint is the content of `server-cert-sha256.txt` on the server (see section 1, step 4):
   ```powershell
   $server = "https://NAME-OR-IP-OF-THE-CENTRAL-SERVER:5443"
   $token  = "THE-TOKEN-FROM-THE-SERVER"
   $pin    = "THE-SHA256-FINGERPRINT-FROM-THE-SERVER"

   $p = "C:\RdpMonitor.Agent\appsettings.json"
   $c = Get-Content $p -Raw | ConvertFrom-Json
   $c.Agent.ServerUrl = $server
   $c.Agent.EnrollmentToken = $token
   $c.Agent.ServerCertificateSha256 = $pin  # the agent will accept ONLY this certificate (safe with a self-signed one)
   $c | ConvertTo-Json -Depth 8 | Set-Content $p -Encoding UTF8

   cd C:\RdpMonitor.Agent
   .\install.ps1
   Get-Service RdpMonitorAgent            # should be: Running
   ```
   The agent trusts nothing but that certificate, so nobody on the network can pose as the server to read the agent's key or feed it false data. `Agent:AllowInsecureTls` (accept any certificate) still exists for quick lab tests, but it removes this protection: do not use it in production. With a real certificate from your own CA you need neither setting.
3. After a few seconds the server appears in the dashboard (**Dashboard** and **Agents**) with status "Online".
4. Agent on the central server itself: the same, with `$server = "https://localhost:5443"`.

**Network:** the agent connects **outbound only** to the server on **TCP 5443** (HTTPS). No inbound ports are opened on the monitored servers. Test: `Test-NetConnection SERVER-NAME -Port 5443`.

---

## 3. What is detected

| What | How | E-mail by default |
|---|---|---|
| RDP logon, logoff, disconnect, reconnect | Terminal Services log | no |
| **Failed RDP logon** (with and without NLA) | Security 4625 | **yes** (`High`) |
| **Suspicious processes** | Security 4688 - a list of programs and command-line patterns | no (enable manually) |
| Files (who, when) | Security 4663 | **not active** - see section 7 |

**Dashboard:** *Dashboard* (agents, online/offline, mute, "Delete events" with password), *Agents* (registered agents, "Delete agent" by typing its name), *Events* (with filters, details and **Export CSV**), *Settings* (priority and e-mail per type, recipients, ignored source addresses).

An e-mail is sent only if: the agent is not muted **and** the event type has "Sends e-mail" ticked **and** its priority is >= `MinSeverityToEmail`. To receive e-mails you must add at least one **active recipient** in *Settings*.

### Ignoring source addresses

If a machine you trust connects over RDP to the monitored servers all the time (for example your admin jump server), you can stop its events from being recorded: *Settings* -> **Ignored source addresses**. Enter a single address (`10.0.0.5`) or a whole network (`10.0.0.0/24`), with an optional note.

- The rule is **global** (all agents) and applies to **new** events only; nothing already recorded is removed. Ignored events are not stored and never send an e-mail.
- **Failed logons from that address are still reported**, unless you tick *Also ignore failed logons* for it. This is deliberate: a trusted machine that gets compromised and starts guessing passwords would otherwise stay invisible.
- Process events have no source address, so they are never affected.
- A rule can be deactivated and activated again without deleting it.

### Exporting events

*Events* -> **Export CSV** downloads the events that match the filters currently shown (agent, minimum priority), newest first, up to 200,000 rows. The file is UTF-8 and opens correctly in Excel; it has local and UTC time, agent, type, priority, user, source IP, session, details and whether an e-mail was sent. Text that a spreadsheet could run as a formula (starting with `=`, `+`, `-` or `@`) is prefixed with an apostrophe.

### Event retention (automatic cleanup)

Events are **deleted automatically after 7 days** by default, so the database does not grow forever. Change it under *Settings* -> **Event retention**: enter the number of days (1-3650), or **0 to keep everything**. The change applies at once and needs no restart.

- The cleanup runs once an hour (and when you press *Clean up now*). The age counts from the moment the **server received** the event, so events an agent delivers late after an outage are not deleted on arrival.
- Deleted events cannot be recovered. If you need a longer archive, use **Export CSV** on the *Events* page regularly.
- The page shows how many events are stored and the date of the oldest one. A value that is not a whole number is refused and nothing changes.
- Upgrading: a database from an older version gets the 7-day default. If you want to keep your history, set **0** (or a longer period) *before* the first cleanup runs (it starts 30 seconds after the service starts), or export first.

---

## 4. E-mail - setup and test

1. In `Smtp` on the server fill in `Host`, `Port`, `UseSsl`, `FromAddress`; then `Restart-Service RdpMonitorServer`.
2. Test the connection to the mail server:
   ```powershell
   $s = New-Object System.Net.Mail.SmtpClient("SMTP-HOST", 25); $s.EnableSsl = $false
   $s.Send("from@company.com", "to@company.com", "test", "it works")
   ```
3. Add a recipient in *Settings*.
4. Try an RDP logon with a wrong password - an e-mail should arrive, and *Events* shows "E-mail sent: Yes".

---

## 5. Process audit - Windows setup (on every agent)

Windows records process creation only if auditing is enabled. Enable it once per machine:

```powershell
cd C:\RdpMonitor.Agent
.\setup-audit.ps1          # shows what it will change and asks (y/n)
Restart-Service RdpMonitorAgent
```

The script turns on `Process Creation` auditing and the command line in it. If audit policy is managed by Group Policy (domain), a GPO may overwrite it - then enable it through the GPO.

The list of "suspicious" items is in the agent's `appsettings.json`, section `Audit` (`SuspiciousProcesses` - names; `SuspiciousCommandLinePatterns` - regular expressions). After a change: `Restart-Service RdpMonitorAgent`. Recommendation: watch the dashboard for a week and remove patterns that only produce noise before you enable e-mail for `ProcessStarted`.

Test (changes nothing):
```powershell
cmd /c "echo vssadmin delete shadows /all"
```
After 15-30 seconds *Events* should show a `ProcessStarted` with details (program, command line, why it was flagged, parent process, PID, privileges).

---

## 6. Updating to a newer version

Settings, database, certificate and the agent identity are kept; only new settings are added.

```powershell
Unblock-File C:\update\RdpMonitor.Agent.zip
Expand-Archive C:\update\RdpMonitor.Agent.zip C:\update -Force
cd C:\update\RdpMonitor.Agent
.\update.ps1 -Target C:\RdpMonitor.Agent
```
For the server - the same with `RdpMonitor.Server` and `-Target C:\RdpMonitor.Server`. The script makes a copy `appsettings.backup-DATE.json`.

---

## 7. File audit (prepared, not yet active)

The code is ready, but Windows records "who touched the file" only if the **Audit File System** policy is on. On domain machines it is often overwritten by Group Policy. A domain administrator needs to enable:

> Computer Configuration -> Policies -> Windows Settings -> Security Settings -> Advanced Audit Policy Configuration -> Object Access -> **Audit File System = Success**

Then: add the folders to `Audit:FileFolders` on the agent, run `.\setup-audit.ps1` (puts an audit entry on the folders, does not change permissions) and `Restart-Service RdpMonitorAgent`. To undo: `.\setup-audit.ps1 -Remove`.

---

## 8. Uninstalling

```powershell
cd C:\RdpMonitor.Agent;  .\uninstall.ps1      # agent
cd C:\RdpMonitor.Server; .\uninstall.ps1      # server (also removes the port 5443 rule)
```
The folders and the data in `C:\ProgramData\RdpMonitor` (database in its `server-data` subfolder, certificate, agent identity) are not deleted - remove them manually for a full cleanup. Then, if you like, remove the agent from *Agents* in the dashboard too.

---

## 9. If something does not work

| Problem | What to check |
|---|---|
| The server/agent "hangs" when started by hand, or the service does not start | Did you run `Unblock-File` on the zip and the files? Antivirus/ASR/Application Control on the machine. The packages are not a single-file exe precisely because of this. |
| The agent does not appear in the dashboard | `Test-NetConnection server -Port 5443`; does `EnrollmentToken` match; is `ServerCertificateSha256` exactly the value in `server-cert-sha256.txt`; `ServerUrl` with `https://` and the port; has the server `Agent:EnrollmentToken` been changed from `CHANGE-ME...` (otherwise it answers 503); "already registered" (409) means the agent exists in the dashboard: delete it under *Agents*, then restart the agent |
| No e-mails | Is there an active recipient; are `Smtp:Host` and `FromAddress` filled in; was the service restarted after the change; the `MinSeverityToEmail` threshold; the "Sends e-mail" tick for the type |
| No failed RDP logons | `auditpol /get /subcategory:"Logon"` must be `Success and Failure` |
| No `ProcessStarted` | Did you run `setup-audit.ps1`; `auditpol /get /subcategory:"Process Creation"` = `Success`; does event 4688 contain a command line |
| Agent messages | `Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='RdpMonitor Agent'} -MaxEvents 30`, or stop the service and run `.\RdpMonitorAgent.exe` by hand |

---

## 10. Security

**Before real use**

- Set your own `Admin:Password` (12+ characters) and `Agent:EnrollmentToken` (16+ random characters). While they still start with `CHANGE-ME` the server refuses to sign anyone in or enroll any agent, because those defaults are public on GitHub.
- Pin the server certificate on every agent (`Agent:ServerCertificateSha256`, section 2) instead of using `AllowInsecureTls`.
- Keep the server on an internal network/behind a VPN; do not expose port 5443 to the internet.
- Use e-mail through a mail server you trust. Port 25 without TLS (`UseSsl: false`) sends alerts, which contain user names, IP addresses and command lines, in clear text over your network.

**What the software does for you**

- The program folders are locked to SYSTEM and Administrators by `install.ps1` and `update.ps1` (pass `-NoFolderLock` to skip this; then make sure ordinary users cannot write to the folder). The database lives in `C:\ProgramData\RdpMonitor\server-data`, and the TLS key, the agent's API key and the agent's event queue are separate files, all readable only by SYSTEM, Administrators and the service account.
- Sign-in and enrollment secrets are compared in constant time; repeated wrong sign-ins, wrong enrollment tokens and wrong "Delete events" passwords are limited to 10 per minute per address (the server answers 429) and every failed sign-in is written to the Windows event log with the source address.
- The dashboard cookie is `Secure`, `HttpOnly`, `SameSite=Strict` and expires after 8 hours; pages are sent with `X-Frame-Options: DENY`, a Content-Security-Policy, `nosniff` and `no-store`.
- An agent that already exists cannot be re-enrolled with just the shared token (HTTP 409), so a stolen token cannot be used to take over another agent. The token can still create *new* fake agents, which is why it must stay secret; if it leaks, change it on the server and re-enroll.
- Everything an agent sends is length-limited and stripped of control characters (so a hostile host name cannot inject e-mail headers or break the page); requests are limited to 2 MB and 500 events.
- Passwords that appear in a process command line (`net user x Pass /add`, `-password ...`, `token=...`) are masked before the event is stored or e-mailed. This is best effort: it cannot recognise every way a program takes a secret.

**Know the limits**

- The agent runs as `LocalSystem` (needed to read the Security log) and the server too. Anyone who is already an administrator on those machines controls them; the tool protects against ordinary local users and the network, not against a local administrator.
- The enrollment token is stored in each agent''s `appsettings.json` (administrators only). An administrator of one monitored server therefore knows it.
- Monitoring processes and files affects the people who work on those servers - make sure it is allowed by your policy and the law.