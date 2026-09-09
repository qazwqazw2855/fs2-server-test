# God2 Classic Server

Foundation architecture for the single official God2 Classic Server process.

- Runtime: C# on .NET 10 LTS
- Host: single ConsoleHost executable
- Startup: `Start_Server.bat`
- Data authority: MariaDB through persistence abstractions
- Scope: server foundation only

This repository intentionally does not contain Launcher, GUI, LoginServer.exe, WorldServer.exe, or Unified Server code.
