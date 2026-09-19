# ConvertWordDb.ps1 - converts a word-frequency source file to .wfq format.
#
# The source files (worddb_NL.xml, worddb_EN.xml) are kept as plain XML for
# human readability.  At build time this script wraps them in the .wfq schema:
# it adds version, language and isPersonal attributes to the root element and
# inserts an empty <Candidates /> section right after it.
#
# Only the first two lines are modified; the rest of the file is streamed
# verbatim in 64 KB chunks so even a 500 MB database converts in seconds.
#
# That fast path assumes the source looks like the database builder's output:
#   line 1:  <?xml version="1.0" encoding="utf-8"?>
#   line 2:  <WordDatabase ...attributes...>      (whole opening tag, not self-closing)
# Rather than trusting that silently, the script checks line 2 before touching
# it and re-reads the header of what it wrote afterwards.  If anything is off it
# prints an MSBuild-style error, deletes the (bad) output, and exits 1, so the
# build FAILS instead of shipping a .wfq the app would later skip as corrupt.
# (The bad output is deleted on purpose: MSBuild treats an existing output that
# is newer than its source as up to date, so a leftover file would be shipped
# silently by the next build.)
#
# Usage:
#   ConvertWordDb.ps1 -Source path\to\worddb_NL.xml -Dest path\to\worddb_NL.wfq -Language nl

param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Dest,
    [Parameter(Mandatory)][string]$Language
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    try { [System.IO.File]::Delete($Dest) } catch { }
    # "origin : error CODE : text" is the canonical form MSBuild/Visual Studio report as an error.
    [Console]::Error.WriteLine("ConvertWordDb.ps1 : error WDB001 : $Source -> $Dest : $Message")
    exit 1
}

function Convert-Source {
    $reader = [System.IO.StreamReader]::new($Source, [System.Text.Encoding]::UTF8)
    $writer = [System.IO.StreamWriter]::new($Dest, $false, [System.Text.Encoding]::UTF8)
    try {
        # Line 1: <?xml version="1.0" encoding="utf-8"?> - copy verbatim
        $decl = $reader.ReadLine()
        # Line 2: <WordDatabase ...> - inject version, language and isPersonal
        $root = $reader.ReadLine()

        if ($null -eq $decl -or $null -eq $root) {
            throw "the source has fewer than two lines; expected the XML declaration on line 1 and the <WordDatabase> opening tag on line 2."
        }
        if ($root -notmatch '^\s*<WordDatabase(\s|>|$)') {
            throw "line 2 is not the <WordDatabase ...> opening tag (found: '$($root.Trim())'). Expected the XML declaration on line 1 and the root tag on line 2, with no comment or blank line between them."
        }
        if ($root -notmatch '>') {
            throw "the <WordDatabase> opening tag is not closed on line 2 (attributes split over several lines?). The whole tag must fit on line 2."
        }
        if ($root -match '/\s*>\s*$') {
            throw "the <WordDatabase> root tag is self-closing (an empty database); there are no words to convert."
        }

        $writer.WriteLine($decl)

        # Insert the attributes at the FIRST position followed by '>' - exactly once.  (An unbounded
        # Replace also matched the position before a space that precedes the '>', inserting the
        # attributes twice and producing a duplicate-attribute error.)
        $rx = [System.Text.RegularExpressions.Regex]::new(
            '(?=\s*>)',
            [System.Text.RegularExpressions.RegexOptions]::None,
            [System.TimeSpan]::FromSeconds(5))
        $root = $rx.Replace($root, " version=""1"" language=""$Language"" isPersonal=""false""", 1)
        $writer.WriteLine($root)

        # Empty Candidates section - personal copies will populate this over time.
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
}

# Re-reads only the HEADER of the output (root tag, first child, first word) - not the whole file,
# which would cost a full parse of a possibly huge dictionary - and checks it is what the app expects.
function Test-Output {
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.IgnoreWhitespace = $true
    $settings.IgnoreComments = $true
    $settings.IgnoreProcessingInstructions = $true

    $xr = [System.Xml.XmlReader]::Create($Dest, $settings)
    try {
        # Reading the start tag already throws on malformed markup, including a duplicated attribute.
        while ($xr.Read() -and $xr.NodeType -ne [System.Xml.XmlNodeType]::Element) { }
        if ($xr.LocalName -ne 'WordDatabase') { throw "the output's root element is '$($xr.LocalName)', expected 'WordDatabase'." }
        if ($xr.IsEmptyElement) { throw "the output's root element is empty." }
        if ($xr.GetAttribute('version') -ne '1') { throw "the output's root element has no version=""1"" attribute." }
        if ($xr.GetAttribute('language') -ne $Language) { throw "the output's root element has no language=""$Language"" attribute." }
        if ($xr.GetAttribute('isPersonal') -ne 'false') { throw "the output's root element has no isPersonal=""false"" attribute." }

        while ($xr.Read() -and $xr.NodeType -ne [System.Xml.XmlNodeType]::Element) { }
        if ($xr.LocalName -ne 'Candidates') { throw "the first child of the output's root is '$($xr.LocalName)', expected 'Candidates'." }

        while ($xr.Read() -and $xr.NodeType -ne [System.Xml.XmlNodeType]::Element) { }
        if ($xr.LocalName -ne 'Word') { throw "the output has no <Word> entries after <Candidates />." }
    }
    finally { $xr.Dispose() }
}

try {
    Convert-Source
    Test-Output
}
catch {
    Fail $_.Exception.Message
}
