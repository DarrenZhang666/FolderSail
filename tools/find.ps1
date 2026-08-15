# Lists FolderSail UI elements matching a name, with their screen rectangles.
param(
    [string]$Match = ""
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process FolderSail -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)

$all = $root.FindAll(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)

foreach ($element in $all) {
    $name = $element.Current.Name
    if ($Match -ne "" -and $name -notlike "*$Match*") { continue }
    $r = $element.Current.BoundingRectangle
    $cx = [int]($r.X + $r.Width / 2)
    $cy = [int]($r.Y + $r.Height / 2)
    "{0,-14} {1,-22} center={2},{3}" -f $element.Current.ControlType.ProgrammaticName, $name, $cx, $cy
}
