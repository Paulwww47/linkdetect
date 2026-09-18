# Capture clipboard images with DIB header analysis and pixel dumps.
# Usage: pwsh -STA -NoProfile -File tools\clipboard-capture.ps1 [-Seconds 900]
param(
    [int]$Seconds = 900,
    [string]$LogPath = "$env:TEMP\clipboard-capture.log",
    [string]$OutDir = "$env:TEMP\clipcapture"
)

Add-Type -AssemblyName PresentationCore, WindowsBase
Add-Type -Namespace Native -Name ClipboardApi -MemberDefinition @'
[DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
'@

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Set-Content -Path $LogPath -Value "capture start $(Get-Date -Format 'HH:mm:ss')"

function Write-Log {
    param([string]$Line)
    $Line | Tee-Object -FilePath $LogPath -Append | Out-Null
    Write-Output $Line
}

function Get-StreamBytes {
    param($Payload)
    if ($null -eq $Payload) { return $null }
    $stream = $Payload -as [System.IO.Stream]
    if ($stream) {
        if ($stream.CanSeek) { $stream.Position = 0 }
        $ms = New-Object System.IO.MemoryStream
        $stream.CopyTo($ms)
        return $ms.ToArray()
    }
    if ($Payload -is [byte[]]) { return $Payload }
    return $null
}

function Save-Png {
    param([System.Windows.Media.Imaging.BitmapSource]$Source, [string]$Path)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($Source))
    $fs = [System.IO.File]::Create($Path)
    try { $encoder.Save($fs) } finally { $fs.Dispose() }
}

function Get-PixelReport {
    param([System.Windows.Media.Imaging.BitmapSource]$Source, [string]$Tag, [string]$Dir)

    $bgra = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap($Source, ([System.Windows.Media.PixelFormats]::Bgra32), $null, 0)
    $w = $bgra.PixelWidth
    $h = $bgra.PixelHeight
    $stride = $w * 4
    $pixels = New-Object byte[] ($stride * $h)
    $bgra.CopyPixels($pixels, $stride, 0)

    $minA = 255; $maxA = 0; $zero = 0; $opaqueCount = 0
    $nonBlack = 0
    $colors = New-Object 'System.Collections.Generic.HashSet[int]'
    $step = [math]::Max(1, [int](($w * $h) / 200000))

    for ($y = 0; $y -lt $h; $y++) {
        $row = $y * $stride
        for ($x = 0; $x -lt $w; $x++) {
            $i = $row + $x * 4
            $b = $pixels[$i]; $g = $pixels[$i + 1]; $r = $pixels[$i + 2]; $a = $pixels[$i + 3]
            if ($a -lt $minA) { $minA = $a }
            if ($a -gt $maxA) { $maxA = $a }
            if ($a -eq 0) { $zero++ }
            if ($a -eq 255) { $opaqueCount++ }
            if ($r -ne 0 -or $g -ne 0 -or $b -ne 0) { $nonBlack++ }
            if ((($y * $w + $x) % $step) -eq 0) {
                [void]$colors.Add(($r -shl 16) -bor ($g -shl 8) -bor $b)
            }
        }
    }

    $total = $w * $h
    Write-Log ("    [{0}] {1}x{2} fmt={3} alpha[min={4} max={5} zero={6}% ff={7}%] rgbNonBlack={8}% sampledColors={9}" -f `
        $Tag, $w, $h, $Source.Format, $minA, $maxA, `
        [math]::Round(100 * $zero / $total, 1), [math]::Round(100 * $opaqueCount / $total, 1), `
        [math]::Round(100 * $nonBlack / $total, 1), $colors.Count)

    $base = Join-Path $Dir $Tag
    Save-Png -Source $bgra -Path "$base-asdecoded.png"

    $forced = [byte[]]::new($pixels.Length)
    [Array]::Copy($pixels, $forced, $pixels.Length)
    for ($i = 3; $i -lt $forced.Length; $i += 4) { $forced[$i] = 255 }
    $opaque = [System.Windows.Media.Imaging.BitmapSource]::Create(
        $w, $h, $bgra.DpiX, $bgra.DpiY,
        [System.Windows.Media.PixelFormats]::Bgra32, $null, $forced, $stride)
    $opaque.Freeze()
    Save-Png -Source $opaque -Path "$base-alpha255.png"
}

function Show-DibHeader {
    param([byte[]]$Bytes, [string]$Path)
    if ($null -eq $Bytes) { Write-Log '    DIB: no payload'; return }
    [System.IO.File]::WriteAllBytes($Path, $Bytes)
    $biSize = [BitConverter]::ToUInt32($Bytes, 0)
    $biWidth = [BitConverter]::ToInt32($Bytes, 4)
    $biHeight = [BitConverter]::ToInt32($Bytes, 8)
    $biBitCount = [BitConverter]::ToUInt16($Bytes, 14)
    $biCompression = [BitConverter]::ToUInt32($Bytes, 16)
    $biSizeImage = [BitConverter]::ToUInt32($Bytes, 20)
    Write-Log ("    DIB bytes={0} headerSize={1} w={2} h={3} bitCount={4} compression={5} sizeImage={6}" -f `
        $Bytes.Length, $biSize, $biWidth, $biHeight, $biBitCount, $biCompression, $biSizeImage)
    if ($biSize -ge 108) {
        $alphaMask = [BitConverter]::ToUInt32($Bytes, 52)
        $redMask = [BitConverter]::ToUInt32($Bytes, 40)
        Write-Log ("    DIB V4/V5 redMask=0x{0:X} alphaMask=0x{1:X}" -f $redMask, $alphaMask)
    }
    if ($biBitCount -eq 32 -and $biSize -eq 40) {
        $zeroFourth = 0; $checked = 0
        $start = [int]$biSize
        for ($i = $start + 3; $i -lt [math]::Min($Bytes.Length, $start + 400000); $i += 4) {
            $checked++
            if ($Bytes[$i] -eq 0) { $zeroFourth++ }
        }
        if ($checked -gt 0) {
            Write-Log ("    DIB 4th byte zero in {0}% of {1} sampled pixels (BITMAPINFOHEADER 32bpp BI_RGB => alpha undefined)" -f `
                [math]::Round(100 * $zeroFourth / $checked, 1), $checked)
        }
    }
}

Write-Output "logging to $LogPath, dumps to $OutDir"
$deadline = (Get-Date).AddSeconds($Seconds)
$last = [Native.ClipboardApi]::GetClipboardSequenceNumber()

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 200
    $seq = [Native.ClipboardApi]::GetClipboardSequenceNumber()
    if ($seq -eq $last) { continue }
    $last = $seq
    Start-Sleep -Milliseconds 400

    $stamp = Get-Date -Format 'HHmmss'
    $dir = Join-Path $OutDir "$stamp-$seq"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    Write-Log ("=== {0} seq={1} dir={2}" -f (Get-Date -Format 'HH:mm:ss'), $seq, $dir)
    try { $data = [System.Windows.Clipboard]::GetDataObject() } catch {
        Write-Log ("    GetDataObject threw: " + $_.Exception.Message); continue
    }
    if ($null -eq $data) { Write-Log '    clipboard empty'; continue }

    $formats = $data.GetFormats($false)
    Write-Log ("    formats: " + ($formats -join ' | '))

    foreach ($name in @('DeviceIndependentBitmap', 'Format17')) {
        try {
            if ($data.GetDataPresent($name, $false)) {
                $bytes = Get-StreamBytes ($data.GetData($name, $false))
                Write-Log ("    --- CF '{0}'" -f $name)
                Show-DibHeader -Bytes $bytes -Path (Join-Path $dir "$name.bin")
            }
        } catch { Write-Log ("    {0} threw: {1}" -f $name, $_.Exception.Message) }
    }

    foreach ($name in @('PNG', 'image/png')) {
        try {
            if ($data.GetDataPresent($name, $false)) {
                $bytes = Get-StreamBytes ($data.GetData($name, $false))
                if ($bytes) {
                    $magic = ($bytes[0..7] | ForEach-Object { $_.ToString('X2') }) -join ' '
                    Write-Log ("    CF '{0}' bytes={1} magic={2}" -f $name, $bytes.Length, $magic)
                    [System.IO.File]::WriteAllBytes((Join-Path $dir ("{0}.bin" -f ($name -replace '[^A-Za-z0-9]', '_'))), $bytes)
                } else {
                    Write-Log ("    CF '{0}' present but not a byte stream ({1})" -f $name, $data.GetData($name, $false).GetType().FullName)
                }
            }
        } catch { Write-Log ("    {0} threw: {1}" -f $name, $_.Exception.Message) }
    }

    Write-Log ("    ContainsImage=" + [System.Windows.Clipboard]::ContainsImage())
    try {
        $img = [System.Windows.Clipboard]::GetImage()
        if ($null -eq $img) { Write-Log '    GetImage()=null' }
        else { Get-PixelReport -Source $img -Tag 'clipboard' -Dir $dir }
    } catch { Write-Log ("    GetImage threw: " + $_.Exception.Message) }

    Write-Log ("    image-processing.log lines=" + @(Get-Content "$env:LOCALAPPDATA\LinkDetect\image-processing.log" -ErrorAction SilentlyContinue).Count)
}

Write-Log 'capture end'
