@echo off
setlocal
set "__MS_SOURCE=%~f0"
set "__MS_DIR=%~dp0"
rem MineScript test checker - standalone Windows batch launcher.
rem The PowerShell logic is embedded below, so no Python or separate .ps1 is needed.
set "_MS_PS=%TEMP%\minescript_checker_%RANDOM%_%RANDOM%.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$s=[IO.File]::ReadAllText($env:__MS_SOURCE);$marker='#--MINESCRIPT_PS_PAYLOAD--';$i=$s.LastIndexOf($marker);if($i -lt 0){exit 2};[IO.File]::WriteAllText($env:_MS_PS,$s.Substring($i+$marker.Length).TrimStart([char]13,[char]10),[Text.UTF8Encoding]::new($false))" 2>nul
if errorlevel 1 (
    echo ERROR: Could not extract the embedded PowerShell checker.
    exit /b 2
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%_MS_PS%" %*
set "_MS_RESULT=%ERRORLEVEL%"
del /q "%_MS_PS%" >nul 2>nul
exit /b %_MS_RESULT%
#--MINESCRIPT_PS_PAYLOAD--
param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
$ErrorActionPreference = 'Stop'

function Usage {
    Write-Host 'Usage: checker.bat "path\to\robot.exe" [options]'
    Write-Host '       checker.bat "dotnet run --project Minescript --" [options]'
    Write-Host ''
    Write-Host 'Options:'
    Write-Host '  --tests DIR         Tests directory (default: tests next to checker.bat)'
    Write-Host '  -t N, --only N      Only test number N (repeatable)'
    Write-Host '  --strict-eol        Compare line endings too'
    Write-Host '  --timeout SECONDS   Per-test timeout (default: 10)'
    Write-Host '  --keep DIR          Keep output logs in this directory'
    Write-Host '  -h, --help          Show this help'
}

if (-not $Arguments -or @('-h','--help','/?') -contains $Arguments[0]) { Usage; exit 0 }
$exe = $Arguments[0]
$tests = Join-Path $env:__MS_DIR 'tests'
$only = [System.Collections.Generic.HashSet[int]]::new()
$strict = $false
$timeout = 10.0
$keep = $null
for ($i = 1; $i -lt $Arguments.Count; $i++) {
    $flag = $Arguments[$i]
    switch ($flag) {
        { $_ -in '-t','--only','--tests','--timeout','--keep' } {
            if ($i + 1 -ge $Arguments.Count) { throw "Missing value after $flag" }
            $value = $Arguments[++$i]
            switch ($flag) {
                { $_ -in '-t','--only' } { $n = 0; if (-not [int]::TryParse($value,[ref]$n)) { throw "Invalid test number: $value" }; [void]$only.Add($n); break }
                '--tests' { $tests = $value; break }
                '--timeout' { $timeout = [double]::Parse($value,[Globalization.CultureInfo]::InvariantCulture); break }
                '--keep' { $keep = $value; break }
            }
            break
        }
        '--strict-eol' { $strict = $true; break }
        default { throw "Unknown option: $flag" }
    }
}
if ($timeout -le 0) { throw 'Timeout must be positive' }
if (-not (Test-Path -LiteralPath $tests -PathType Container)) { throw "Tests directory not found: $tests" }
$tests = (Resolve-Path -LiteralPath $tests).Path

# A literal executable path containing spaces is treated as one token.
# Otherwise, split a quoted command string (such as dotnet run ... --).
if (Test-Path -LiteralPath $exe -PathType Leaf) { $command = @($exe) }
else {
    $command = @([regex]::Matches($exe, '(?:[^\s"]|"[^"]*")+') | ForEach-Object { $_.Value.Trim('"') })
}
if (-not $command.Count) { throw 'No executable command provided' }

$names = @{}
$namesPath = Join-Path $tests 'names.txt'
if (Test-Path -LiteralPath $namesPath) {
    foreach ($line in [IO.File]::ReadAllLines($namesPath)) {
        if ($line -match '^(\S+)\s+(.*)$') { $names[$Matches[1]] = $Matches[2] }
    }
}
$nums = @(Get-ChildItem -LiteralPath $tests -File | ForEach-Object {
    if ($_.Name -match '^input_(\d+)\.txt$') { [int]$Matches[1] }
} | Sort-Object -Unique)
if ($only.Count -gt 0) { $nums = @($nums | Where-Object { $only.Contains($_) }) }

if ($keep) { $outDir = [IO.Path]::GetFullPath($keep) }
else { $outDir = Join-Path ([IO.Path]::GetTempPath()) ('ms_out_' + [guid]::NewGuid().ToString('N').Substring(0,12)) }
[void][IO.Directory]::CreateDirectory($outDir)
$failed = [System.Collections.Generic.List[int]]::new()
$utf8 = [Text.UTF8Encoding]::new($false)

function QuoteArg([string]$arg) {
    # Correct Windows command-line escaping, including trailing backslashes.
    if ($arg -notmatch '[\s"]' -and $arg.Length -gt 0) { return $arg }
    return '"' + ([regex]::Replace($arg, '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"'
}
function Normalize([string]$s, [bool]$strictEol) {
    if ($strictEol) { return $s }
    return $s.Replace("`r`n", "`n").TrimEnd([char]10)
}
foreach ($n in $nums) {
    $id = '{0:D3}' -f $n
    $inp = Join-Path $tests ("input_$id.txt")
    $exp = Join-Path $tests ("expected_output_$id.txt")
    $out = Join-Path $outDir ("output_$id.txt")
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Force }
    $reason = $null
    if (-not (Test-Path -LiteralPath $exp)) { $reason = 'expected file is missing' }
    else {
        $p = $null
        try {
            $psi = [Diagnostics.ProcessStartInfo]::new()
            $psi.FileName = $command[0]
            $argsList = @($command | Select-Object -Skip 1) + @('-q', $inp, $out)
            $psi.Arguments = ($argsList | ForEach-Object { QuoteArg $_ }) -join ' '
            $psi.UseShellExecute = $false
            $psi.CreateNoWindow = $true
            $psi.RedirectStandardOutput = $true
            $psi.RedirectStandardError = $true
            $p = [Diagnostics.Process]::Start($psi)
            if (-not $p.WaitForExit([int][Math]::Ceiling($timeout * 1000))) {
                try { $p.Kill() } catch {}
                $reason = 'timeout (hang? infinite loop in your code?)'
            } else {
                $stdout = $p.StandardOutput.ReadToEnd()
                $stderr = $p.StandardError.ReadToEnd()
                if ($p.ExitCode -ne 0) {
                    $detail = if ($stderr) { $stderr } else { $stdout }
                    if ($detail.Length -gt 600) { $detail = $detail.Substring($detail.Length - 600) }
                    $reason = "exit code $($p.ExitCode)`n$detail"
                }
            }
        } catch { $reason = "could not launch or run command: $($_.Exception.Message)" }
        finally { if ($p) { $p.Dispose() } }
    }
    if (-not $reason) {
        if (-not (Test-Path -LiteralPath $out)) { $reason = 'no output file was created' }
        else {
            $gotText = Normalize ([IO.File]::ReadAllText($out, $utf8)) $strict
            $wantText = Normalize ([IO.File]::ReadAllText($exp, $utf8)) $strict
            $got = $gotText.Split([char]10)
            $want = $wantText.Split([char]10)
            for ($j = 0; $j -lt [Math]::Max($got.Length, $want.Length); $j++) {
                $g = if ($j -lt $got.Length) { $got[$j] } else { '<missing>' }
                $w = if ($j -lt $want.Length) { $want[$j] } else { '<missing>' }
                if ($g -cne $w) {
                    $reason = "first difference at log line $($j+1):`n    expected: '$w'`n    got:      '$g'"
                    break
                }
            }
        }
    }
    if ($reason) {
        $failed.Add($n)
        $name = if ($names.ContainsKey($id)) { $names[$id] } else { '' }
        Write-Host "FAIL #$id $name`n  $reason`n  input: $inp"
    }
}
Write-Host "`n$($nums.Count - $failed.Count)/$($nums.Count) passed. Your outputs are in $outDir"
if ($failed.Count) { Write-Host ('failed: ' + (($failed | ForEach-Object { '{0:D3}' -f $_ }) -join ', ')) }
if ($failed.Count) { exit 1 } else { exit 0 }
