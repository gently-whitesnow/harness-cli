#!/bin/sh
set -eu

dotnet run --project src/Harness/Host/Harness.Host.csproj -- check
dotnet format Harness.slnx --verify-no-changes --severity warn
dotnet test
