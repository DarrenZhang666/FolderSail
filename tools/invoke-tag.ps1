# Invokes the sidebar tag row whose label matches -Name.
param(
    [Parameter(Mandatory = $true)][string]$Name
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process FolderSail -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)

$textCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
$label = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $textCondition)

if ($null -eq $label) { throw "label '$Name' not found" }
$target = $label.Current.BoundingRectangle

$buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)
$buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)

foreach ($button in $buttons) {
    $r = $button.Current.BoundingRectangle
    # The tag row is the sidebar button that geometrically contains its label.
    if ($r.X -le $target.X -and $r.Y -le $target.Y -and
        ($r.X + $r.Width) -ge ($target.X + $target.Width) -and
        ($r.Y + $r.Height) -ge ($target.Y + $target.Height) -and
        $r.Width -lt 300) {
        $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
        Write-Output "invoked tag '$Name' at $($r.X),$($r.Y) size $($r.Width)x$($r.Height)"
        exit 0
    }
}

throw "no button wraps label '$Name'"
