<#
.SYNOPSIS
    Publie la buvette pour la VM de la Freebox (Linux ARM64) et l'y installe.

.EXAMPLE
    # Première fois : prépare la VM (Caddy, service, mot de passe), puis envoie l'application.
    .\deploy\freebox\deployer.ps1 -Vm freebox@192.168.1.60 -Installer -Domaine buvette.mondomaine.fr

.EXAMPLE
    # Les fois suivantes : envoie simplement la nouvelle version.
    .\deploy\freebox\deployer.ps1 -Vm freebox@192.168.1.60
#>
param(
    # Utilisateur et adresse SSH de la VM, par exemple freebox@192.168.1.60
    [Parameter(Mandatory)] [string] $Vm,
    [switch] $Installer,
    [string] $Domaine
)

$ErrorActionPreference = 'Stop'
$racine = Resolve-Path "$PSScriptRoot\..\.."
$publie = Join-Path $racine 'publie-freebox'
$archive = Join-Path $env:TEMP 'buvette.tar.gz'
$distant = '/tmp/buvette-deploiement'


Write-Host '==> Publication pour Linux ARM64 (processeur de la Freebox Delta)'
if (Test-Path $publie) { Remove-Item -Recurse -Force $publie }
dotnet publish "$racine\src\Buvette" -c Release -r linux-arm64 --self-contained -o $publie
if ($LASTEXITCODE) { throw 'La publication a échoué.' }

# Jamais de base ni de réglages de développement envoyés : la base de la VM est la seule qui compte.
Get-ChildItem $publie -Include 'buvette.db*', 'appsettings.Development.json' -Recurse | Remove-Item -Force

Write-Host '==> Archive'
tar -czf $archive -C $publie .
if ($LASTEXITCODE) { throw "Échec de la création de l'archive." }

Write-Host "==> Envoi vers $Vm"
ssh $Vm "rm -rf $distant && mkdir -p $distant"
scp $archive "$PSScriptRoot\installer.sh" "$PSScriptRoot\mettre-a-jour.sh" "$PSScriptRoot\buvette.service" "$PSScriptRoot\Caddyfile" "${Vm}:$distant/"
if ($LASTEXITCODE) { throw "Échec de l'envoi." }

if ($Installer) {
    Write-Host '==> Installation initiale (le mot de passe sudo puis celui de la buvette vont être demandés)'
    ssh -t $Vm "sudo bash $distant/installer.sh $Domaine"
    if ($LASTEXITCODE) { throw "Échec de l'installation." }
}

Write-Host '==> Mise en service'
ssh -t $Vm "sudo bash $distant/mettre-a-jour.sh $distant/buvette.tar.gz && rm -rf $distant"
if ($LASTEXITCODE) { throw 'Échec de la mise à jour.' }

Remove-Item $archive
Write-Host ''
Write-Host 'Buvette en ligne.' -ForegroundColor Green
if ($Domaine) { Write-Host "    https://$Domaine" }
elseif ($Installer) { Write-Host "    http://$($Vm.Split('@')[-1])   (réseau local ; relancer avec -Installer -Domaine pour Internet)" }
