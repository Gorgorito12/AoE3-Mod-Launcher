# Presses the launcher's real controls through UI Automation: no cursor, no keyboard, no focus.
# Called by `testbed.py uia`, which supplies -ProcessId. Prints JSON. Keep this file ASCII.
#
#   tree [-Window TEXT]                every interactive control of the run's windows
#   invoke -Id NAME                    a button, a link, a menu item
#   toggle -Id NAME                    a checkbox or switch; prints its new state
#   select -Id NAME -Value TEXT        an item of a combo box or a list
#   type -Id NAME -Value TEXT          a text field
#   read -Id NAME                      name, value, enabled, toggle state
#   wait -Id NAME [-Timeout MS]        until the control exists
#   scroll -Value PERCENT              the first thing in the run's windows that scrolls vertically
#
# -Id is the AutomationId, which WPF takes from x:Name. -ByName matches the visible name instead,
# for a control built in code.
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$Action,
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [string]$Id,
    [string]$Value,
    [string]$Window,
    [switch]$ByName,
    [int]$Timeout = 5000
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$Types = [System.Windows.Automation.ControlType]
$Interactive = @(
    $Types::Button, $Types::CheckBox, $Types::ComboBox, $Types::RadioButton, $Types::Edit,
    $Types::Hyperlink, $Types::MenuItem, $Types::TabItem, $Types::ListItem, $Types::Slider,
    $Types::Spinner, $Types::SplitButton
)

function Get-RunWindows {
    $condition = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $ProcessId)
    $found = $AE::RootElement.FindAll($Scope::Children, $condition)
    $all = @()
    foreach ($w in $found) {
        if (-not $Window -or $w.Current.Name -like "*$Window*") { $all += $w }
    }
    return $all
}

function Find-Control {
    $property = if ($ByName) { $AE::NameProperty } else { $AE::AutomationIdProperty }
    $condition = New-Object System.Windows.Automation.PropertyCondition($property, $Id)
    foreach ($w in (Get-RunWindows)) {
        $hit = $w.FindFirst($Scope::Descendants, $condition)
        if ($hit) { return $hit }
    }
    return $null
}

function Require-Control {
    $deadline = (Get-Date).AddMilliseconds($Timeout)
    do {
        $control = Find-Control
        if ($control) { return $control }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw "no control '$Id' in the run's windows"
}

function Get-Pattern($control, $pattern) {
    $out = $null
    if ($control.TryGetCurrentPattern($pattern, [ref]$out)) { return $out }
    return $null
}

function Describe($control) {
    $c = $control.Current
    $rect = $c.BoundingRectangle
    $toggle = Get-Pattern $control ([System.Windows.Automation.TogglePattern]::Pattern)
    $text = Get-Pattern $control ([System.Windows.Automation.ValuePattern]::Pattern)
    return [ordered]@{
        id      = $c.AutomationId
        name    = $c.Name
        type    = $c.ControlType.ProgrammaticName -replace '^ControlType\.', ''
        enabled = $c.IsEnabled
        visible = -not $c.IsOffscreen
        toggle  = if ($toggle) { "$($toggle.Current.ToggleState)" } else { $null }
        value   = if ($text) { $text.Current.Value } else { $null }
        rect    = if ($rect.IsEmpty) { $null } else { @([int]$rect.X, [int]$rect.Y, [int]$rect.Width, [int]$rect.Height) }
    }
}

function Print($object) { $object | ConvertTo-Json -Depth 5 -Compress }

$Depth = 0
function Invoke-Control($control) {
    if (-not $control.Current.IsEnabled) { throw "'$Id' is disabled" }
    $invoke = Get-Pattern $control ([System.Windows.Automation.InvokePattern]::Pattern)
    if ($invoke) { $invoke.Invoke(); return }
    $toggle = Get-Pattern $control ([System.Windows.Automation.TogglePattern]::Pattern)
    if ($toggle) { $toggle.Toggle(); return }
    $item = Get-Pattern $control ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($item) { $item.Select(); return }
    # A caption found by name: the button is the element above it.
    $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($control)
    if ($parent -and $Depth -lt 4) { $script:Depth = $Depth + 1; Invoke-Control $parent; return }
    throw "'$Id' takes no click ($($control.Current.ControlType.ProgrammaticName))"
}

try {
    switch ($Action) {
        'tree' {
            $controls = @()
            $condition = [System.Windows.Automation.Condition]::TrueCondition
            foreach ($w in (Get-RunWindows)) {
                foreach ($control in $w.FindAll($Scope::Descendants, $condition)) {
                    if ($Interactive -contains $control.Current.ControlType) {
                        $row = Describe $control
                        $row['window'] = $w.Current.Name
                        $controls += [pscustomobject]$row
                    }
                }
            }
            ConvertTo-Json -InputObject @($controls) -Depth 5
        }
        'invoke' { Invoke-Control (Require-Control); Print @{ ok = $true; invoked = $Id } }
        'toggle' {
            $control = Require-Control
            $toggle = Get-Pattern $control ([System.Windows.Automation.TogglePattern]::Pattern)
            if (-not $toggle) { throw "'$Id' is not a toggle" }
            $toggle.Toggle()
            Print @{ ok = $true; state = "$($toggle.Current.ToggleState)" }
        }
        'select' {
            $control = Require-Control
            $expand = Get-Pattern $control ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
            if ($expand) { $expand.Expand(); Start-Sleep -Milliseconds 150 }
            $condition = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Value)
            $item = $control.FindFirst($Scope::Descendants, $condition)
            if (-not $item) { throw "'$Id' has no item '$Value'" }
            (Get-Pattern $item ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
            if ($expand) { $expand.Collapse() }
            Print @{ ok = $true; selected = $Value }
        }
        'type' {
            $text = Get-Pattern (Require-Control) ([System.Windows.Automation.ValuePattern]::Pattern)
            if (-not $text) { throw "'$Id' takes no text" }
            $text.SetValue($Value)
            Print @{ ok = $true }
        }
        'scroll' {
            $condition = New-Object System.Windows.Automation.PropertyCondition($AE::IsScrollPatternAvailableProperty, $true)
            foreach ($w in (Get-RunWindows)) {
                foreach ($area in $w.FindAll($Scope::Descendants, $condition)) {
                    $scroll = Get-Pattern $area ([System.Windows.Automation.ScrollPattern]::Pattern)
                    if ($scroll -and $scroll.Current.VerticallyScrollable) {
                        $scroll.SetScrollPercent(-1, [double]$Value)
                        Print @{ ok = $true; scrolled = $area.Current.AutomationId }
                        exit 0
                    }
                }
            }
            throw 'nothing scrolls'
        }
        'read' { Print (Describe (Require-Control)) }
        'wait' { Print (Describe (Require-Control)) }
        default { throw "unknown action '$Action'" }
    }
}
catch {
    Print @{ ok = $false; error = $_.Exception.Message }
    exit 1
}
