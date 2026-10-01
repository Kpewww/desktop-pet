<#
  Desktop pets build script. Needs nothing but the C# compiler that ships
  with Windows (.NET Framework 4.8). Every pet is one folder under assets\ (and art\) with a
  character.txt; they all share the engine in src\.

    .\build.ps1                 compile every pet into out\<id>.exe
    .\build.ps1 -Char example   ...just one of them; works with every task
    .\build.ps1 new mypet       start a new pet: copies art\example and assets\example to mypet
    .\build.ps1 art             build assets\<pet>\ from the text sources in art\<pet>\
    .\build.ps1 test            compile and run the unit tests
    .\build.ps1 run             compile, then (re)start the pet(s)
    .\build.ps1 dist            release build: one zip per pet plus one with all of them, in dist\
    .\build.ps1 clean           delete out\, dist\ and art\out\
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'art', 'test', 'run', 'dist', 'clean', 'new')]
    [string]$Task = 'build',
    [string]$Char = 'all',
    [switch]$Release,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Rest = @()
)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$Out = Join-Path $Root 'out'

$Csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $Csc)) { $Csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $Csc)) { throw 'csc.exe from .NET Framework 4.x was not found.' }

$Refs = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
          '/r:System.Runtime.Serialization.dll', '/r:System.Xml.dll', '/r:System.Security.dll')

function Invoke-Csc([string[]]$Arguments) {
    $lines = & $Csc /nologo /codepage:65001 /utf8output @Arguments 2>&1
    $code = $LASTEXITCODE
    foreach ($l in $lines) {
        $s = "$l"
        # The in-box compiler always nags that it only supports C# 5; that is intentional here.
        if ($s -match 'only supports language versions up to C# 5' -or $s -match 'go\.microsoft\.com/fwlink/\?LinkID=533240' -or $s -eq '') { continue }
        Write-Host $s
    }
    if ($code -ne 0) { throw "csc failed with exit code $code" }
}

function New-Dir([string]$Path) { New-Item -ItemType Directory -Force -Path $Path | Out-Null }

function Write-Utf8([string]$Path, [string]$Text, [bool]$Bom) {
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding $Bom))
}

# The pets to work on: folders under $Under that contain $Marker, narrowed by -Char.
function Get-PetKeys([string]$Under, [string]$Marker) {
    $all = @(Get-ChildItem (Join-Path $Root $Under) -Directory |
             Where-Object { Test-Path (Join-Path $_.FullName $Marker) } | ForEach-Object { $_.Name })
    if ($Char -eq 'all') { return $all }
    if ($all -notcontains $Char) { throw "no pet '$Char' in $Under\ (have: $($all -join ', '))" }
    return @($Char)
}

# What the build needs from assets\<key>\character.txt: its id (exe and system names) and name.
function Read-Pet([string]$Key) {
    $pet = @{ Key = $Key }
    foreach ($line in [System.IO.File]::ReadAllLines((Join-Path $Root "assets\$Key\character.txt"), [System.Text.Encoding]::UTF8)) {
        if ($line -match '^\s*\[') { break }   # the foods follow; the header is all we need
        if ($line -match '^\s*(id|name)\s*=\s*(.+?)\s*$') { $pet[$Matches[1]] = $Matches[2] }
    }
    if (-not $pet.id -or -not $pet.name) { throw "assets\$Key\character.txt needs an id and a name" }
    $pet.Exe = Join-Path $Out ($pet.id + '.exe')
    return $pet
}

function Get-Pets { return @(Get-PetKeys 'assets' 'character.txt' | ForEach-Object { Read-Pet $_ }) }

function Get-Version {
    $m = Select-String -Path (Join-Path $Root 'src\App\AssemblyInfo.cs') -Pattern 'AssemblyVersion\("(\d+\.\d+\.\d+)'
    if ($m) { return $m.Matches[0].Groups[1].Value }
    return '0.0.0'
}

function Build-App($Pet) {
    New-Dir $Out
    Stop-Pet $Pet   # a running copy locks the exe
    $key = $Pet.Key
    $assets = Join-Path $Root "assets\$key"
    foreach ($name in @('character.txt', 'icon.ico')) {
        if (-not (Test-Path (Join-Path $assets $name))) { throw "assets\$key\$name is missing." }
    }
    if (-not (Get-ChildItem $assets -Filter '*.atlas')) { throw "assets\$key has no atlas - run .\build.ps1 art -Char $key first." }
    # Everything in the pet's folder is embedded as Pet.<file>, plus the shared font.
    $resources = @(Get-ChildItem $assets -File | Where-Object { $_.Extension -in '.atlas', '.ico', '.txt' } |
                   ForEach-Object { "/resource:$($_.FullName),Pet.$($_.Name)" })
    $font = Join-Path $Root 'assets\pixel12.font'
    if (Test-Path $font) { $resources += "/resource:$font,Pet.pixel12.font" }

    # The exe's title and manifest carry the pet's name and id.
    $obj = Join-Path $Out "obj\$key"
    New-Dir $obj
    $info = Join-Path $obj 'PetInfo.cs'
    $title = $Pet.name.Replace('\', '\\').Replace('"', '\"')
    Write-Utf8 $info ("[assembly: System.Reflection.AssemblyTitle(`"$title`")]`r`n" +
                      "[assembly: System.Reflection.AssemblyProduct(`"$title`")]`r`n") $true
    $manifest = Join-Path $obj 'app.manifest'
    $text = [System.IO.File]::ReadAllText((Join-Path $Root 'src\App\app.manifest'))
    Write-Utf8 $manifest ($text.Replace('$ID$', $Pet.id).Replace('$VERSION$', (Get-Version) + '.0')) $false

    $cscArgs = @('/target:winexe', "/out:$($Pet.Exe)", '/platform:anycpu32bitpreferred', '/unsafe', '/warn:4',
                 "/win32manifest:$manifest", "/win32icon:$assets\icon.ico") +
               $Refs + @("/recurse:$Root\src\Core\*.cs", "/recurse:$Root\src\App\*.cs", $info) + $resources
    if ($Release) { $cscArgs += @('/optimize+', '/debug-') } else { $cscArgs += @('/debug:pdbonly', '/define:DEBUG') }
    Invoke-Csc $cscArgs
    Write-Host ("built {0} ({1:N0} KB)" -f $Pet.Exe, ((Get-Item $Pet.Exe).Length / 1KB))
}

function Build-Art {
    $tool = Join-Path $Out 'tools\ArtBuild.exe'
    New-Dir (Split-Path $tool)
    Invoke-Csc (@('/target:exe', "/out:$tool", '/optimize+', '/unsafe', '/platform:anycpu') + $Refs +
                @("/recurse:$Root\art\tool\*.cs", "$Root\src\Core\Atlas.cs"))
    foreach ($key in (Get-PetKeys 'art' 'clips.txt')) {
        & $tool $Root $key @Rest
        if ($LASTEXITCODE -ne 0) { throw "ArtBuild failed for $key" }
    }
}

function Test-Core {
    $testExe = Join-Path $Out 'tests\AlyTests.exe'
    New-Dir (Split-Path $testExe)
    Invoke-Csc (@('/target:exe', "/out:$testExe", '/debug:pdbonly', '/define:DEBUG') + $Refs +
                @("/recurse:$Root\src\Core\*.cs", "/recurse:$Root\src\Tests\*.cs"))
    & $testExe @Rest
    if ($LASTEXITCODE -ne 0) { throw 'tests failed' }
}

# Asks a running pet to save and quit (the same event its own single-instance logic uses).
function Stop-Pet($Pet) {
    $procs = @(Get-Process -Name $Pet.id -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { return }
    try { [System.Threading.EventWaitHandle]::OpenExisting("Local\$($Pet.id).Quit").Set() | Out-Null } catch { }
    foreach ($p in $procs) {
        if (-not $p.WaitForExit(3000)) { $p.Kill(); $p.WaitForExit(2000) | Out-Null }
    }
}

# One folder per pet, with friendly names: <name>\<name>.exe + 使用说明 + font licences.
function Copy-PetFolder($Pet, [string]$Folder) {
    New-Dir $Folder
    Copy-Item $Pet.Exe (Join-Path $Folder ($Pet.name + '.exe'))
    $guide = Join-Path $Root "docs\$($Pet.Key)\使用说明.txt"
    if (Test-Path $guide) { Copy-Item $guide (Join-Path $Folder '使用说明.txt') }
    $licenses = Join-Path $Folder 'FONT-LICENSES'
    New-Dir $licenses
    $fonts = Join-Path $Root 'art\fonts'
    if (Test-Path (Join-Path $fonts 'OFL.txt')) { Copy-Item (Join-Path $fonts 'OFL.txt') (Join-Path $licenses 'Fusion-Pixel-OFL.txt') }
    if (Test-Path (Join-Path $fonts 'LICENSES')) {
        Get-ChildItem (Join-Path $fonts 'LICENSES') -Directory | ForEach-Object {
            Get-ChildItem $_.FullName -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $licenses ($_.Directory.Name + '-' + $_.Name)) }
        }
    }
}

# .NET's ZipArchive stores non-ASCII names as UTF-8 (with the flag Windows 10/11 honours),
# unlike Compress-Archive in PowerShell 5.1, which uses the local code page. Entries are
# added one by one so the paths use '/' as the zip format requires.
function Write-Zip([string]$Stage, [string]$Zip) {
    if (Test-Path $Zip) { Remove-Item -Force $Zip }
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open($Zip, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem $Stage -Recurse -File | ForEach-Object {
            $name = $_.FullName.Substring($Stage.Length + 1).Replace('\', '/')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $name, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally { $archive.Dispose() }
    Write-Host ("packed {0} ({1:N0} KB)" -f $Zip, ((Get-Item $Zip).Length / 1KB))
}

switch ($Task) {
    'build' { foreach ($pet in (Get-Pets)) { Build-App $pet } }
    'art'   { Build-Art }
    'test'  { Test-Core }
    'run'   {
        foreach ($pet in (Get-Pets)) {
            Build-App $pet
            Start-Process -FilePath $pet.Exe
        }
    }
    'dist'  {
        $script:Release = $true
        $version = Get-Version
        $stage = Join-Path $Out 'dist-stage'
        if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
        New-Dir (Join-Path $Root 'dist')
        $pets = Get-Pets
        foreach ($pet in $pets) {
            Build-App $pet
            $own = Join-Path $stage $pet.id
            Copy-PetFolder $pet (Join-Path $own $pet.name)
            Write-Zip $own (Join-Path $Root "dist\$($pet.id)-$version.zip")
        }
        if ($pets.Count -gt 1) {
            # all of them in one zip, each in its own folder, to send in one go
            $together = Join-Path $stage 'together'
            foreach ($pet in $pets) { Copy-PetFolder $pet (Join-Path $together $pet.name) }
            Write-Zip $together (Join-Path $Root "dist\DesktopPets-$version.zip")
        }
    }
    'new'   {
        # .\build.ps1 new <key>: a copy of the example pet to make your own from.
        $key = if ($Rest.Count -gt 0) { $Rest[0] } else { throw 'usage: .\build.ps1 new <key>   (lower-case letters/digits, e.g. mochi)' }
        if ($key -notmatch '^[a-z][a-z0-9_]{0,19}$') { throw "the key must be lower-case letters/digits: $key" }
        foreach ($d in @("art\$key", "assets\$key")) { if (Test-Path (Join-Path $Root $d)) { throw "$d already exists" } }
        Copy-Item -Recurse (Join-Path $Root 'art\example') (Join-Path $Root "art\$key")
        New-Dir (Join-Path $Root "assets\$key")
        foreach ($f in @('character.txt', 'lines.zh.txt')) { Copy-Item (Join-Path $Root "assets\example\$f") (Join-Path $Root "assets\$key\$f") }
        $ch = Join-Path $Root "assets\$key\character.txt"
        $id = 'Pet' + (Get-Culture).TextInfo.ToTitleCase($key).Replace('_', '')
        $text = [System.IO.File]::ReadAllText($ch, [System.Text.Encoding]::UTF8)
        $text = $text -replace '(?m)^key\s*=.*$', "key      = $key" -replace '(?m)^id\s*=.*$', "id       = $id" -replace '(?m)^name\s*=.*$', "name     = $key" -replace '(?m)^nick\s*=.*$', "nick     = $key" -replace '(?m)^hotkey\s*=.*$', 'hotkey   = Ctrl+Alt+P'
        Write-Utf8 $ch $text $false
        Write-Host "new pet '$key' (id $id): edit art\$key\ and assets\$key\, then .\build.ps1 art -Char $key; .\build.ps1 run -Char $key"
    }
    'clean' {
        foreach ($d in @($Out, (Join-Path $Root 'dist'), (Join-Path $Root 'art\out'))) {
            if (Test-Path $d) { Remove-Item -Recurse -Force $d }
        }
    }
}
