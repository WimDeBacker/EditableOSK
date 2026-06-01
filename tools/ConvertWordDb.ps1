# ConvertWordDb.ps1 — converts a word-frequency source file to .wfq format.
#
# The source files (worddb_NL.xml, worddb_EN.xml) are kept as plain XML for
# human readability.  At build time this script wraps them in the .wfq schema:
# it adds version, language and isPersonal attributes to the root element and
# inserts an empty <Candidates /> section right after it.
#
# Only the first two lines are modified; the rest of the file is streamed
# verbatim in 64 KB chunks so even a 500 MB database converts in seconds.
#
# Usage:
#   ConvertWordDb.ps1 -Source path\to\worddb_NL.xml -Dest path\to\worddb_NL.wfq -Language nl

param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Dest,
    [Parameter(Mandatory)][string]$Language
)

$reader = [System.IO.StreamReader]::new($Source, [System.Text.Encoding]::UTF8)
$writer = [System.IO.StreamWriter]::new($Dest, $false, [System.Text.Encoding]::UTF8)
try {
    # Line 1: <?xml version="1.0" encoding="utf-8"?> — copy verbatim
    $writer.WriteLine($reader.ReadLine())

    # Line 2: <WordDatabase ...> — inject version, language and isPersonal
    # before the closing > of the opening tag.
    $line = $reader.ReadLine()
    $line = [System.Text.RegularExpressions.Regex]::Replace(
        $line,
        '(?=\s*>)',
        " version=""1"" language=""$Language"" isPersonal=""false""",
        [System.Text.RegularExpressions.RegexOptions]::None,
        [System.TimeSpan]::FromSeconds(5)
    )
    $writer.WriteLine($line)

    # Empty Candidates section — personal copies will populate this over time.
    $writer.WriteLine("  <Candidates />")

    # Stream the remaining content (all the <Word> elements) in 64 KB chunks.
    # This avoids loading the entire file into memory.
    $buf = [char[]]::new(65536)
    while (!$reader.EndOfStream) {
        $n = $reader.Read($buf, 0, $buf.Length)
        if ($n -gt 0) { $writer.Write($buf, 0, $n) }
    }
}
finally {
    $reader.Dispose()
    $writer.Dispose()
}
