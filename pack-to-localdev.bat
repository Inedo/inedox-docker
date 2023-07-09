@echo off

dotnet new tool-manifest --force
dotnet tool install inedo.extensionpackager

cd Docker\InedoExtension
dotnet inedoxpack pack . C:\LocalDev\BuildMaster\Extensions\Docker.upack --build=Debug -o
cd ..\..