if (-not (Test-Path Env:AZP_URL)) {
  Write-Error "error: missing AZP_URL environment variable"
  exit 1
}

New-Item "/azdev/azp/agent" -ItemType directory | Out-Null

if (-not (Test-Path Env:AZP_TOKEN_FILE)) {
  if (-not (Test-Path Env:AZP_TOKEN)) {
    Write-Error "error: missing AZP_TOKEN environment variable"
    exit 1
  }

  $Env:AZP_TOKEN_FILE = "/azdev/azp/.token"
  $Env:AZP_TOKEN | Out-File -FilePath $Env:AZP_TOKEN_FILE
}

$Env:AZP_WORK = if($IsLinux) { [System.IO.Path]::GetFullPath('/azdev/agent') } else { "C:/azdev/agent" }

Remove-Item Env:AZP_TOKEN

if ((Test-Path Env:AZP_WORK) -and -not (Test-Path $Env:AZP_WORK)) {
  New-Item $Env:AZP_WORK -ItemType directory | Out-Null
}

# Let the agent ignore the token env variables
$Env:VSO_AGENT_IGNORE = "AZP_TOKEN,AZP_TOKEN_FILE"

$azureRegion = Invoke-RestMethod -Headers @{"Metadata"="true"} -Uri "http://169.254.169.254/metadata/instance/compute/location?api-version=2017-08-01&format=text"
Write-Host "Azure Region: $azureRegion"

$Env:AzureRegion = $azureRegion

Write-Host "##vso[task.setvariable variable=AzureRegion;]$azureRegion"

Set-Location "/azdev/azp/agent"

Write-Host "1. Determining matching Azure Pipelines agent..." -ForegroundColor Cyan

$archSfx = if ($IsLinux) { "tar.gz" } else { "zip" }
$os = if ($IsLinux) { "linux" } else { "win" }

$base64AuthInfo = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$(Get-Content ${Env:AZP_TOKEN_FILE})"))
$package = Invoke-RestMethod -Headers @{Authorization=("Basic $base64AuthInfo")} "$(${Env:AZP_URL})/_apis/distributedtask/packages/agent?platform=$os-x64&`$top=1"
$packageUrl = $package[0].Value.downloadUrl

Write-Host $packageUrl

Write-Host "2. Downloading and installing Azure Pipelines agent..." -ForegroundColor Cyan

$wc = New-Object System.Net.WebClient
$wc.DownloadFile($packageUrl, "$(Get-Location)/agent.$archSfx")

if ($IsLinux) {
  tar -xzf agent.$archSfx -C /azdev/azp/agent
} else {
  Expand-Archive -Path "agent.zip" -DestinationPath "/azdev/azp/agent"
}

try
{
  Write-Host "3. Configuring Azure Pipelines agent..." -ForegroundColor Cyan

  $sfx = if($IsLinux) { "sh" } else { "cmd" }

  if ($IsLinux) {
    chmod +x ./config.sh
    chmod +x ./run.sh
  } 

  . "./config.$sfx" --unattended `
    --agent "$(if (Test-Path Env:AZP_AGENT_NAME) { ${Env:AZP_AGENT_NAME} } else { hostname })" `
    --url "$(${Env:AZP_URL})" `
    --auth PAT `
    --token "$(Get-Content ${Env:AZP_TOKEN_FILE})" `
    --pool "$(if (Test-Path Env:AZP_POOL) { ${Env:AZP_POOL} } else { 'Default' })" `
    --work "$(if (Test-Path Env:AZP_WORK) { ${Env:AZP_WORK} } else { '_work' })" `
    --replace

  . "./config.$sfx" --unattended `
    --agent "$(if (Test-Path Env:AZP_AGENT_NAME) { ${Env:AZP_AGENT_NAME} } else { hostname })" `
    --url "$(${Env:AZP_URL})" `
    --auth PAT `
    --token "$(Get-Content ${Env:AZP_TOKEN_FILE})" `
    --pool "$(if (Test-Path Env:AZP_POOL) { ${Env:AZP_POOL} } else { 'Default' })" `
    --work "$(if (Test-Path Env:AZP_WORK) { ${Env:AZP_WORK} } else { '_work' })" `
    --replace

  Write-Host "4. Running Azure Pipelines agent..." -ForegroundColor Cyan

  $csharpFile = Join-Path $PSScriptRoot "RunWithRetry.cs"

  Add-Type -TypeDefinition (Get-Content -Raw -Path $csharpFile) -Language CSharp

  $exitCode = [Program]::Run($PWD, $sfx)
  # ./run.cmd --once
  # $exitCode = $LASTEXITCODE

  Write-Host "4. Finished running job (Exit code:$exitCode)" -ForegroundColor Cyan
}
finally
{
  $agentName = $env:AZP_AGENT_NAME
  if ($agentName -ieq "Placeholder") {
      Write-Host "Skipping cleanup. This is a placeholder agent."
  } else {
    Write-Host "Cleanup. Removing Azure Pipelines agent..." -ForegroundColor Cyan

    ./config.cmd remove --unattended `
      --auth PAT `
      --token "$(Get-Content ${Env:AZP_TOKEN_FILE})"
  }
}