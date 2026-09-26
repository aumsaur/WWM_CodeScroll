# Builds the portable exe into dist\. It's self-contained: the .NET runtime is
# packed inside, so it runs on a PC that has never had .NET installed.
# -AsInvoker builds a copy that runs without the admin prompt, for testing only.
param([switch]$AsInvoker)

$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot $(if ($AsInvoker) { 'dist-test' } else { 'dist' })

dotnet publish (Join-Path $PSScriptRoot 'src\WwmRedeem\WwmRedeem.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:AsInvoker=$($AsInvoker.IsPresent.ToString().ToLower()) `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Get-ChildItem $out -File | ForEach-Object { "{0}  {1:N1} MB" -f $_.FullName, ($_.Length / 1MB) }
