# Windows ZIP extractors do not consistently restore symbolic links. Run this
# after extraction in an elevated PowerShell (or with Developer Mode enabled).
# Without a link, invoke .\sdw-cli.exe migrate with the same arguments.
$ErrorActionPreference = 'Stop'

New-Item -ItemType SymbolicLink `
    -Path (Join-Path $PSScriptRoot 'sdw-migrate.exe') `
    -Target (Join-Path $PSScriptRoot 'sdw-cli.exe') | Out-Null
