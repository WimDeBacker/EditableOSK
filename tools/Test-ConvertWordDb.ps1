# Test-ConvertWordDb.ps1 - checks tools\ConvertWordDb.ps1 against source files of different shapes.
#
# Sources shaped like the database builder's output must convert to well-formed XML; anything
# the converter cannot handle must make it exit 1 AND delete its output, so the build fails
# instead of shipping a .wfq the app would skip as corrupt.
#
# Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\Test-ConvertWordDb.ps1
# Exits 0 when every case behaves as expected, 1 otherwise.

$script = Join-Path $PSScriptRoot 'ConvertWordDb.ps1'
$work   = Join-Path ([System.IO.Path]::GetTempPath()) ("wfq_test_" + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($work) | Out-Null

$decl  = '<?xml version="1.0" encoding="utf-8"?>'
$nl    = "`r`n"
$words = '  <Word value="de" frequency="100" />' + $nl + '  <Word value="het" frequency="90" />' + $nl + '</WordDatabase>'

# name, source text, should the conversion succeed?
$cases = @(
    @('real shape (attributes, tag on line 2)',    ($decl + $nl + '<WordDatabase generated="x" wordCount="2">' + $nl + $words), $true),
    @('root tag without attributes',               ($decl + $nl + '<WordDatabase>' + $nl + $words),                              $true),
    @('space before the closing >',                ($decl + $nl + '<WordDatabase generated="x" >' + $nl + $words),               $true),
    @('attributes on separate lines',              ($decl + $nl + '<WordDatabase' + $nl + '    generated="x"' + $nl + '    wordCount="2">' + $nl + $words), $false),
    @('no XML declaration',                        ('<WordDatabase generated="x">' + $nl + $words),                              $false),
    @('comment line before the root',              ($decl + $nl + '<!-- built by tool -->' + $nl + '<WordDatabase generated="x">' + $nl + $words), $false),
    @('empty database (self-closing root)',        ($decl + $nl + '<WordDatabase generated="x" />'),                             $false),
    @('root already has a version attribute',      ($decl + $nl + '<WordDatabase version="1" generated="x">' + $nl + $words),    $false),
    @('only one line',                             $decl,                                                                        $false),
    @('root with no words after it',               ($decl + $nl + '<WordDatabase generated="x">' + $nl + '</WordDatabase>'),     $false)
)

$failures = 0
try {
    foreach ($c in $cases) {
        $name = $c[0]; $text = $c[1]; $shouldSucceed = $c[2]
        $src = Join-Path $work 'src.xml'
        $dst = Join-Path $work 'out.wfq'
        [System.IO.File]::WriteAllText($src, $text, (New-Object System.Text.UTF8Encoding($false)))
        # Leave a stale file behind, as an earlier successful build would have: a failure must remove it.
        [System.IO.File]::WriteAllText($dst, 'stale output from an earlier build', (New-Object System.Text.UTF8Encoding($false)))

        & powershell -NoProfile -ExecutionPolicy Bypass -File $script -Source $src -Dest $dst -Language nl *> $null
        $exit = $LASTEXITCODE
        $exists = [System.IO.File]::Exists($dst)

        $problem = $null
        if ($shouldSucceed) {
            if ($exit -ne 0) { $problem = "expected success, exit code was $exit" }
            elseif (-not $exists) { $problem = 'expected an output file, none was written' }
            else {
                try {
                    $doc = New-Object System.Xml.XmlDocument
                    $doc.Load($dst)
                    $r = $doc.DocumentElement
                    if ($r.GetAttribute('version') -ne '1' -or $r.GetAttribute('language') -ne 'nl' -or $r.GetAttribute('isPersonal') -ne 'false') {
                        $problem = 'root attributes are wrong'
                    }
                    elseif ($doc.SelectNodes('/WordDatabase/Word').Count -ne 2) { $problem = 'the <Word> entries were not preserved' }
                    elseif ($doc.SelectNodes('/WordDatabase/Candidates').Count -ne 1) { $problem = 'expected exactly one <Candidates />' }
                }
                catch { $problem = 'output is not well-formed: ' + $_.Exception.Message }
            }
        }
        else {
            if ($exit -eq 0) { $problem = 'expected the build to fail, exit code was 0' }
            elseif ($exists) { $problem = 'the bad output file was left behind (an incremental build would ship it)' }
        }

        if ($problem) { $failures++; '{0,-46} FAIL - {1}' -f $name, $problem }
        else          { '{0,-46} ok' -f $name }
    }
}
finally {
    try { [System.IO.Directory]::Delete($work, $true) } catch { }
}

if ($failures -gt 0) { "$failures case(s) failed."; exit 1 }
'all cases behave as expected.'
exit 0
