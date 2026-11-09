$ErrorActionPreference = "Stop"
dotnet build "$PSScriptRoot\CleanC.sln" -c Release -m:1
