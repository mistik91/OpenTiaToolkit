function New-TestWire {
    param([string]$Id, [string]$Ends)
    return '<Wire UId="' + $Id + '">' + $Ends + '</Wire>'
}
function New-TestPin { param([string]$Id, [string]$Pin); return '<NameCon UId="' + $Id + '" Name="' + $Pin + '" />' }
function New-TestAccess { param([string]$Id, [string]$Name); return '<Access Scope="LocalVariable" UId="' + $Id + '"><Symbol><Component Name="' + $Name + '" /></Symbol></Access>' }
function New-TestGraphXml {
    param([string]$Parts, [string]$Wires, [string]$Static = '')
    return '<Document><SW.Blocks.FB><AttributeList><Interface><Sections><Section Name="Static">' + $Static +
        '</Section></Sections></Interface></AttributeList><ObjectList><SW.Blocks.CompileUnit><AttributeList><NetworkSource>' +
        '<FlgNet xmlns="http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v5"><Parts>' + $Parts +
        '</Parts><Wires>' + $Wires + '</Wires></FlgNet></NetworkSource><ProgrammingLanguage>LAD</ProgrammingLanguage>' +
        '</AttributeList></SW.Blocks.CompileUnit></ObjectList></SW.Blocks.FB></Document>'
}
function New-TestBooleanXml {
    param([string]$Variant = '')
    $names = @('AutoStart','JogRequest','Permit','GuardClosed','Fault','Reset','RunRequest')
    $parts = ''
    for ($i = 0; $i -lt 7; $i++) { $parts += New-TestAccess ([string]($i+1)) $names[$i] }
    foreach ($id in @(10,11,13,14,15,16)) {
        $negated = if ($id -in @(15,16)) { '<Negated Name="operand" />' } else { '' }
        $parts += '<Part Name="Contact" UId="' + $id + '">' + $negated + '</Part>'
    }
    $parts += '<Part Name="O" UId="12"><TemplateValue Name="Card" Type="Cardinality">2</TemplateValue></Part><Part Name="Coil" UId="17" />'
    $wires = New-TestWire '30' ('<Powerrail />' + (New-TestPin '10' 'in') + (New-TestPin '11' 'in'))
    $partIds = @(10,11,13,14,15,16,17)
    for ($i=0; $i -lt 7; $i++) { $wires += New-TestWire ([string](31+$i)) ('<IdentCon UId="' + ($i+1) + '" />' + (New-TestPin ([string]$partIds[$i]) 'operand')) }
    $links = @(@(10,'out',12,'in1'),@(11,'out',12,'in2'),@(12,'out',13,'in'),@(13,'out',14,'in'),@(14,'out',15,'in'),@(15,'out',16,'in'),@(16,'out',17,'in'))
    if ($Variant -eq 'jog-bypasses-interlocks') {
        $links = @(@(10,'out',13,'in'),@(13,'out',14,'in'),@(14,'out',15,'in'),@(15,'out',16,'in'),@(16,'out',12,'in1'),@(11,'out',12,'in2'),@(12,'out',17,'in'))
    }
    if ($Variant -eq 'cycle') { $links[0] = @(16,'out',12,'in1') }
    for ($i=0; $i -lt $links.Count; $i++) { $l=$links[$i]; $wires += New-TestWire ([string](40+$i)) ((New-TestPin ([string]$l[0]) $l[1]) + (New-TestPin ([string]$l[2]) $l[3])) }
    $xml = New-TestGraphXml $parts $wires
    switch ($Variant) {
        'fault-inverted' { $xml = $xml.Replace('<Part Name="Contact" UId="15"><Negated Name="operand" /></Part>', '<Part Name="Contact" UId="15" />') }
        'reset-inverted' { $xml = $xml.Replace('<Part Name="Contact" UId="16"><Negated Name="operand" /></Part>', '<Part Name="Contact" UId="16" />') }
        'permit-inverted' { $xml = $xml.Replace('<Part Name="Contact" UId="13"></Part>', '<Part Name="Contact" UId="13"><Negated Name="operand" /></Part>') }
        'guard-inverted' { $xml = $xml.Replace('<Part Name="Contact" UId="14"></Part>', '<Part Name="Contact" UId="14"><Negated Name="operand" /></Part>') }
        'wrong-output' { $xml = $xml.Replace('Name="RunRequest"', 'Name="OtherOutput"') }
        'unknown-input' { $xml = $xml.Replace('Name="Permit"', 'Name="Unknown"') }
        'latched-output' { $xml = $xml.Replace('Name="Coil"', 'Name="SCoil"') }
        'duplicate-id' { $xml = $xml.Replace('UId="17"', 'UId="16"') }
        'missing-wire-id' { $xml = $xml.Replace('<Wire UId="30">', '<Wire>') }
        'duplicate-connector' { $xml = $xml.Replace('<NameCon UId="10" Name="in" />', '<NameCon UId="10" Name="in" /><NameCon UId="10" Name="in" />') }
        'duplicate-container' { $xml = $xml.Replace('</Parts>', '</Parts><Parts />') }
        'duplicate-pin-wire' { $xml = $xml.Replace('</Wires>', '<Wire UId="100"><Powerrail /><NameCon UId="17" Name="in" /></Wire></Wires>') }
        'missing-operand' { $xml = $xml.Replace('<IdentCon UId="3" />', '<IdentCon UId="99" />') }
        'unsupported-pin' { $xml = $xml.Replace('UId="13" Name="in"', 'UId="13" Name="in2"') }
        'unsupported-part' { $xml = $xml.Replace('Name="O"', 'Name="Unknown"') }
        'wrong-cardinality' { $xml = $xml.Replace('>2</TemplateValue>', '>3</TemplateValue>') }
        'extra-part' { $xml = $xml.Replace('</Parts>', '<Part Name="Contact" UId="99" /></Parts>') }
        'extra-wire' { $xml = $xml.Replace('</Wires>', '<Wire UId="100"><Powerrail /><NameCon UId="13" Name="out" /></Wire></Wires>') }
        'wrong-language' { $xml = $xml.Replace('>LAD<', '>SCL<') }
        'wrong-namespace' { $xml = $xml.Replace('FlgNet/v5', 'FlgNet/v99') }
        'dtd' { $xml = '<!DOCTYPE Document [<!ENTITY leak SYSTEM "file:///C:/private.txt">]>' + $xml }
        'renamed-ids' { $xml = $xml -replace 'UId="([0-9]+)"', 'UId="renamed_$1"' }
        'commuted-branches' { $xml = $xml.Replace('Name="AutoStart"', 'Name="SWAP"').Replace('Name="JogRequest"', 'Name="AutoStart"').Replace('Name="SWAP"', 'Name="JogRequest"') }
        'commuted-guards' { $xml = $xml.Replace('Name="Permit"', 'Name="SWAP"').Replace('Name="GuardClosed"', 'Name="Permit"').Replace('Name="SWAP"', 'Name="GuardClosed"') }
    }
    return $xml
}
function New-TestTimerXml {
    param([string]$Variant = '')
    $parts = (New-TestAccess '1' 'RunRequest') + (New-TestAccess '3' 'DelayedRun') +
        '<Access Scope="TypedConstant" UId="2"><Constant><ConstantValue>T#750ms</ConstantValue></Constant></Access>' +
        '<Part Name="Contact" UId="10" /><Part Name="TON" UId="11"><Instance Scope="LocalVariable" UId="20"><Component Name="RunDelay" /></Instance><TemplateValue Name="time_type" Type="Type">Time</TemplateValue></Part><Part Name="Coil" UId="12" />'
    $ends = @(
        ('<Powerrail />' + (New-TestPin '10' 'in')),
        ('<IdentCon UId="1" />' + (New-TestPin '10' 'operand')),
        ((New-TestPin '10' 'out') + (New-TestPin '11' 'IN')),
        ('<IdentCon UId="2" />' + (New-TestPin '11' 'PT')),
        ((New-TestPin '11' 'ET') + '<OpenCon UId="21" />'),
        ((New-TestPin '11' 'Q') + (New-TestPin '12' 'in')),
        ('<IdentCon UId="3" />' + (New-TestPin '12' 'operand'))
    )
    $wires = ''; for ($i=0; $i -lt $ends.Count; $i++) { $wires += New-TestWire ([string](30+$i)) $ends[$i] }
    $xml = New-TestGraphXml $parts $wires '<Member Name="RunDelay" Datatype="TON_TIME" Remanence="NonRetain" />'
    switch ($Variant) {
        'wrong-preset' { $xml = $xml.Replace('T#750ms', 'T#100ms') }
        'equivalent-preset' { $xml = $xml.Replace('T#750ms', 'T#0.75s') }
        'wrong-input' { $xml = $xml.Replace('Name="RunRequest"', 'Name="AutoStart"') }
        'wrong-output' { $xml = $xml.Replace('Name="DelayedRun"', 'Name="RunRequest"') }
        'wrong-instance' { $xml = $xml.Replace('<Component Name="RunDelay"', '<Component Name="OtherTimer"') }
        'wrong-static-type' { $xml = $xml.Replace('Datatype="TON_TIME"', 'Datatype="IEC_TIMER"') }
        'temporary-state' { $xml = $xml.Replace('Name="Static"', 'Name="Temp"') }
        'retained-state' { $xml = $xml.Replace('Remanence="NonRetain"', 'Remanence="Retain"') }
        'retentive-timer' { $xml = $xml.Replace('Name="TON"', 'Name="TONR"') }
        'q-bypass' { $xml = $xml.Replace('UId="11" Name="Q"', 'UId="10" Name="out"') }
        'wrong-template' { $xml = $xml.Replace('>Time</TemplateValue>', '>LTime</TemplateValue>') }
        'bound-et' { $xml = $xml.Replace('<OpenCon UId="21" />', '<IdentCon UId="3" />') }
        'inverted-contact' { $xml = $xml.Replace('<Part Name="Contact" UId="10" />', '<Part Name="Contact" UId="10"><Negated Name="operand" /></Part>') }
    }
    return $xml
}
