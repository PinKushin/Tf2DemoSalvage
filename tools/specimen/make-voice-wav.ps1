<#
.SYNOPSIS
Writes the specimen's voice input: 5 s of 16-bit mono PCM at 11025 Hz, a 440 Hz / 660 Hz tone alternating every
half second at half amplitude, so a codec's voice-activity gate sees signal the whole time.

.DESCRIPTION
`voice_inputfromfile 1` makes the engine read `voice_input.wav` instead of the microphone (the name is a string in
every era's engine.dll, 2007..live). 11025 Hz is the rate assumed for the era voice path; a mismatch only shifts pitch.
The file is generated rather than recorded so it carries nothing personal and is identical on every machine.

.PARAMETER Path
Where to write the WAV (refuses to overwrite).
#>
param([Parameter(Mandatory)][string]$Path)

$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Path) { throw "$Path exists; refusing to overwrite." }

$rate = 11025
$samples = $rate * 5
$data = New-Object byte[] ($samples * 2)
for ($i = 0; $i -lt $samples; $i++) {
    $freq = if ([math]::Floor($i / ($rate / 2)) % 2 -eq 0) { 440 } else { 660 }
    $value = [int16]([math]::Round(16383 * [math]::Sin(2 * [math]::PI * $freq * $i / $rate)))
    [BitConverter]::GetBytes($value).CopyTo($data, $i * 2)
}

$stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew)
try {
    $w = New-Object IO.BinaryWriter($stream)
    $w.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([int](36 + $data.Length))
    $w.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $w.Write([int]16)
    $w.Write([int16]1); $w.Write([int16]1); $w.Write([int]$rate); $w.Write([int]($rate * 2))
    $w.Write([int16]2); $w.Write([int16]16)
    $w.Write([Text.Encoding]::ASCII.GetBytes('data')); $w.Write([int]$data.Length); $w.Write($data)
    $w.Flush()
} finally { $stream.Dispose() }
