# RDP Monitor

Агент/сървър система за наблюдение на RDP сесии и подозрителни процеси на Windows Server 2022 и по-нови.

- **Агент** – Windows услуга на всеки наблюдаван сървър: засича RDP събития и подозрителни процеси и ги праща към централния сървър. (Файловият одит е подготвен, но чака политика за одит.)
- **Сървър** – централен: приема данни, пази ги (SQLite), праща имейл според приоритет и показва уеб табло.

**За инсталация и работа виж [ИНСТАЛАЦИЯ.md](ИНСТАЛАЦИЯ.md).**

## Пакети

Създават се с `.\publish.ps1` (изисква .NET 8 SDK) в папка `dist\`:

- `RdpMonitor.Server.zip`
- `RdpMonitor.Agent.zip`
- `ИНСТАЛАЦИЯ.md`

## Устройство на кода

```
src/RdpMonitor.Shared   общи типове (събития, приоритети, DTO)
src/RdpMonitor.Agent    Windows услуга: RdpSessionMonitor, AuditMonitor/AuditParser, ServerApiClient, LocalEventQueue
src/RdpMonitor.Server   ASP.NET Core: API за агентите, Razor Pages табло, AlertService, EmailService, SQLite
deploy/update.ps1       скрипт за обновяване (копира се във всеки пакет)
```

Агентът пази събитията локално (`C:\ProgramData\RdpMonitor\events.queue.jsonl`), докато сървърът ги приеме, така че при прекъсване нищо не се губи.

## Отговорна употреба

Инструментът следи входове, процеси (и по избор файлове) на сървъри. Използвай го само на системи, които управляваш или за които имаш изрично разрешение, и в съответствие с вътрешните правила и закона за защита на личните данни. Подаван е „както е“, без гаранции (виж [LICENSE](LICENSE)). Преди реална употреба смени паролите и токена по подразбиране (`CHANGE-ME…`) – виж [ИНСТАЛАЦИЯ.md](ИНСТАЛАЦИЯ.md).

---

**English (short):** RDP Monitor is a Windows Server agent + central ASP.NET Core server. The agent (Windows service) reports RDP logons/logoffs/failed logons and suspicious process starts (optional file audit) to the server, which stores them in SQLite, sends e-mail alerts by priority and shows a web dashboard. Build with the .NET 8 SDK (`.\publish.ps1` produces self-contained `dist\*.zip` packages). Documentation is in Bulgarian. Licensed under MIT.
