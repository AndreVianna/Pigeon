$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $dotnetRoot = Split-Path -Parent $dotnet
    $sdk = (dotnet --version).Trim()
    $compiler = Join-Path $dotnetRoot "sdk\$sdk\Roslyn\bincore\csc.dll"
    $refRoot = Join-Path $dotnetRoot 'packs\Microsoft.NETCore.App.Ref'
    $packs = @(Get-ChildItem $refRoot -Directory | Where-Object { $_.Name -match '^11\.0\.' })
    $refPack = $packs | Where-Object { $_.Name -match '^11\.0\.\d+$' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $refPack) { $refPack = $packs | Sort-Object Name -Descending | Select-Object -First 1 }
    if (-not $refPack -or -not (Test-Path $compiler)) { throw 'A .NET 11 SDK and reference pack are required.' }
    $out = Join-Path $root 'bin\offline'
    New-Item -ItemType Directory -Force $out | Out-Null
    $refs = Get-ChildItem (Join-Path $refPack.FullName 'ref\net11.0\*.dll') | ForEach-Object { '-r:"' + $_.FullName + '"' }
    function Compile([string]$name, [string[]]$options, [string[]]$sources) {
        $rsp = Join-Path $out "$name.rsp"
        @('/nologo', '/nullable:enable', '/langversion:14.0') + $options + $refs + $sources | Set-Content -Encoding utf8 $rsp
        & dotnet $compiler "@$rsp"
        if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $name" }
    }
    $global = 'tools\OfflineGlobalUsings.cs'
    Compile 'core' @('/target:library', ('/out:"' + $out + '\Pigeon.Core.dll"')) @($global, 'src\Pigeon.Core\Sqlite.cs', 'src\Pigeon.Core\Mailbox.cs')
    Compile 'service' @('/target:library', ('/out:"' + $out + '\Pigeon.Service.dll"'), ('/r:"' + $out + '\Pigeon.Core.dll"')) @($global, 'src\Pigeon.Service\Broker.cs', 'src\Pigeon.Service\LocalPipe.cs', 'src\Pigeon.Service\McpSession.cs', 'src\Pigeon.Service\McpStdio.cs')
    Compile 'adapters' @('/target:library', ('/out:"' + $out + '\Pigeon.Adapters.dll"'), ('/r:"' + $out + '\Pigeon.Core.dll"'), ('/r:"' + $out + '\Pigeon.Service.dll"')) @($global, 'src\Pigeon.Adapters\RuntimeAdapter.cs', 'src\Pigeon.Adapters\ClaudeChannel.cs')
    $references = @(('/r:"' + $out + '\Pigeon.Core.dll"'), ('/r:"' + $out + '\Pigeon.Adapters.dll"'), ('/r:"' + $out + '\Pigeon.Service.dll"'))
    $project = [xml](Get-Content -Raw 'src\Pigeon.Cli\Pigeon.Cli.csproj')
    $cliVersion = [string]$project.Project.PropertyGroup.Version
    if ($cliVersion -notmatch '^[0-9A-Za-z.-]+$') { throw 'Invalid CLI package version.' }
    $versionFile = Join-Path $out 'Pigeon.OfflineAssemblyInfo.cs'
    ('[assembly: System.Reflection.AssemblyInformationalVersion("' + $cliVersion + '")]') | Set-Content -Encoding utf8 $versionFile
    Compile 'cli' (@('/target:exe', ('/out:"' + $out + '\pigeon.dll"')) + $references) @($global, 'src\Pigeon.Cli\Program.cs', 'src\Pigeon.Cli\CliHelp.cs', ('"' + $versionFile + '"'))
    $runtime = @{ runtimeOptions = @{ tfm = 'net11.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $refPack.Name } } } | ConvertTo-Json -Compress -Depth 4
    Set-Content -Encoding utf8 (Join-Path $out 'pigeon.runtimeconfig.json') $runtime
    Write-Host 'Built the application offline. Run dotnet bin/offline/pigeon.dll --help; xUnit tests require restored packages.'
}
finally { Pop-Location }
