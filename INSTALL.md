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
   | `Admin:Username` / `Admin:Password` | dashboard login - **change the password**. The same password protects "Delete events". |
   | `Agent:EnrollmentToken` | secret code the agents use to register - **make up your own** |
   | `Alerts:MinSeverityToEmail` | global threshold: events below it never send an e-mail (`Info`, `Low`, `Medium`, `High`, `Critical`). Recommended: `Medium`. |
   | `Smtp:Host`, `Port`, `UseSsl`, `Username`, `Password`, `FromAddress` | mail server. For an internal relay without authentication: `Port 25`, `UseSsl false`, empty `Username/Password`. **`Host` and `FromAddress` are required**, otherwise nothing is sent. |
   | `Kestrel:Endpoints:Https:Url` | address and port, default `https://0.0.0.0:5443` |

3. Install as a service (this also opens port 5443/TCP in the firewall):
   ```powershell
   cd C:\RdpMonitor.Server
   .\install.ps1
   Get-Service RdpMonitorServer        # should be: Running
   ```
4. Open `https://SERVER-NAME:5443/` and sign in. The browser will warn about the certificate - on first start the server creates a self-signed one (normal for an internal network).

---

## 2. Agent (on every monitored server)

1. Extract:
   ```powershell
   Expand-Archive C:\RdpMonitor.Agent.zip C:\ -Force
   Get-ChildItem C:\RdpMonitor.Agent -Recurse | Unblock-File
   ```
2. Fill in the settings and install (change the first two lines; the token is the one from the server's `appsettings.json`):
   ```powershell
   $server = "https://NAME-OR-IP-OF-THE-CENTRAL-SERVER:5443"
   $token  = "THE-TOKEN-FROM-THE-SERVER"

   $p = "C:\RdpMonitor.Agent\appsettings.json"
   $c = Get-Content $p -Raw | ConvertFrom-Json
   $c.Agent.ServerUrl = $server
   $c.Agent.EnrollmentToken = $token
   $c.Agent.AllowInsecureTls = $true       # needed while the server uses a self-signed certificate
   $c | ConvertTo-Json -Depth 8 | Set-Content $p -Encoding UTF8

   cd C:\RdpMonitor.Agent
   .\install.ps1
   Get-Service RdpMonitorAgent            # should be: Running
   ```
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

**Dashboard:** *Dashboard* (agents, online/offline, mute, "Delete events" with password), *Agents* (registered agents, "Delete agent" by typing its name), *Events* (with filters and details), *Settings* (priority and e-mail per type, recipients).

An e-mail is sent only if: the agent is not muted **and** the event type has "Sends e-mail" ticked **and** its priority is >= `MinSeverityToEmail`. To receive e-mails you must add at least one **active recipient** in *Settings*.

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
The folders and the data in `C:\ProgramData\RdpMonitor` (database, certificate, agent identity) are not deleted - remove them manually for a full cleanup. Then, if you like, remove the agent from *Agents* in the dashboard too.

---

## 9. If something does not work

| Problem | What to check |
|---|---|
| The server/agent "hangs" when started by hand, or the service does not start | Did you run `Unblock-File` on the zip and the files? Antivirus/ASR/Application Control on the machine. The packages are not a single-file exe precisely because of this. |
| The agent does not appear in the dashboard | `Test-NetConnection server -Port 5443`; does `EnrollmentToken` match; `AllowInsecureTls: true`; `ServerUrl` with `https://` and the port |
| No e-mails | Is there an active recipient; are `Smtp:Host` and `FromAddress` filled in; was the service restarted after the change; the `MinSeverityToEmail` threshold; the "Sends e-mail" tick for the type |
| No failed RDP logons | `auditpol /get /subcategory:"Logon"` must be `Success and Failure` |
| No `ProcessStarted` | Did you run `setup-audit.ps1`; `auditpol /get /subcategory:"Process Creation"` = `Success`; does event 4688 contain a command line |
| Agent messages | `Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='RdpMonitor Agent'} -MaxEvents 30`, or stop the service and run `.\RdpMonitorAgent.exe` by hand |

---

## 10. Security - before real use

- Change `Admin:Password` and `Agent:EnrollmentToken` (do not leave `CHANGE-ME...`).
- Keep the server on an internal network/behind a VPN; do not expose port 5443 to the internet.
- The agent runs as `LocalSystem` (needed to read the Security log).
- Monitoring processes and files affects the people who work on those servers - make sure it is allowed by your policy and the law.
