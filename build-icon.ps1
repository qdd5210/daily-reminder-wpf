Add-Type -AssemblyName PresentationCore, WindowsBase
$assetDirectory = Join-Path $PSScriptRoot 'assets'
$frames = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $drawing = New-Object System.Windows.Media.DrawingVisual
    $context = $drawing.RenderOpen()
    $context.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / 256), ($size / 256)))
    $shell = New-Object System.Windows.Media.LinearGradientBrush ([System.Windows.Media.ColorConverter]::ConvertFromString('#293f5b')), ([System.Windows.Media.ColorConverter]::ConvertFromString('#0e192b')), 45
    $context.DrawRoundedRectangle($shell, $null, (New-Object System.Windows.Rect 8,8,240,240),55,55)
    $outline = New-Object System.Windows.Media.Pen ([System.Windows.Media.BrushConverter]::new().ConvertFromString('#8057728c')),2
    $context.DrawRoundedRectangle($null, $outline, (New-Object System.Windows.Rect 9,9,238,238),54,54)
    $letter = New-Object System.Windows.Media.LinearGradientBrush ([System.Windows.Media.ColorConverter]::ConvertFromString('#adf4ff')), ([System.Windows.Media.ColorConverter]::ConvertFromString('#49c7b8')), 65
    $geometry = [System.Windows.Media.Geometry]::Parse('F0 M148 48h30v159h-29v-12a58 58 0 1 1-1-94V48 Z M115 113a33 33 0 1 0 0 66a33 33 0 0 0 0-66 Z')
    $context.DrawGeometry($letter,$null,$geometry)
    $context.DrawEllipse([System.Windows.Media.BrushConverter]::new().ConvertFromString('#14273b'),$null,(New-Object System.Windows.Point 185,67),30,30)
    $context.DrawEllipse([System.Windows.Media.BrushConverter]::new().ConvertFromString('#ffc574'),$null,(New-Object System.Windows.Point 185,67),22,22)
    $hands = New-Object System.Windows.Media.Pen ([System.Windows.Media.BrushConverter]::new().ConvertFromString('#243347')),5
    $hands.StartLineCap = $hands.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $context.DrawGeometry($null,$hands,[System.Windows.Media.Geometry]::Parse('M185 54v14l9 6'))
    $bells = New-Object System.Windows.Media.Pen ([System.Windows.Media.BrushConverter]::new().ConvertFromString('#ffc574')),5
    $bells.StartLineCap = $bells.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $context.DrawGeometry($null,$bells,[System.Windows.Media.Geometry]::Parse('M164 43l-5 6 M206 43l5 6'))
    $context.Pop()
    $context.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size,$size,96,96,([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($drawing)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = New-Object System.IO.MemoryStream
    $encoder.Save($memory)
    $frames += [pscustomobject]@{Size=$size;Data=$memory.ToArray()}
    if ($size -eq 256) { [System.IO.File]::WriteAllBytes((Join-Path $assetDirectory 'app-icon.png'),$memory.ToArray()) }
    $memory.Dispose()
}
$stream = [System.IO.File]::Create((Join-Path $assetDirectory 'app-icon.ico'))
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$frame.Data.Length)
    $writer.Write([uint32]$offset)
    $offset += $frame.Data.Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }
$writer.Dispose()
