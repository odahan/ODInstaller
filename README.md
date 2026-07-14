# OD Installer

OD Installer is a small .NET 10 Windows installer generator. The MVP targets per-user desktop applications such as OD.HarmoTools.

## Build and generate

1. Build the solution: `dotnet build OdInstaller.slnx -c Release`.
2. Create the self-contained installation runtime: `./build-runtime.ps1`.
3. Put the published OD.HarmoTools files in `samples/OD.HarmoTools/publish` and its actual license in `LICENSE.txt`.
4. Run `dotnet run --project src/OdInstaller.Builder -- build samples/OD.HarmoTools/installer.json --force`.

The output is one EXE: a self-contained WPF setup host followed by a standard ZIP payload. The host discovers the payload from its footer, so it can be copied and launched alone.

## Solution

`Core` contains models, validation, file and package safety; `Builder` makes the package; `Setup` installs it; `Uninstaller` removes only files listed in the local manifest. Tests cover core behaviour and builder validation.

## MVP limitations

Only `perUser` is supported. Existing installations are replaced in place; this is not a differential updater. `.url` shortcuts are used for a small dependency-free implementation. The sample does not include the proprietary OD.HarmoTools published output, so add it before an end-to-end test.
