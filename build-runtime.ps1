param([string]$Configuration = "Release")
$runtime = Join-Path $PSScriptRoot "src/OD.Installer.Builder/bin/$Configuration/net10.0/runtime"
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
dotnet publish "$PSScriptRoot/src/OD.Installer.Setup/OD.Installer.Setup.csproj" -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$runtime/setup"
dotnet publish "$PSScriptRoot/src/OD.Installer.Uninstaller/OD.Installer.Uninstaller.csproj" -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$runtime/uninstaller"
Copy-Item "$runtime/setup/OD.Installer.Setup.exe" "$runtime/OD.Installer.Setup.exe" -Force
Copy-Item "$runtime/uninstaller/OD.Installer.Uninstaller.exe" "$runtime/OD.Installer.Uninstaller.exe" -Force
