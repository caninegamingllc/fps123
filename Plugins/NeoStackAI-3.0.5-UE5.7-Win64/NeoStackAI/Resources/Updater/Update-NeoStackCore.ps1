param([Parameter(Mandatory=$true)][string]$RequestPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# This helper is copied into a unique attempt directory before the editor closes.
# Requests contain data only. Never build another shell command from request values.
function Assert-PhysicalPath([string]$Path) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw 'An update path is not absolute.' }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ($full.Length -le 3) { throw 'An update path cannot be a drive root.' }
    $part = $full
    while ($part) {
        if (Test-Path -LiteralPath $part) {
            if (([IO.File]::GetAttributes($part) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw ('Linked installations cannot be updated automatically: ' + $part)
            }
        }
        $parent = [IO.Path]::GetDirectoryName($part)
        if ($parent -eq $part) { break }
        $part = $parent
    }
    return $full
}
function Assert-Child([string]$Path, [string]$Root) {
    $full = Assert-PhysicalPath $Path
    $prefix = (Assert-PhysicalPath $Root).TrimEnd('\') + '\'
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Update path escaped its approved root.' }
    return $full
}
function Assert-PhysicalTree([string]$Root) {
    [void](Assert-PhysicalPath $Root)
    # Enumerate each directory without following links; reject before descending.
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($Root)
    while ($pending.Count) {
        $current = $pending.Pop()
        foreach ($item in Get-ChildItem -LiteralPath $current -Force) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Linked update content: ' + $item.FullName) }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
        }
    }
}
function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}
function Remove-OwnedTree([string]$Path) {
    if ([IO.Path]::GetFullPath($Path) -ieq $target) { [void](Assert-Child $Path $pluginsRoot) }
    else { [void](Assert-Child $Path $recoveryRoot) }
    if (Test-Path -LiteralPath $Path) {
        Assert-PhysicalTree $Path
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
    }
}
function Write-Result([string]$State, [string]$Message) {
    $data = @{ state = $State; message = $Message; attempt = $request.Attempt; backup = $backup } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText(($resultPath + '.tmp'), $data, (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath ($resultPath + '.tmp') -Destination $resultPath -Force
}
function Test-Owner {
    $owner = Get-Process -Id $request.OwnerPid -ErrorAction SilentlyContinue
    if (-not $owner) { return $false }
    try { return $owner.StartTime.ToUniversalTime().ToFileTimeUtc().ToString() -eq $request.OwnerStartTime }
    finally { $owner.Dispose() }
}
function Assert-NotCanceled {
    if (Test-Path -LiteralPath $cancelPath) { throw 'Update canceled. The downloaded package is available for another attempt.' }
}
function Wait-ForShutdown {
    Write-Result 'ready' 'Waiting for the editor to finish saving.'
    $deadline = [DateTime]::UtcNow.AddSeconds(180)
    while (-not (Test-Path -LiteralPath $armPath)) {
        Assert-NotCanceled
        if (-not (Test-Owner)) { throw 'Editor exited without confirming the update shutdown.' }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Editor shutdown was not confirmed. Install again when ready.' }
        Start-Sleep -Milliseconds 250
    }
    if ([IO.File]::ReadAllText($armPath) -cne $request.Attempt) { throw 'Invalid shutdown confirmation.' }
    $deadline = [DateTime]::UtcNow.AddSeconds(180)
    while (Test-Owner) {
        Assert-NotCanceled
        if ([DateTime]::UtcNow -gt $deadline) { throw 'The editor is still running. No installation files were changed.' }
        Start-Sleep -Milliseconds 250
    }
    Assert-NotCanceled
}

# Only ask Restart Manager who is using this installation. Never shut down or
# terminate another process. A nonzero result fails closed.
Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class NeoStackUpdateLocks {
    [DllImport("rstrtmgr.dll", CharSet=CharSet.Unicode)] static extern int RmStartSession(out uint session, int flags, StringBuilder key);
    [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint session);
    [DllImport("rstrtmgr.dll", CharSet=CharSet.Unicode)] static extern int RmRegisterResources(uint session, uint count, string[] files, uint apps, IntPtr app, uint services, IntPtr service);
    [DllImport("rstrtmgr.dll")] static extern int RmGetList(uint session, out uint needed, ref uint count, IntPtr info, ref uint reason);
    public static void AssertUnused(string[] files) {
        uint session; var key = new StringBuilder(33);
        int error = RmStartSession(out session, 0, key);
        if (error != 0) throw new InvalidOperationException("Cannot check installation use: " + error);
        try {
            if (files.Length == 0) return;
            error = RmRegisterResources(session, (uint)files.Length, files, 0, IntPtr.Zero, 0, IntPtr.Zero);
            if (error != 0) throw new InvalidOperationException("Cannot register installation resources: " + error);
            uint needed, count = 0, reason = 0;
            error = RmGetList(session, out needed, ref count, IntPtr.Zero, ref reason);
            if (error == 234 || needed != 0 || reason != 0) throw new InvalidOperationException("This installation is still in use. Close its other editors and try again.");
            if (error != 0) throw new InvalidOperationException("Cannot verify installation locks: " + error);
        } finally { RmEndSession(session); }
    }
}
"@

function Test-PluginTree([string]$PluginRoot) {

  $manifestPath = Join-Path $PluginRoot 'NeoStackAI.release.sha256'

  if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Update has no release integrity manifest.' }

  $lines = [IO.File]::ReadAllLines($manifestPath)

  $headers = @('# neostack-release-manifest-v1', ('# version=' + $ExpectedVersion), ('# engine=' + $ExpectedEngine), ('# platform=' + $ExpectedPlatform))

  if ($lines.Count -lt 5) { throw 'Release integrity manifest is empty.' }

  for ($index = 0; $index -lt $headers.Count; $index++) { if ($lines[$index] -cne $headers[$index]) { throw 'Release identity does not match this update request.' } }

  $rootFull = [IO.Path]::GetFullPath($PluginRoot)

  $rootPrefix = $rootFull.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

  $declared = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

  foreach ($line in $lines[4..($lines.Count - 1)]) {

    if ($line -notmatch '^([0-9a-f]{64}) \*(.+)$') { throw 'Malformed release integrity manifest.' }

    $expectedHash = $Matches[1]

    $relative = $Matches[2]

    if ($relative.Contains('NSAI_CaptureRuntime')) { throw 'Release contains disallowed capture runtime files.' }

    if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains('\') -or (($relative -split '/') -contains '..')) { throw 'Unsafe path in release integrity manifest.' }

    $target = [IO.Path]::GetFullPath((Join-Path $PluginRoot $relative))

    if (-not $target.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Release integrity path escaped the plugin folder.' }

    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw ('Release file is missing: ' + $relative) }

    $actualHash = Get-Sha256 $target

    if ($actualHash -ne $expectedHash) { throw ('Release file hash mismatch: ' + $relative) }

    [void]$declared.Add($relative)

  }

  $moduleExtension = if ($ExpectedPlatform -eq 'Win64') { '.dll' } else { '.dylib' }

  $required = @('NeoStackAI.uplugin', ('Binaries/' + $ExpectedPlatform + '/UnrealEditor.modules'))

  foreach ($relative in $required) { if (-not $declared.Contains($relative)) { throw ('Release manifest omits required file: ' + $relative) } }

  foreach ($moduleName in @('NeoStackAI')) {

    $candidates = @(('Binaries/' + $ExpectedPlatform + '/UnrealEditor-' + $moduleName + $moduleExtension))

    if ($ExpectedPlatform -eq 'Mac') { $candidates += ('Binaries/Mac/libUnrealEditor-' + $moduleName + $moduleExtension) }

    if (-not ($candidates | Where-Object { $declared.Contains($_) } | Select-Object -First 1)) { throw ('Release manifest omits required module: ' + $moduleName) }

  }

}


$request = Get-Content -LiteralPath $RequestPath -Raw | ConvertFrom-Json
$attemptRoot = Assert-PhysicalPath ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($RequestPath)))
if ($request.Attempt -notmatch '^[a-f0-9]{32}$') { throw 'Invalid update attempt.' }
$resultPath = Join-Path $attemptRoot 'result.json'
$armPath = Join-Path $attemptRoot 'shutdown.confirmed'
$cancelPath = Join-Path $attemptRoot 'cancel'
$target = Assert-PhysicalPath $request.TargetPluginDir
$parent = Assert-PhysicalPath $request.PluginParentDir
if ([IO.Path]::GetDirectoryName($target) -ine $parent -or [IO.Path]::GetFileName($target) -ine 'NeoStackAI') { throw 'Unexpected plugin installation path.' }
$archive = Assert-PhysicalPath $request.ZipPath
$projectFile = Assert-PhysicalPath $request.ProjectFile
$projectRoot = Assert-PhysicalPath ([IO.Path]::GetDirectoryName($projectFile))
# PluginManager scans every subtree of Plugins until finding a descriptor.
# Backups and failed staging MUST live outside that search tree, on the same
# volume so directory promotion and rollback remain atomic renames.
$pluginsRoot = $parent
while ($pluginsRoot -and [IO.Path]::GetFileName($pluginsRoot) -ine 'Plugins') {
    $pluginsRoot = [IO.Path]::GetDirectoryName($pluginsRoot)
}
if (-not $pluginsRoot) { throw 'Automatic updates require a project or Engine Plugins installation.' }
$installationOwner = Assert-PhysicalPath ([IO.Path]::GetDirectoryName($pluginsRoot))
if ($installationOwner -ine $projectRoot -and -not (Test-Path -LiteralPath (Join-Path $installationOwner 'Build/Build.version') -PathType Leaf)) {
    throw 'Cannot verify the owner of this Plugins directory. Install this package manually.'
}
$recoveryRoot = Assert-Child (Join-Path $installationOwner ('Saved/NeoStackAI/Updates/' + $request.Attempt)) $installationOwner
if ($recoveryRoot.StartsWith(($pluginsRoot + '\'), [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetPathRoot($recoveryRoot) -ine [IO.Path]::GetPathRoot($target)) {
    throw 'Update recovery storage must be outside Plugins and on the installation volume.'
}
$backup = Assert-Child (Join-Path $recoveryRoot 'NeoStackAI') $recoveryRoot
$staging = Assert-Child (Join-Path $recoveryRoot 'staging') $recoveryRoot
$projectBackup = Join-Path $recoveryRoot 'project.original'
$projectChanged = $false
$writtenProjectHash = ''
$oldMoved = $false
$newPromoted = $false
$moves = New-Object 'System.Collections.Generic.List[object]'
$installationLock = $null
$ExpectedVersion = $request.LatestVersion
$ExpectedEngine = $request.Engine
$ExpectedPlatform = 'Win64'
$quarantine = Assert-Child (Join-Path $projectRoot ('Saved/NeoStackAI/LegacyExtensionQuarantine/' + $request.Attempt)) $projectRoot
function Save-Journal {
    $journal = @{ target=$target; backup=$backup; oldMoved=$oldMoved; newPromoted=$newPromoted; projectFile=$projectFile; projectBackup=$projectBackup; projectChanged=$projectChanged; moves=@($moves.ToArray()) }
    [IO.File]::WriteAllText((Join-Path $recoveryRoot 'journal.json'), ($journal | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
}
try {
    if ((Test-Path -LiteralPath $backup) -or (Test-Path -LiteralPath $staging)) { throw 'Update attempt directories already exist.' }
    Assert-PhysicalTree $target
    if ((Get-Sha256 $archive) -cne $request.ExpectedSha256.ToLowerInvariant()) { throw 'Downloaded package verification failed.' }
    # The mutex file is held with sharing disabled. Do not remove it on release:
    # deleting/recreating a lock path would allow two owners to hold different files.
    $lockPath = Assert-Child (Join-Path $parent '.neostack-core-update.lock') $parent
    $installationLock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $originalDescriptorHash = Get-Sha256 (Join-Path $target 'NeoStackAI.uplugin')
    Wait-ForShutdown
    if ((Get-Sha256 (Join-Path $target 'NeoStackAI.uplugin')) -cne $originalDescriptorHash) { throw 'The installed plugin changed while waiting for shutdown.' }
    # Capture all inputs after shutdown. A saved .uproject is authoritative; don't
    # overwrite it with a snapshot from before the editor's Save prompt.
    $originalProject = [IO.File]::ReadAllBytes($projectFile)
    $project = [Text.Encoding]::UTF8.GetString($originalProject).TrimStart([char]0xfeff) | ConvertFrom-Json
    if ($project.PSObject.Properties.Name -contains 'AdditionalPluginDirectories') {
        foreach ($extra in $project.AdditionalPluginDirectories) {
            $searchRoot = if ([IO.Path]::IsPathRooted($extra)) { [IO.Path]::GetFullPath($extra) } else { [IO.Path]::GetFullPath((Join-Path $projectRoot $extra)) }
            $searchRoot = $searchRoot.TrimEnd('\')
            if ($recoveryRoot -ieq $searchRoot -or $recoveryRoot.StartsWith(($searchRoot + '\'), [StringComparison]::OrdinalIgnoreCase) -or $quarantine -ieq $searchRoot -or $quarantine.StartsWith(($searchRoot + '\'), [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Recovery storage overlaps an additional plugin search directory.'
            }
        }
    }
    $legacyPaths = @()
    foreach ($entry in $request.LegacyDirectories) {
        $path = Assert-Child $entry $projectRoot
        if ($path -ieq $target -or $target.StartsWith(($path + '\'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Legacy cleanup overlaps the current plugin.' }
        if (Test-Path -LiteralPath $path) { Assert-PhysicalTree $path; $legacyPaths += $path }
    }
    $resources = @($projectFile)
    foreach ($root in @($target) + $legacyPaths) {
        $resources += @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { $_.Extension -in @('.dll', '.exe', '.uplugin') } | ForEach-Object FullName)
    }
    [NeoStackUpdateLocks]::AssertUnused([string[]]$resources)
    [void]([IO.Directory]::CreateDirectory($recoveryRoot))
    [void](New-Item -ItemType Directory -Path $staging)
    # Inspect entries before extraction. No archive path may escape the staging tree.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $entries = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName
            if ($name.Contains(':') -or $name.Contains('\') -or [IO.Path]::IsPathRooted($name) -or (($name -split '/') -contains '..')) { throw 'Unsafe archive entry.' }
            [void](Assert-Child (Join-Path $staging $name) $staging)
            if (-not $entries.Add($name)) { throw 'Duplicate archive entry.' }
        }
    } finally { $zip.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $staging)
    Assert-PhysicalTree $staging
    $candidate = Assert-Child (Join-Path $staging 'NeoStackAI') $staging
    Test-PluginTree $candidate
    Assert-NotCanceled
    [NeoStackUpdateLocks]::AssertUnused([string[]]$resources)
    Assert-PhysicalTree $target
    # Keep an exact project backup and a durable recovery journal before mutation.
    [IO.File]::WriteAllBytes($projectBackup, $originalProject)
    Save-Journal
    [IO.Directory]::Move($target, $backup)
    $oldMoved = $true
    Save-Journal
    [IO.Directory]::Move($candidate, $target)
    $newPromoted = $true
    Save-Journal
    Test-PluginTree $target
    foreach ($path in $legacyPaths) {
        [void](New-Item -ItemType Directory -Path $quarantine -Force)
        $destination = Assert-Child (Join-Path $quarantine ($moves.Count.ToString() + '-' + [IO.Path]::GetFileName($path))) $quarantine
        Assert-PhysicalTree $path
        [IO.Directory]::Move($path, $destination)
        $moves.Add(@{ source=$path; destination=$destination })
        Save-Journal
    }
    if ($project.PSObject.Properties.Name -contains 'Plugins') {
        $retained = @($project.Plugins | Where-Object { $_.Name -notin $request.LegacyNames })
        if ($retained.Count -ne @($project.Plugins).Count) {
            # Refuse a concurrent external project edit; never clobber it.
            if ((Get-Sha256 $projectFile) -cne (Get-Sha256 $projectBackup)) { throw 'Project settings changed during installation.' }
            $project.Plugins = $retained
            $tempProject = $projectFile + '.neostack-' + $request.Attempt
            [IO.File]::WriteAllText($tempProject, ($project | ConvertTo-Json -Depth 100), (New-Object Text.UTF8Encoding($false)))
            $writtenProjectHash = Get-Sha256 $tempProject
            [IO.File]::Replace($tempProject, $projectFile, [NullString]::Value)
            $projectChanged = $true
            Save-Journal
        }
    }
    # Retain the old installation and journal as recoverable evidence. Cleanup of
    # a prior backup is never a prerequisite for installing another update.
    Remove-OwnedTree $staging
    Write-Result 'installed' 'Update installed. Restart Unreal Editor to continue.'
    exit 0
} catch {
    $failure = $_.Exception.Message
    $rollbackErrors = New-Object 'System.Collections.Generic.List[string]'
    if ($projectChanged) {
        try {
            if ((Get-Sha256 $projectFile) -cne $writtenProjectHash) { throw 'Project settings changed after installation; preserved the external edit.' }
            [IO.File]::WriteAllBytes($projectFile, [IO.File]::ReadAllBytes($projectBackup))
        }
        catch { $rollbackErrors.Add('Project restoration failed: ' + $_.Exception.Message) }
    }
    for ($i = $moves.Count - 1; $i -ge 0; --$i) {
        try { [IO.Directory]::Move($moves[$i].destination, $moves[$i].source) }
        catch { $rollbackErrors.Add('Legacy restoration failed: ' + $_.Exception.Message) }
    }
    if ($newPromoted) {
        try { Remove-OwnedTree $target; $newPromoted = $false }
        catch { $rollbackErrors.Add('New plugin removal failed: ' + $_.Exception.Message) }
    }
    if ($oldMoved -and -not (Test-Path -LiteralPath $target)) {
        try { [void](Assert-PhysicalPath $backup); [IO.Directory]::Move($backup, $target); $oldMoved = $false }
        catch { $rollbackErrors.Add('Previous plugin remains at ' + $backup) }
    }
    # Avoid deleting evidence or traversing an unexpected replacement on failure.
    $message = $failure
    if ($rollbackErrors.Count) { $message += ' Recovery required: ' + ($rollbackErrors -join '; ') }
    Write-Result 'failed' $message
    exit 1
} finally {
    if ($installationLock) { $installationLock.Dispose() }
}
