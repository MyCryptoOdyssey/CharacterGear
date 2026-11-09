# Fails (exit 1) if the plugin source does anything but read. Allowed game calls: RequestId (appraise),
# AddChatText / AddStatusText (text in your own chat window), IsValidObject, OpenedContainer (reads which container is open).
param([string]$src)
$bad = @(
  'Actions\s*\.\s*(?!RequestId\b|AddChatText\b|AddStatusText\b|IsValidObject\b|OpenedContainer\b(?!\s*=[^=]))[A-Za-z_]+',
  '\b(ApplyItem|AutoWield|CastSpell|DropItem|GiveItem|MoveItem|SelectItem|UseItem|InvokeChatParser|Logout|SetAutorun|SetCombatMode|FaceHeading|RadianFaceHeading|SetCursorPosition|SetIdleTime)\b',
  '\b(Trade|Vendor|Salvage|Fellowship|SpellTab|UIElement)[A-Z][A-Za-z]*\s*\(',
  '\bAdd(Attribute|Skill|Vital)Experience\b',
  '\b(DispatchOnChatCommand|PostMessage|SendMessage|SendInput|mouse_event|keybd_event|IDQueue)\b'
)
$hits = @()
foreach ($f in Get-ChildItem -Path $src -Filter *.cs) {
  $n = 0
  foreach ($line in [IO.File]::ReadAllLines($f.FullName)) {
    $n++
    $code = ($line -split '//')[0]
    foreach ($p in $bad) { if ($code -match $p) { $hits += "$($f.Name):$n  $($line.Trim())" } }
  }
}
if ($hits.Count -gt 0) { Write-Host "READ-ONLY GUARD: forbidden call(s) found:"; $hits | ForEach-Object { Write-Host "  $_" }; exit 1 }
Write-Host "Read-only guard: OK (only RequestId + own chat text)"
exit 0
