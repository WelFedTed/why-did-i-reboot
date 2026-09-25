# Why Did I Reboot

A WPF app (.NET 8, `net8.0-windows`) that explains restarts, shutdowns and crashes from the Windows event logs.

## Git

- Always commit and push changes directly to `main`. Do not create feature branches or pull requests unless asked.

## Build and test

- The solution is `WhyDidIReboot.slnx`; CI (`.github/workflows/build.yml`) builds it on `windows-latest` with `-warnaserror`, runs the xunit tests, then does a headless `--export` smoke test.
- Locally on Windows: `dotnet build WhyDidIReboot.slnx -c Release -warnaserror` then `dotnet test WhyDidIReboot.slnx -c Release --no-build`.
- `src/WhyDidIReboot/Core/` has no WPF dependencies. On Linux, where the WPF SDK is unavailable, it and the tests can be compiled into a plain `net8.0` test project (plus the `System.Diagnostics.EventLog` package); tests that use Windows `\` paths fail there and only pass on Windows.
