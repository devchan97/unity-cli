$ErrorActionPreference = "Stop"

$skillDir = "$env:USERPROFILE\.claude\skills\unity-cli"
$skillUrl = "https://raw.githubusercontent.com/devchan97/unity-cli/master/skill/SKILL.md"

New-Item -ItemType Directory -Force -Path $skillDir | Out-Null

Write-Host "Installing unity-cli Claude Code skill..."
Invoke-WebRequest -Uri $skillUrl -OutFile "$skillDir\SKILL.md" -UseBasicParsing

Write-Host "Installed skill to $skillDir\SKILL.md"
Write-Host "Claude Code will now recognize unity-cli commands in any project."
