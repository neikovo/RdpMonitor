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
