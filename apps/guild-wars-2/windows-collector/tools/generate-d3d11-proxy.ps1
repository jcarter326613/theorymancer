[CmdletBinding(DefaultParameterSetName = "Generate")]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReferenceDll,

    [Parameter(Mandatory = $true, ParameterSetName = "Generate")]
    [string]$AssemblyOutput,

    [Parameter(Mandatory = $true, ParameterSetName = "Generate")]
    [string]$DefinitionOutput,

    [Parameter(Mandatory = $true, ParameterSetName = "Generate")]
    [string]$HeaderOutput,

    [Parameter(Mandatory = $true, ParameterSetName = "Generate")]
    [string]$AssemblyTemplate,

    [Parameter(Mandatory = $true, ParameterSetName = "Generate")]
    [string]$DefinitionTemplate,

    [Parameter(Mandatory = $true, ParameterSetName = "Generate")]
    [string]$HeaderTemplate,

    [Parameter(Mandatory = $true, ParameterSetName = "Verify")]
    [string]$CandidateDll,

    [Parameter(Mandatory = $true, ParameterSetName = "Verify")]
    [switch]$Verify
)

$ErrorActionPreference = "Stop"

function Read-UInt16([byte[]]$Bytes, [int]$Offset) {
    return [BitConverter]::ToUInt16($Bytes, $Offset)
}

function Read-UInt32([byte[]]$Bytes, [int]$Offset) {
    return [BitConverter]::ToUInt32($Bytes, $Offset)
}

function Convert-RvaToOffset([byte[]]$Bytes, [uint32]$Rva, [int]$SectionTableOffset, [uint16]$SectionCount) {
    for ($sectionIndex = 0; $sectionIndex -lt $SectionCount; $sectionIndex++) {
        $sectionOffset = $SectionTableOffset + (40 * $sectionIndex)
        $virtualSize = Read-UInt32 $Bytes ($sectionOffset + 8)
        $virtualAddress = Read-UInt32 $Bytes ($sectionOffset + 12)
        $rawSize = Read-UInt32 $Bytes ($sectionOffset + 16)
        $rawOffset = Read-UInt32 $Bytes ($sectionOffset + 20)
        $sectionSize = [Math]::Max($virtualSize, $rawSize)

        if ($Rva -ge $virtualAddress -and $Rva -lt ($virtualAddress + $sectionSize)) {
            return [int]($rawOffset + ($Rva - $virtualAddress))
        }
    }

    throw "RVA 0x{0:X8} is outside every PE section." -f $Rva
}

function Read-AsciiZeroTerminated([byte[]]$Bytes, [int]$Offset) {
    $end = $Offset
    while ($end -lt $Bytes.Length -and $Bytes[$end] -ne 0) {
        $end++
    }

    if ($end -eq $Bytes.Length) {
        throw "Unterminated export name in PE file."
    }

    return [Text.Encoding]::ASCII.GetString($Bytes, $Offset, $end - $Offset)
}

function Get-PeExports([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or (Read-UInt16 $bytes 0) -ne 0x5A4D) {
        throw "$Path is not a PE file."
    }

    $peOffset = [int](Read-UInt32 $bytes 0x3C)
    if ((Read-UInt32 $bytes $peOffset) -ne 0x00004550) {
        throw "$Path does not contain a PE signature."
    }

    $sectionCount = Read-UInt16 $bytes ($peOffset + 6)
    $optionalHeaderSize = Read-UInt16 $bytes ($peOffset + 20)
    $optionalHeaderOffset = $peOffset + 24
    if ((Read-UInt16 $bytes $optionalHeaderOffset) -ne 0x020B) {
        throw "$Path is not a 64-bit PE image."
    }

    $dataDirectoryOffset = $optionalHeaderOffset + 112
    $exportDirectoryRva = Read-UInt32 $bytes $dataDirectoryOffset
    $exportDirectorySize = Read-UInt32 $bytes ($dataDirectoryOffset + 4)
    if ($exportDirectoryRva -eq 0 -or $exportDirectorySize -eq 0) {
        throw "$Path has no export directory."
    }

    $sectionTableOffset = $optionalHeaderOffset + $optionalHeaderSize
    $exportDirectoryOffset = Convert-RvaToOffset $bytes $exportDirectoryRva $sectionTableOffset $sectionCount
    $ordinalBase = Read-UInt32 $bytes ($exportDirectoryOffset + 16)
    $functionCount = Read-UInt32 $bytes ($exportDirectoryOffset + 20)
    $nameCount = Read-UInt32 $bytes ($exportDirectoryOffset + 24)
    $functionsOffset = Convert-RvaToOffset $bytes (Read-UInt32 $bytes ($exportDirectoryOffset + 28)) $sectionTableOffset $sectionCount
    $namesOffset = Convert-RvaToOffset $bytes (Read-UInt32 $bytes ($exportDirectoryOffset + 32)) $sectionTableOffset $sectionCount
    $nameOrdinalsOffset = Convert-RvaToOffset $bytes (Read-UInt32 $bytes ($exportDirectoryOffset + 36)) $sectionTableOffset $sectionCount

    $exportsByOrdinal = @{}
    for ($functionIndex = 0; $functionIndex -lt $functionCount; $functionIndex++) {
        $functionRva = Read-UInt32 $bytes ($functionsOffset + (4 * $functionIndex))
        if ($functionRva -ne 0) {
            $ordinal = [int]($ordinalBase + $functionIndex)
            $exportsByOrdinal[$ordinal] = [PSCustomObject]@{
                Ordinal = $ordinal
                Names = [Collections.Generic.List[string]]::new()
            }
        }
    }

    for ($nameIndex = 0; $nameIndex -lt $nameCount; $nameIndex++) {
        $functionIndex = Read-UInt16 $bytes ($nameOrdinalsOffset + (2 * $nameIndex))
        $ordinal = [int]($ordinalBase + $functionIndex)
        if (-not $exportsByOrdinal.ContainsKey($ordinal)) {
            throw "Export name points to a missing function ordinal $ordinal in $Path."
        }

        $nameRva = Read-UInt32 $bytes ($namesOffset + (4 * $nameIndex))
        $nameOffset = Convert-RvaToOffset $bytes $nameRva $sectionTableOffset $sectionCount
        $exportsByOrdinal[$ordinal].Names.Add((Read-AsciiZeroTerminated $bytes $nameOffset))
    }

    return @($exportsByOrdinal.Values | Sort-Object Ordinal)
}

function Render-Template([string]$TemplatePath, [string]$Marker, [string[]]$Replacement) {
    $template = [IO.File]::ReadAllText($TemplatePath)
    if ($template.IndexOf($Marker, [StringComparison]::Ordinal) -lt 0) {
        throw "Template $TemplatePath does not contain $Marker."
    }
    if ($template.IndexOf($Marker, $template.IndexOf($Marker, [StringComparison]::Ordinal) + $Marker.Length,
                         [StringComparison]::Ordinal) -ge 0) {
        throw "Template $TemplatePath contains $Marker more than once."
    }

    return $template.Replace($Marker, [string]::Join("`r`n", $Replacement))
}

$referenceExports = Get-PeExports $ReferenceDll

if ($Verify) {
    $candidateExports = Get-PeExports $CandidateDll
    $referenceShape = @($referenceExports | ForEach-Object { "{0}:{1}" -f $_.Ordinal, ($_.Names -join ",") })
    $candidateShape = @($candidateExports | ForEach-Object { "{0}:{1}" -f $_.Ordinal, ($_.Names -join ",") })

    $difference = Compare-Object $referenceShape $candidateShape
    if ($null -ne $difference) {
        $difference | Format-Table -AutoSize | Out-String | Write-Error
        throw "The proxy export surface does not match $ReferenceDll."
    }

    Write-Output "Verified $($candidateExports.Count) D3D11 exports against $ReferenceDll."
    exit 0
}

foreach ($export in $referenceExports) {
    if ($export.Ordinal -gt [UInt16]::MaxValue) {
        throw "Export ordinal $($export.Ordinal) cannot be resolved with GetProcAddress."
    }
    if ($export.Names.Count -gt 1) {
        throw "Export ordinal $($export.Ordinal) has aliases, which this proxy generator does not support."
    }
    foreach ($name in $export.Names) {
        if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
            throw "Export name '$name' cannot be represented by the MASM proxy generator."
        }
    }
}

$assemblyStubs = [Collections.Generic.List[string]]::new()
$definitionExports = [Collections.Generic.List[string]]::new()
$headerExports = [Collections.Generic.List[string]]::new()

foreach ($export in $referenceExports) {
    $symbol = "proxy_ordinal_$($export.Ordinal)"
    $recordDiagnostics = $export.Names -contains "D3D11CreateDevice" -or $export.Names -contains "D3D11CreateDeviceAndSwapChain"
    $diagnosticsFlag = if ($recordDiagnostics) { 1 } else { 0 }
    $assemblyStubs.Add("PUBLIC $symbol")
    $assemblyStubs.Add("$symbol PROC")
    $assemblyStubs.Add("    mov r11d, $($export.Ordinal)")
    $assemblyStubs.Add("    mov r10d, $diagnosticsFlag")
    $assemblyStubs.Add("    jmp ProxyDispatch")
    $assemblyStubs.Add("$symbol ENDP")
    $assemblyStubs.Add("")

    if ($export.Names.Count -eq 0) {
        $definitionExports.Add("    $symbol @$($export.Ordinal) NONAME")
        $headerExports.Add("    { $($export.Ordinal), `"<ordinal-only>`" },")
    } else {
        $definitionExports.Add("    $($export.Names[0])=$symbol @$($export.Ordinal)")
        $escapedName = $export.Names[0].Replace('\', '\\').Replace('"', '\"')
        $headerExports.Add("    { $($export.Ordinal), `"$escapedName`" },")
    }
}

$assemblyDirectory = Split-Path -Parent $AssemblyOutput
$definitionDirectory = Split-Path -Parent $DefinitionOutput
$headerDirectory = Split-Path -Parent $HeaderOutput
New-Item -ItemType Directory -Force -Path $assemblyDirectory, $definitionDirectory, $headerDirectory | Out-Null
Set-Content -Path $AssemblyOutput -Value (Render-Template $AssemblyTemplate "{{EXPORT_STUBS}}" $assemblyStubs) -Encoding ascii
Set-Content -Path $DefinitionOutput -Value (Render-Template $DefinitionTemplate "{{EXPORT_DEFINITIONS}}" $definitionExports) -Encoding ascii
Set-Content -Path $HeaderOutput -Value (Render-Template $HeaderTemplate "{{EXPORT_METADATA}}" $headerExports) -Encoding ascii
Write-Output "Generated $($referenceExports.Count) D3D11 forwarding stubs from $ReferenceDll."
