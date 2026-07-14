param([string]$Configuration = "Release")
$runtime = Join-Path $PSScriptRoot "src/OdInstaller.Builder/bin/$Configuration/net10.0/runtime"
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
dotnet publish "$PSScriptRoot/src/OdInstaller.Setup/OdInstaller.Setup.csproj" -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -o "$runtime/setup"
dotnet publish "$PSScriptRoot/src/OdInstaller.Uninstaller/OdInstaller.Uninstaller.csproj" -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -o "$runtime/uninstaller"
Copy-Item "$runtime/setup/OdInstaller.Setup.exe" "$runtime/OdInstaller.Setup.exe" -Force
Copy-Item "$runtime/uninstaller/OdInstaller.Uninstaller.exe" "$runtime/OdInstaller.Uninstaller.exe" -Force
