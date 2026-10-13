# Saves a screenshot of the Snapr editor window, or of the whole primary screen with -Screen.
# Used for visual QA (contrast and overlap checks). Runs as the current user; no admin needed.
param([string]$Out = "$env:TEMP\snapr-qa.png", [switch]$Screen)
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class QaWin {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
[QaWin]::SetProcessDPIAware() | Out-Null
if ($Screen) {
  $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $x = $b.X; $y = $b.Y; $w = $b.Width; $h = $b.Height
} else {
  $p = Get-Process Snapr -ErrorAction Stop | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
  if (-not $p) { throw "No visible Snapr window." }
  $r = New-Object QaWin+RECT
  [QaWin]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
  $x = $r.L; $y = $r.T; $w = $r.R - $r.L; $h = $r.B - $r.T
}
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
"Saved $Out ($w x $h)"
