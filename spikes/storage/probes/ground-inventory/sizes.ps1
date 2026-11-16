# Sizes of what the plugin stores, measured on an in-memory database with invented content.
# Windows PowerShell 5.1. Loads KeePass 2.62's KeePassLib and the plugin's Release build copied
# from the review4 worktree. Shows no window, writes no file but its own output.
param([string]$Bin, [string]$KeePass)

$ErrorActionPreference = 'Stop'
$kp = [System.Reflection.Assembly]::LoadFrom($KeePass)
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
public static class ProbeResolver
{
    private static Assembly _keePass;
    private static string _bin;
    public static void Install(Assembly keePass, string bin)
    {
        _keePass = keePass;
        _bin = bin;
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
    }
    private static Assembly Resolve(object sender, ResolveEventArgs e)
    {
        var name = new AssemblyName(e.Name).Name;
        if (name == "KeePass") { return _keePass; }
        var path = Path.Combine(_bin, name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
}
'@
[ProbeResolver]::Install($kp, $Bin)
$null = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($Bin, 'BouncyCastle.Cryptography.dll'))
$null = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($Bin, 'Newtonsoft.Json.dll'))
$gate = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($Bin, 'KeePassGate.dll'))

"KeePass " + $kp.GetName().Version
"history defaults: items " + [KeePassLib.PwDatabase]::DefaultHistoryMaxItems + ", size " + [KeePassLib.PwDatabase]::DefaultHistoryMaxSize

$db = New-Object KeePassLib.PwDatabase
$store = New-Object KeePassGate.Bridge.CustomDataStore($db)
$service = New-Object KeePassGate.Authority.AuthorityService($store, $null)
$change = $service.Create()
"create: " + $change.Succeeded

foreach ($key in $store.Keys) {
    $value = $store.Get($key)
    "{0,-40} {1,6} chars" -f ($key.Substring(0, [Math]::Min(40, $key.Length))), $value.Length
}

$reading = $service.Read()
$issuer = New-Object KeePassGate.Authority.AuthorityIssuer
$profiles = [string[]]@('ops', 'reader')
$client = $issuer.IssueClient($reading.Material.AuthorityKey, 'agent-fictitious', $profiles, $null)
$der = $client.CertificateDer
"client certificate DER: " + $der.Length + " bytes"
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(,$der)
$record = [KeePassGate.Authority.AdmissionRecord]::For($cert, [DateTimeOffset]::UtcNow, $profiles)
"admission record: " + $record.Save().Length + " chars"

$many = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -lt 32; $i++) { $many.Add(('p' * 60) + $i.ToString('00')) }
$big = [KeePassGate.Authority.AdmissionRecord]::For($cert, [DateTimeOffset]::UtcNow, $many)
"admission record with 32 profiles of 62 chars: " + $big.Save().Length + " chars"

$names = [KeePassGate.Acl.ProfileNames]::Empty()
foreach ($n in @('ops', 'reader', 'editor')) { $null = $names.Add($n) }
"profile list of 3: " + $names.Write().Length + " chars"
$full = [KeePassGate.Acl.ProfileNames]::Empty()
foreach ($n in $many) { $null = $full.Add($n) }
"profile list of 32 x 62 chars: " + $full.Write().Length + " chars"

$typical = '{"default":"inherit","reader":["read","list"],"editor":["create","read","update","list","protected"]}'
"typical rule: " + $typical.Length + " chars, reads: " + ($null -ne [KeePassGate.Acl.AclDocument]::Read($typical))
$verbs = '["create","read","update","strip","delete","list","attachments","protected"]'
$parts = New-Object System.Collections.Generic.List[string]
$parts.Add('"default":"deny"')
for ($i = 0; $i -lt 31; $i++) { $parts.Add('"' + ('p' * 62) + $i.ToString('00') + '":' + $verbs) }
$largest = '{' + [string]::Join(',', $parts) + '}'
"largest rule, 31 profiles of 64 chars with all eight: " + $largest.Length + " chars, reads: " + ($null -ne [KeePassGate.Acl.AclDocument]::Read($largest))

$reading.Dispose()
