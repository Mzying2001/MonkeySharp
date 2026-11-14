[CmdletBinding()]
param(
    [ValidateSet('84.4.10','156.0.150')][string[]]$CefSharpVersions = @('84.4.10','156.0.150'),
    [string[]]$Platforms = @('x64','x86'),
    [string]$CorpusScript = '',
    [switch]$PackageOnly,
    [switch]$KeepWorktrees
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../testing/TestProcess.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$runId = [Guid]::NewGuid().ToString('N').Substring(0,12)
$runRoot = Join-Path $root "artifacts/delivery/$runId"
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$checkoutParent = Join-Path ([IO.Path]::GetPathRoot($root)) ("ms-" + $runId.Substring(0,10))
if (Test-Path -LiteralPath $checkoutParent) { throw "Checkout target already exists: $checkoutParent" }
New-Item -ItemType Directory -Path $checkoutParent | Out-Null
$caseIndex = 0
$commit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source commit.' }
$patch = Join-Path $runRoot 'source.patch'
& git -c core.safecrlf=false -C $root diff --binary HEAD "--output=$patch"
if ($LASTEXITCODE -ne 0) { throw 'Cannot snapshot current source changes.' }
$patchHash = (Get-FileHash -LiteralPath $patch -Algorithm SHA256).Hash.ToLowerInvariant()
if ($CorpusScript) { $CorpusScript = (Resolve-Path -LiteralPath $CorpusScript).Path }
$results = @()

function Invoke-Logged([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory, [string]$Log) {
    $run = Invoke-TestProcess -FilePath $Executable -ArgumentList $Arguments -WorkingDirectory $WorkingDirectory -StdoutPath $Log -TimeoutSeconds 900
    Assert-TestProcess $run $Executable
}

foreach ($version in $CefSharpVersions) {
    foreach ($platform in $Platforms) {
        if ($platform -notin @('x64','x86')) { throw "Unsupported platform: $platform" }
        $caseRoot = Join-Path $runRoot "$version-$platform"
        $checkout = Join-Path $checkoutParent ([string]$caseIndex)
        $caseIndex++
        New-Item -ItemType Directory -Path $caseRoot -Force | Out-Null
        Invoke-Logged 'git' @('-C',$root,'worktree','add','--detach',$checkout,$commit) $root (Join-Path $caseRoot 'checkout.log')
        if ((Get-Item -LiteralPath $patch).Length -gt 0) {
            # Install attributes first so pinned binary inputs keep their exact upstream bytes.
            Invoke-Logged 'git' @('-c','core.autocrlf=false','-c','core.eol=lf','-C',$checkout,'apply','--include=.gitattributes','--whitespace=nowarn',$patch) $root (Join-Path $caseRoot 'patch-attributes.log')
            Invoke-Logged 'git' @('-c','core.autocrlf=false','-c','core.eol=lf','-C',$checkout,'apply','--exclude=.gitattributes','--whitespace=nowarn',$patch) $root (Join-Path $caseRoot 'patch.log')
        }
        $record = [ordered]@{ version=$version; platform=$platform; commit=$commit; sourcePatchSha256=$patchHash; status='failed'; checkout=$checkout }
        try {
            if ($PackageOnly) { $record.baseline = 'skipped-package-only' }
            else {
                Invoke-Logged 'powershell' @('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $checkout 'scripts/compatibility/Run-Baseline.ps1'),'-Platform',$platform,'-CefSharpVersion',$version) $checkout (Join-Path $caseRoot 'baseline.log')
                $record.baseline = 'passed'
                $baselineRoot = Get-ChildItem -LiteralPath (Join-Path $checkout "artifacts/baseline/$version-$platform") -Directory | Select-Object -First 1
                if (!$baselineRoot) { throw 'Baseline evidence is missing.' }
                Copy-Item -LiteralPath $baselineRoot.FullName -Destination (Join-Path $caseRoot 'baseline') -Recurse
            }
            $feed = Join-Path $caseRoot 'feed'
            $packageVersion = "3.0.0-validation.$runId.$version.$platform"
            Invoke-Logged 'dotnet' @('pack',(Join-Path $checkout 'Mzying2001.MonkeySharp.Core/Mzying2001.MonkeySharp.Core.csproj'),'-c','Release','-p:Platform=AnyCPU',"-p:Version=$packageVersion","-p:PackageVersion=$packageVersion",'-o',$feed,'--nologo') $checkout (Join-Path $caseRoot 'pack-core.log')
            Invoke-Logged 'dotnet' @('pack',(Join-Path $checkout 'Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj'),'-c','Release',"-p:Platform=$platform","-p:CefSharpVersion=$version","-p:Version=$packageVersion","-p:PackageVersion=$packageVersion",'-o',$feed,'--nologo') $checkout (Join-Path $caseRoot 'pack-adapter.log')
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $suffix = if ($version -eq '84.4.10') { '84' } else { '' }
            $adapter = Get-Item -LiteralPath (Join-Path $feed "Mzying2001.MonkeySharp.CefSharp$suffix.$platform.$packageVersion.nupkg")
            $zip = [IO.Compression.ZipFile]::OpenRead($adapter.FullName)
            try {
                $snapshots = Get-Content -LiteralPath (Join-Path $checkout 'Mzying2001.MonkeySharp.CefSharp/Protocol/public-suffix-snapshots.json') -Raw | ConvertFrom-Json
                $snapshotFiles = @($snapshots | ForEach-Object { 'psl-' + ([version]$_.chromiumVersion).Major + '.dat.gz' })
                foreach ($name in @('public-suffix-snapshots.json','THIRD-PARTY-NOTICES.md') + $snapshotFiles) {
                    if (!@($zip.Entries | Where-Object FullName -EQ "third-party/public-suffix/$name").Count) { throw "Package lacks $name" }
                }
            } finally { $zip.Dispose() }
            $record.packageContents = 'passed'
            $consumer = Join-Path $caseRoot 'consumer'
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PackageConsumer') -Destination $consumer -Recurse
            $escapedFeed = [Security.SecurityElement]::Escape($feed)
            [IO.File]::WriteAllText((Join-Path $consumer 'NuGet.Config'), "<configuration><packageSources><clear/><add key=`"local`" value=`"$escapedFeed`"/><add key=`"nuget`" value=`"https://api.nuget.org/v3/index.json`"/></packageSources></configuration>")
            $project = Join-Path $consumer 'PackageConsumer.csproj'
            $properties = @("-p:Platform=$platform","-p:CefSharpVersion=$version","-p:MonkeySharpPackageVersion=$packageVersion")
            Invoke-Logged 'dotnet' (@('restore',$project,'--configfile',(Join-Path $consumer 'NuGet.Config'),'--nologo') + $properties) $consumer (Join-Path $caseRoot 'consumer-restore.log')
            Invoke-Logged 'dotnet' (@('build',$project,'-c','Release','--no-restore','--nologo') + $properties) $consumer (Join-Path $caseRoot 'consumer-build.log')
            $report = Join-Path $caseRoot 'consumer-result.json'
            $arguments = @($report,$version)
            if ($CorpusScript) { $arguments += $CorpusScript }
            Invoke-Logged (Join-Path $consumer "bin/$platform/Release/net462/MonkeySharp.PackageConsumer.exe") $arguments $consumer (Join-Path $caseRoot 'consumer.log')
            $consumerResult = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
            if (!$consumerResult.success -or $consumerResult.platform -ne $platform) { throw 'The packaged consumer did not pass.' }
            $record.consumer = $consumerResult
            $corpusOutput = Join-Path $caseRoot 'corpus-consumer'
            Invoke-Logged 'node' @((Join-Path $checkout 'scripts/compatibility/Run-Corpus.cjs'),'--manager','monkeysharp','--host',
                (Join-Path $consumer "bin/$platform/Release/net462/MonkeySharp.PackageConsumer.exe"),'--expected-cef-version',$version,'--expected-platform',$platform,'--output',$corpusOutput) $checkout (Join-Path $caseRoot 'consumer-corpus.log')
            $record.consumerCorpus = Get-Content -LiteralPath (Join-Path $corpusOutput 'results.json') -Raw -Encoding UTF8 | ConvertFrom-Json
            if (!$PackageOnly) {
                $smokeResults = Get-ChildItem -LiteralPath (Join-Path $checkout "artifacts/baseline/$version-$platform") -Filter results.jsonl -Recurse | Select-Object -First 1
                if (!$smokeResults) { throw 'Native smoke evidence is missing.' }
                Copy-Item -LiteralPath $smokeResults.FullName -Destination (Join-Path $caseRoot 'smoke-results.jsonl')
                Copy-Item -LiteralPath (Join-Path $smokeResults.DirectoryName 'cdp-probe.jsonl') -Destination (Join-Path $caseRoot 'cdp-probe.jsonl')
                $nativeProbes = Join-Path $smokeResults.Directory.Parent.FullName 'oopif'
                if (!(Test-Path -LiteralPath $nativeProbes)) { throw 'Native OOPIF evidence is missing.' }
                Copy-Item -LiteralPath $nativeProbes -Destination (Join-Path $caseRoot 'native-probes') -Recurse
                $record.oopif = 'passed'
                $baselineCorpus = Get-Item -LiteralPath (Join-Path $smokeResults.Directory.Parent.FullName 'corpus/results.json')
                if (!$baselineCorpus) { throw 'Baseline corpus evidence is missing.' }
                Copy-Item -LiteralPath $baselineCorpus.FullName -Destination (Join-Path $caseRoot 'baseline-corpus.json')
            }
            $record.status = if ($PackageOnly) { 'package-only-passed' } else { 'passed' }
        }
        catch { $record.error = $_.Exception.Message; throw }
        finally {
            $baselineParent = Join-Path $checkout "artifacts/baseline/$version-$platform"
            if (!$PackageOnly -and (Test-Path -LiteralPath $baselineParent) -and !(Test-Path -LiteralPath (Join-Path $caseRoot 'baseline'))) {
                $partial = Get-ChildItem -LiteralPath $baselineParent -Directory | Select-Object -First 1
                if ($partial) { Copy-Item -LiteralPath $partial.FullName -Destination (Join-Path $caseRoot 'baseline') -Recurse }
            }
            $results += [pscustomobject]$record
            [IO.File]::WriteAllText((Join-Path $runRoot 'results.json'), (ConvertTo-Json -InputObject $results -Depth 30))
            if ($record.status -in @('passed','package-only-passed') -and !$KeepWorktrees) {
                $resolved = (Resolve-Path -LiteralPath $checkout).Path
                $intended = [IO.Path]::GetFullPath($checkoutParent).TrimEnd('\') + '\'
                $actualRoot = (& git -C $resolved rev-parse --show-toplevel).Trim().Replace('/','\')
                if (!$resolved.StartsWith($intended,[StringComparison]::OrdinalIgnoreCase) -or $actualRoot -ne $resolved.TrimEnd('\')) { throw "Unsafe worktree cleanup target: $resolved" }
                Invoke-Logged 'git' @('-C',$root,'worktree','remove','--force',$resolved) $root (Join-Path $caseRoot 'cleanup.log')
            }
        }
        Write-Output "$(if ($PackageOnly) {'Package consumer'} else {'Delivery'}) passed: $version/$platform"
    }
}
Write-Output "Delivery report: $(Join-Path $runRoot 'results.json')"
if (!$KeepWorktrees -and !(Get-ChildItem -LiteralPath $checkoutParent -Force)) { Remove-Item -LiteralPath $checkoutParent }
exit 0
