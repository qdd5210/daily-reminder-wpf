param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    if (-not $SkipTests) {
        & dotnet run --project 'tests\StorageVerification\StorageVerification.csproj'
        if ($LASTEXITCODE -ne 0) { throw 'Storage verification failed' }
    }
    & dotnet publish 'DailyReminder.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o 'release-1.4\app'
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed' }
    & '.\packaging\build-setup.ps1'
}
finally {
    Pop-Location
}
