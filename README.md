# OD Installer

OD Installer is a small .NET 10 Windows installer generator. The MVP targets per-user desktop applications such as OD.HarmoTools.

**Site:** visit [OD.Installer](https://e-naxos.com/installer/)!

## Build and generate

1. Build the solution: `dotnet build OD.Installer.slnx -c Release`.
2. Create or edit `installer.json` with the graphical configurator: `dotnet run --project src/OD.Installer.Configurator`.
   In Visual Studio, select **Configurateur WPF** in the launch-profile dropdown, or right-click `OD.Installer.Configurator` and choose **Set as Startup Project**, before pressing F5. The `OD.Installer.Builder` project is intentionally a command-line tool and requires the `build` command plus a configuration-file path.
3. Create the self-contained installation runtime: `./build-runtime.ps1`.
4. Put the published OD.HarmoTools files in `samples/OD.HarmoTools/publish` and its actual license in `LICENSE.txt`.
5. Run `dotnet run --project src/OD.Installer.Builder -- build samples/OD.HarmoTools/installer.json --force`.

The output is one EXE: a self-contained WPF setup host followed by a standard ZIP payload. The host discovers the payload from its footer, so it can be copied and launched alone.

## Solution

`Core` contains models, validation, file, update-transaction, and package safety; `Configurator` creates and edits installer JSON files; `Builder` makes the package; `Setup` installs or updates it; `Uninstaller` removes the managed installation directory and the external files listed in the local manifest. Tests cover core behaviour and builder validation.

## Multiple source folders

`source.folders` maps several source directories to their installation destinations: `"."` (or empty) for the installation root, a relative subfolder of the root, or an absolute path on the target machine (with the `{LocalAppData}` and `<Application>` placeholders). The application executable must live in a folder mapped to `"."`. The legacy `source.directory` field is still accepted and behaves as a single folder mapped to `"."`. Absolute destinations must initially be empty; later updates recognize them through the installed manifest.

## Updates

Launching a newer installer with the same `application.id` and installation directory performs an in-place transactional update. Setup displays the installed and package versions, prepares the complete new application in a sibling staging directory, verifies that the active installation is not locked, then swaps directories. Shortcuts and the HKCU uninstall entry are updated only after activation. If copying, activation, shortcut creation, or registration fails, the previous directory, overwritten external files, shortcuts, and registry values are restored.

The installation root is fully managed by OD Installer: files absent from the new package are removed by the directory swap. Applications must store user data outside that root, for example under `%AppData%` or `%LocalAppData%`. External destinations are updated file by file; overwritten files are backed up for rollback, and their previous inventory is retained so a later uninstall can still remove them.

## MVP limitations

Only `perUser` is supported. Updates still contain the full application archive rather than a differential patch. Moving an existing installation to another directory requires uninstalling it first; installing an older version requires explicit confirmation. Shortcuts are real `.lnk` files created through the shell's COM interface, with the working directory set so applications that depend on their own folder start correctly. The sample does not include the proprietary OD.HarmoTools published output, so add it before an end-to-end test.
