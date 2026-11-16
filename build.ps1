param([string]$Configuration = "Release")
dotnet build src\MiRemote.App\MiRemote.App.csproj -c $Configuration
if ($LASTEXITCODE -eq 0) {
    Write-Host "Build OK: src\MiRemote.App\bin\$Configuration\net9.0-windows\MiRemote.exe"
}
