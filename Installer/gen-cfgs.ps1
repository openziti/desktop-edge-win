echo @"
{
  "Active": false,
  "Duration": 9223372036854,
  "Identities": [ 
"@
foreach ($c in $cfgs)
{
    $n=$c.name.replace('.json','')
    if ($n -eq "config") { continue }
    
echo @"
  { "Name": "$n", "Fingerprint": "$n" },
"@
}
echo @"
  { "Name": "", "FingerPrint": "" }
"@
echo @"
  ],
  "LogLevel": "debug", "TunIpv4": "100.64.0.200", "TunIpv4Mask": 24
}
"@

