if ($IsLinux) {
  sudo mkdir -p /home/azp
  sudo chmod 777 /home/azp
}

if (-not (Test-Path Env:AZP_URL)) {
  Write-Error "error: missing AZP_URL environment variable"
  exit 1
}

New-Item "/home/azp/agent" -ItemType directory | Out-Null

if (-not (Test-Path Env:AZP_TOKEN_FILE)) {
  if (-not (Test-Path Env:AZP_TOKEN)) {
    Write-Error "error: missing AZP_TOKEN environment variable"
    exit 1
  }

  $Env:AZP_TOKEN_FILE = "/home/azp/.token"
  $Env:AZP_TOKEN | Out-File -FilePath $Env:AZP_TOKEN_FILE
}

$Env:AZP_WORK = if($IsLinux) { [System.IO.Path]::GetFullPath('/home/azp/work') } else { "C:/home/azp/work" }

Remove-Item Env:AZP_TOKEN

if ((Test-Path Env:AZP_WORK) -and -not (Test-Path $Env:AZP_WORK)) {
  New-Item $Env:AZP_WORK -ItemType directory | Out-Null
}

$csharpFile = Join-Path $PSScriptRoot "Functions.cs"

Add-Type -TypeDefinition (Get-Content -Raw -Path $csharpFile) -Language CSharp

$excludedVars = [Functions]::GetEnvironmentVars("^((GITHUB_.+)|(AZP_*))");

Write-Host "Excluded vars: $excludedVars"

# Let the agent ignore the token env variables
$Env:VSO_AGENT_IGNORE = "AZP_TOKEN,AZP_TOKEN_FILE,AZP_TASK_URL,token,taskUrl,buildNumber,pool,targetAzureRegion,image,parallelism,$excludedVars"

$azureRegion = Invoke-RestMethod -Headers @{"Metadata"="true"} -Uri "http://169.254.169.254/metadata/instance/compute/location?api-version=2017-08-01&format=text"
Write-Host "Azure Region: $azureRegion"

$Env:AzureRegion = $azureRegion

Write-Host "##vso[task.setvariable variable=AzureRegion;]$azureRegion"

Set-Location "/home/azp/agent"

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
  tar -xzf agent.$archSfx -C /home/azp/agent
} else {
  Expand-Archive -Path "agent.zip" -DestinationPath "/home/azp/agent"
}

$agentName = $env:AZP_AGENT_NAME

try
{
  $pat = $(Get-Content ${Env:AZP_TOKEN_FILE})

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
    --token "$pat" `
    --pool "$(if (Test-Path Env:AZP_POOL) { ${Env:AZP_POOL} } else { 'Default' })" `
    --work "$(if (Test-Path Env:AZP_WORK) { ${Env:AZP_WORK} } else { '_work' })" `
    --replace

  . "./config.$sfx" --unattended `
    --agent "$(if (Test-Path Env:AZP_AGENT_NAME) { ${Env:AZP_AGENT_NAME} } else { hostname })" `
    --url "$(${Env:AZP_URL})" `
    --auth PAT `
    --token "$pat" `
    --pool "$(if (Test-Path Env:AZP_POOL) { ${Env:AZP_POOL} } else { 'Default' })" `
    --work "$(if (Test-Path Env:AZP_WORK) { ${Env:AZP_WORK} } else { '_work' })" `
    --replace

  Write-Host "4. Running Azure Pipelines agent..." -ForegroundColor Cyan

  $agentRunnerDll = Join-Path $PSScriptRoot "tools/AzDevAgentRunner.dll"

  $taskUrl = $Env:AZP_TASK_URL

  # Clear environment variables before running
  [Functions]::ClearEnvironmentVars($Env:VSO_AGENT_IGNORE);

  . dotnet $agentRunnerDll runagent `
    --taskUrl "$taskUrl" `
    --token "$pat" `
    -- "run.$sfx" --once

  # $exitCode = [Program]::Run($PWD, $sfx)
  # "./run.$sfx" --once
  $exitCode = $LASTEXITCODE

  Write-Host "4. Finished running job (Exit code:$exitCode)" -ForegroundColor Cyan
  exit 0;
}
finally
{
  if ($agentName -ieq "Placeholder") {
      Write-Host "Skipping cleanup. This is a placeholder agent."
  } else {
    Write-Host "Cleanup. Removing Azure Pipelines agent..." -ForegroundColor Cyan

    ./config.cmd remove --unattended `
      --auth PAT `
      --token "$pat"
  }
}