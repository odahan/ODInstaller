# OD Installer

OD Installer is a small .NET 10 Windows installer generator. The MVP targets per-user desktop applications such as OD.HarmoTools.

## Build and generate

1. Build the solution: `dotnet build OD.Installer.slnx -c Release`.
2. Create or edit `installer.json` with the graphical configurator: `dotnet run --project src/OD.Installer.Configurator`.
   In Visual Studio, select **Configurateur WPF** in the launch-profile dropdown, or right-click `OD.Installer.Configurator` and choose **Set as Startup Project**, before pressing F5. The `OD.Installer.Builder` project is intentionally a command-line tool and requires the `build` command plus a configuration-file path.
3. Create the self-contained installation runtime: `./build-runtime.ps1`.
4. Put the published OD.HarmoTools files in `samples/OD.HarmoTools/publish` and its actual license in `LICENSE.txt`.
5. Run `dotnet run --project src/OD.Installer.Builder -- build samples/OD.HarmoTools/installer.json --force`.

The output is one EXE: a self-contained WPF setup host followed by a standard ZIP payload. The host discovers the payload from its footer, so it can be copied and launched alone.

## Solution

`Core` contains models, validation, file and package safety; `Configurator` creates and edits installer JSON files; `Builder` makes the package; `Setup` installs it; `Uninstaller` removes only files listed in the local manifest. Tests cover core behaviour and builder validation.

## MVP limitations

Only `perUser` is supported. Existing installations are replaced in place; this is not a differential updater. Shortcuts are real `.lnk` files created through the shell's COM interface, with the working directory set so applications that depend on their own folder start correctly. The sample does not include the proprietary OD.HarmoTools published output, so add it before an end-to-end test.
