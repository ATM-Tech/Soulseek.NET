#!/bin/bash
set -e

dotnet test --no-build --configuration Release -p:CollectCoverage=true -p:CoverletOutput="../coverage.json" tests/Soulseek.NET.Tests.Unit -p:Include="[Soulseek.NET*]*" -p:Exclude="[*.Tests.*]*"
dotnet test --no-build --configuration Release -p:CollectCoverage=true -p:MergeWith="../coverage.json" -p:CoverletOutput="../opencover.xml" -p:CoverletOutputFormat=opencover tests/Soulseek.NET.Tests.Integration -p:Include="[Soulseek.NET*]*" -p:Exclude="[*.Tests.*]*"