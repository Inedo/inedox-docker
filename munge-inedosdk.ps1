$pkgName = "Inedo.SDK"
$pkgProjFile= "c:\Projects\Inedo.sdk\Inedo.SDK\Inedo.SDK.csproj"

$slnFile = "C:\Projects\inedox-Docker\Docker\Docker.sln"
$projFilesToMunge = @( `
  "C:\Projects\inedox-docker\Docker\InedoExtension\InedoExtension.csproj"
)

dotnet sln "$slnFile" add "$pkgProjFile"
foreach ($projFile in $projFilesToMunge) {
  dotnet remove "$projFile" package "$pkgName"
  dotnet add $projFile reference $pkgProjFile
}

pause