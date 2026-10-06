# Restricted, fail-closed analysis of independently exported Siemens LAD XML.
# This checks combinational power flow and timer wiring, not PLC runtime timing.
function Resolve-BenchmarkWorkspaceArtifact {
    param([string]$WorkspaceRoot, [string]$RelativePath)
    if ([string]::IsNullOrWhiteSpace($WorkspaceRoot) -or [string]::IsNullOrWhiteSpace($RelativePath) -or
        [IO.Path]::IsPathRooted($RelativePath) -or @($RelativePath -split '[\\/]' | Where-Object { $_ -eq '..' }).Count -gt 0) {
        throw 'Artifact must have a workspace-relative path without traversal.'
    }
    $root = [IO.Path]::GetFullPath($WorkspaceRoot).TrimEnd('\', '/')
    $path = [IO.Path]::GetFullPath((Join-Path $root $RelativePath))
    if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Artifact is outside the workspace.' }
    $file = Get-Item -LiteralPath $path -ErrorAction Stop
    if ($file.PSIsContainer -or $file.Length -gt 2000000) { throw 'Artifact is not a bounded XML file.' }
    # Refuse reparse points on the file and every directory below the workspace.
    $item = $file
    while ($item.FullName.Length -gt $root.Length) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse-point artifacts are not accepted.' }
        $item = Get-Item -LiteralPath (Split-Path -Parent $item.FullName)
    }
    return $path
}

function Read-BenchmarkLadXml {
    param([string]$Path)
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersInDocument = 2000000
    $reader = [Xml.XmlReader]::Create($Path, $settings)
    try {
        $xml = [Xml.XmlDocument]::new()
        $xml.XmlResolver = $null
        $xml.Load($reader)
        return ,$xml
    }
    finally { $reader.Dispose() }
}

function Get-BenchmarkLadGraph {
    param([Xml.XmlDocument]$Xml, [int]$NetworkIndex)
    $blocks = @($Xml.SelectNodes('/Document/SW.Blocks.FB | /Document/SW.Blocks.FC | /Document/SW.Blocks.OB'))
    if ($blocks.Count -ne 1) { throw 'Exactly one PLC block is required.' }
    $units = @($blocks[0].SelectNodes('ObjectList/SW.Blocks.CompileUnit'))
    if ($NetworkIndex -lt 1 -or $NetworkIndex -gt $units.Count) { throw 'Network index is outside the block.' }
    $unit = $units[$NetworkIndex - 1]
    if ($unit.SelectSingleNode('AttributeList/ProgrammingLanguage').InnerText -cne 'LAD') { throw 'Network is not LAD.' }
    $nets = @($unit.SelectNodes('AttributeList/NetworkSource/*'))
    if ($nets.Count -ne 1 -or $nets[0].LocalName -cne 'FlgNet' -or
        $nets[0].NamespaceURI -notmatch '^http://www\.siemens\.com/automation/Openness/SW/NetworkSource/FlgNet/v[45]$') { throw 'Unsupported network representation.' }
    $net = $nets[0]
    $containers = @($net.SelectNodes('*'))
    if ($containers.Count -ne 2 -or @($containers | Where-Object { $_.LocalName -ceq 'Parts' }).Count -ne 1 -or
        @($containers | Where-Object { $_.LocalName -ceq 'Wires' }).Count -ne 1) { throw 'Expected exactly one Parts and Wires container.' }
    $parts = @{}
    $access = @{}
    $allIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $children = @($net.SelectNodes('*[local-name()="Parts"]/*'))
    if ($children.Count -gt 128) { throw 'Network exceeds the supported node limit.' }
    foreach ($node in $children) {
        $id = $node.GetAttribute('UId')
        if (-not $id -or -not $allIds.Add($id)) { throw 'Missing or duplicate network UId.' }
        if ($node.LocalName -ceq 'Part') { $parts[$id] = $node }
        elseif ($node.LocalName -ceq 'Access') { $access[$id] = $node }
        else { throw 'Unsupported Parts child.' }
    }
    $wires = @($net.SelectNodes('*[local-name()="Wires"]/*'))
    if ($wires.Count -gt 256) { throw 'Network exceeds the supported wire limit.' }
    foreach ($wire in $wires) {
        if ($wire.LocalName -cne 'Wire' -or -not $wire.GetAttribute('UId') -or -not $allIds.Add($wire.GetAttribute('UId'))) { throw 'Invalid or duplicate wire UId.' }
        $endKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($end in $wire.ChildNodes) {
            if ($end -isnot [Xml.XmlElement]) { continue }
            $key = $end.LocalName + '|' + $end.GetAttribute('UId') + '|' + $end.GetAttribute('Name')
            if (-not $endKeys.Add($key)) { throw 'Duplicate connector on a wire.' }
            switch -CaseSensitive ($end.LocalName) {
                'NameCon' {
                    if (-not $parts.ContainsKey($end.GetAttribute('UId'))) { throw 'Wire references a missing part.' }
                    $partName = $parts[$end.GetAttribute('UId')].GetAttribute('Name')
                    $pin = $end.GetAttribute('Name')
                    $validPin = switch -CaseSensitive ($partName) {
                        'Contact' { $pin -cin @('in', 'out', 'operand') }
                        'Coil' { $pin -cin @('in', 'operand') }
                        'O' { $pin -cmatch '^(out|in[1-8])$' }
                        'TON' { $pin -cin @('IN', 'PT', 'ET', 'Q') }
                        default { $false }
                    }
                    if (-not $validPin) { throw "Unsupported pin '$partName/$pin'." }
                }
                'IdentCon' { if (-not $access.ContainsKey($end.GetAttribute('UId'))) { throw 'Wire references a missing operand.' } }
                'OpenCon' { if (-not $end.GetAttribute('UId') -or -not $allIds.Add($end.GetAttribute('UId'))) { throw 'Missing or duplicate open connector UId.' } }
                'Powerrail' { }
                default { throw 'Unsupported wire connector.' }
            }
        }
    }
    return @{ Block = $blocks[0]; Parts = $parts; Access = $access; Wires = $wires
        UsedParts = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        UsedAccess = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        UsedWires = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal) }
}

function Get-BenchmarkLadPinWire {
    param([hashtable]$Graph, [string]$PartId, [string]$Pin)
    $matches = @($Graph.Wires | Where-Object {
        @($_.ChildNodes | Where-Object { $_ -is [Xml.XmlElement] -and $_.LocalName -ceq 'NameCon' -and
            $_.GetAttribute('UId') -ceq $PartId -and $_.GetAttribute('Name') -ceq $Pin }).Count -gt 0
    })
    if ($matches.Count -ne 1) { throw "Pin '$PartId/$Pin' must belong to exactly one wire." }
    [void]$Graph.UsedWires.Add($matches[0].GetAttribute('UId'))
    return ,$matches[0]
}

function Get-BenchmarkLadOperand {
    param([hashtable]$Graph, [string]$PartId, [string]$Pin = 'operand', [switch]$Constant)
    $wire = Get-BenchmarkLadPinWire $Graph $PartId $Pin
    $ends = @($wire.ChildNodes | Where-Object { $_ -is [Xml.XmlElement] })
    $ids = @($ends | Where-Object { $_.LocalName -ceq 'IdentCon' })
    if ($ends.Count -ne 2 -or $ids.Count -ne 1) { throw 'Operand wire must bind exactly one access.' }
    $id = $ids[0].GetAttribute('UId')
    [void]$Graph.UsedAccess.Add($id)
    $node = $Graph.Access[$id]
    if ($Constant) {
        if ($node.GetAttribute('Scope') -cne 'TypedConstant') { throw 'Timer PT must be a typed constant.' }
        $values = @($node.SelectNodes('*[local-name()="Constant"]/*[local-name()="ConstantValue"]'))
        if ($values.Count -ne 1) { throw 'Constant has no unique value.' }
        return $values[0].InnerText
    }
    if ($node.GetAttribute('Scope') -cne 'LocalVariable') { throw 'Only exact local Boolean operands are supported.' }
    $components = @($node.SelectNodes('*[local-name()="Symbol"]/*'))
    if ($components.Count -ne 1 -or $components[0].LocalName -cne 'Component') { throw 'Operand is not a scalar local variable.' }
    return $components[0].GetAttribute('Name')
}

function Get-BenchmarkLadPowerSource {
    param([hashtable]$Graph, [string]$PartId, [string]$Pin)
    $wire = Get-BenchmarkLadPinWire $Graph $PartId $Pin
    $sources = @($wire.ChildNodes | Where-Object { $_ -is [Xml.XmlElement] -and
        ($_.LocalName -ceq 'Powerrail' -or ($_.LocalName -ceq 'NameCon' -and $_.GetAttribute('Name') -cin @('out', 'Q'))) })
    if ($sources.Count -ne 1) { throw 'Power wire must have exactly one supported source.' }
    foreach ($end in @($wire.ChildNodes | Where-Object { $_ -is [Xml.XmlElement] })) {
        if ($end.LocalName -ceq 'Powerrail') { continue }
        if ($end.LocalName -cne 'NameCon' -or $end.GetAttribute('Name') -cnotmatch '^(out|Q|in[0-9]*|IN)$') { throw 'Unsupported power-wire sink.' }
    }
    return ,$sources[0]
}

function Get-BenchmarkLadBooleanValue {
    param([hashtable]$Graph, [string]$PartId, [hashtable]$Values, [hashtable]$Cache, [Collections.Generic.HashSet[string]]$Visiting)
    if ($Cache.ContainsKey($PartId)) { return [bool]$Cache[$PartId] }
    if (-not $Visiting.Add($PartId)) { throw 'Combinational power-flow cycle.' }
    [void]$Graph.UsedParts.Add($PartId)
    $part = $Graph.Parts[$PartId]
    $name = $part.GetAttribute('Name')
    $pins = @('in')
    $operand = $null
    $negated = $false
    if ($name -ceq 'Contact' -or $name -ceq 'Coil') {
        $operand = Get-BenchmarkLadOperand $Graph $PartId
        $negations = @($part.SelectNodes('*[local-name()="Negated"]'))
        if ($negations.Count -gt 1 -or ($negations.Count -eq 1 -and $negations[0].GetAttribute('Name') -cne 'operand') -or
            ($name -ceq 'Coil' -and $negations.Count -gt 0)) { throw 'Unsupported negation.' }
        $negated = $negations.Count -eq 1
        if ($name -ceq 'Contact' -and -not $Values.ContainsKey($operand)) { throw "Unexpected contact operand '$operand'." }
        if (@($part.SelectNodes('*[not(local-name()="Negated")]')).Count -ne 0) { throw 'Unsupported contact/coil metadata.' }
    }
    elseif ($name -ceq 'O') {
        $cards = @($part.SelectNodes('*[local-name()="TemplateValue" and @Name="Card" and @Type="Cardinality"]'))
        if ($cards.Count -ne 1 -or @($part.SelectNodes('*')).Count -ne 1 -or $cards[0].InnerText -notmatch '^[2-8]$') { throw 'Unsupported OR cardinality.' }
        $pins = @(1..[int]$cards[0].InnerText | ForEach-Object { "in$_" })
    }
    else { throw "Unsupported combinational part '$name'." }
    $power = $false
    foreach ($pin in $pins) {
        $source = Get-BenchmarkLadPowerSource $Graph $PartId $pin
        $incoming = if ($source.LocalName -ceq 'Powerrail') { $true } else {
            if ($source.GetAttribute('Name') -cne 'out') { throw 'A timer is not a combinational source.' }
            Get-BenchmarkLadBooleanValue $Graph $source.GetAttribute('UId') $Values $Cache $Visiting
        }
        $power = $power -or $incoming
    }
    if ($name -ceq 'Contact') {
        $gate = [bool]$Values[$operand]
        if ($negated) { $gate = -not $gate }
        $power = $power -and $gate
    }
    [void]$Visiting.Remove($PartId)
    $Cache[$PartId] = $power
    return [bool]$power
}

function Assert-BenchmarkLadGraphConsumed {
    param([hashtable]$Graph)
    if ($Graph.UsedParts.Count -ne $Graph.Parts.Count -or $Graph.UsedAccess.Count -ne $Graph.Access.Count -or
        $Graph.UsedWires.Count -ne $Graph.Wires.Count) { throw 'Unconnected, unused or unsupported network content.' }
}

function Test-BenchmarkLadBooleanNetwork {
    param([string]$WorkspaceRoot, [object]$Assertion)
    $path = Resolve-BenchmarkWorkspaceArtifact $WorkspaceRoot ([string]$Assertion.relativePath)
    $graph = Get-BenchmarkLadGraph (Read-BenchmarkLadXml $path) ([int]$Assertion.networkIndex)
    $names = @($Assertion.inputNames | ForEach-Object { [string]$_ })
    if ($names.Count -lt 1 -or $names.Count -gt 10 -or @($names | Select-Object -Unique).Count -ne $names.Count) { throw 'Unsupported Boolean input list.' }
    $coils = @($graph.Parts.Values | Where-Object { $_.GetAttribute('Name') -ceq 'Coil' })
    if ($coils.Count -ne 1 -or (Get-BenchmarkLadOperand $graph $coils[0].GetAttribute('UId')) -cne [string]$Assertion.outputName) { throw 'Expected exactly one ordinary output coil.' }
    $rows = 1 -shl $names.Count
    $expected = @($Assertion.expectedTrueRows | ForEach-Object { [int]$_ })
    if (@($expected | Select-Object -Unique).Count -ne $expected.Count -or @($expected | Where-Object { $_ -lt 0 -or $_ -ge $rows }).Count -gt 0) { throw 'Invalid expected truth table.' }
    for ($row = 0; $row -lt $rows; $row++) {
        $values = @{}
        for ($index = 0; $index -lt $names.Count; $index++) { $values[$names[$index]] = ($row -band (1 -shl $index)) -ne 0 }
        $actual = Get-BenchmarkLadBooleanValue $graph $coils[0].GetAttribute('UId') $values @{} ([Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal))
        Assert-BenchmarkLadGraphConsumed $graph
        if ($actual -ne ($expected -contains $row)) { throw "Power-flow mismatch at truth-table row $row; actual=$actual." }
    }
    return "Verified $rows combinational rows from exported LAD wiring; output=$($Assertion.outputName). Runtime timing not tested."
}

function Assert-BenchmarkLadWireLink {
    param([hashtable]$Graph, [string]$SinkId, [string]$SinkPin, [string]$SourceId, [string]$SourcePin)
    $wire = Get-BenchmarkLadPinWire $Graph $SinkId $SinkPin
    $ends = @($wire.ChildNodes | Where-Object { $_ -is [Xml.XmlElement] })
    if ($ends.Count -ne 2) { throw 'Timer power wires must have exactly two endpoints.' }
    $source = Get-BenchmarkLadPowerSource $Graph $SinkId $SinkPin
    if ($SourceId -ceq 'powerrail') {
        if ($source.LocalName -cne 'Powerrail') { throw 'Timer input is not powered from the rail.' }
    }
    elseif ($source.LocalName -cne 'NameCon' -or $source.GetAttribute('UId') -cne $SourceId -or $source.GetAttribute('Name') -cne $SourcePin) { throw 'Wrong timer power-flow binding.' }
}

function Test-BenchmarkLadTimerNetwork {
    param([string]$WorkspaceRoot, [object]$Assertion)
    $path = Resolve-BenchmarkWorkspaceArtifact $WorkspaceRoot ([string]$Assertion.relativePath)
    $graph = Get-BenchmarkLadGraph (Read-BenchmarkLadXml $path) ([int]$Assertion.networkIndex)
    $contacts = @($graph.Parts.Values | Where-Object { $_.GetAttribute('Name') -ceq 'Contact' })
    $timers = @($graph.Parts.Values | Where-Object { $_.GetAttribute('Name') -ceq 'TON' })
    $coils = @($graph.Parts.Values | Where-Object { $_.GetAttribute('Name') -ceq 'Coil' })
    if ($graph.Parts.Count -ne 3 -or $contacts.Count -ne 1 -or $timers.Count -ne 1 -or $coils.Count -ne 1) { throw 'Expected one contact, one nonretentive TON and one ordinary coil.' }
    $contactId = $contacts[0].GetAttribute('UId'); $timerId = $timers[0].GetAttribute('UId'); $coilId = $coils[0].GetAttribute('UId')
    foreach ($part in @($contacts[0], $timers[0], $coils[0])) {
        [void]$graph.UsedParts.Add($part.GetAttribute('UId'))
        if (@($part.SelectNodes('*[local-name()="Negated"]')).Count -ne 0) { throw 'Inverted timer/contact/coil is not supported.' }
    }
    if (@($contacts[0].SelectNodes('*')).Count -ne 0 -or @($coils[0].SelectNodes('*')).Count -ne 0) { throw 'Unexpected timer network instruction metadata.' }
    if ((Get-BenchmarkLadOperand $graph $contactId) -cne [string]$Assertion.inputName -or
        (Get-BenchmarkLadOperand $graph $coilId) -cne [string]$Assertion.outputName) { throw 'Wrong timer contact or coil operand.' }
    $instances = @($timers[0].SelectNodes('*[local-name()="Instance" and @Scope="LocalVariable"]'))
    $templates = @($timers[0].SelectNodes('*[local-name()="TemplateValue" and @Name="time_type" and @Type="Type"]'))
    if ($instances.Count -ne 1 -or $templates.Count -ne 1 -or $templates[0].InnerText -cne 'Time' -or
        @($timers[0].SelectNodes('*')).Count -ne 2) { throw 'Wrong timer instance or type template.' }
    $components = @($instances[0].SelectNodes('*'))
    if ($components.Count -ne 1 -or $components[0].LocalName -cne 'Component' -or
        $components[0].GetAttribute('Name') -cne [string]$Assertion.timerInstance) { throw 'Wrong timer state receiver.' }
    $static = @($graph.Block.SelectNodes('AttributeList/Interface/*[local-name()="Sections"]/*[local-name()="Section" and @Name="Static"]/*[local-name()="Member"]') |
        Where-Object { $_.GetAttribute('Name') -ceq [string]$Assertion.timerInstance })
    if ($static.Count -ne 1 -or $static[0].GetAttribute('Datatype') -cne 'TON_TIME' -or
        $static[0].GetAttribute('Remanence') -cne 'NonRetain') { throw 'Timer state must remain the existing nonretained FB static TON_TIME.' }
    Assert-BenchmarkLadWireLink $graph $contactId 'in' 'powerrail' ''
    Assert-BenchmarkLadWireLink $graph $timerId 'IN' $contactId 'out'
    Assert-BenchmarkLadWireLink $graph $coilId 'in' $timerId 'Q'
    $literal = Get-BenchmarkLadOperand $graph $timerId 'PT' -Constant
    if ($literal -cnotmatch '^T#(?<amount>[0-9]+(?:\.[0-9]+)?)(?<unit>ms|s)$') { throw 'Unsupported TIME literal.' }
    $milliseconds = [decimal]::Parse($Matches.amount, [Globalization.CultureInfo]::InvariantCulture)
    if ($Matches.unit -ceq 's') { $milliseconds *= 1000 }
    if ($milliseconds -ne [decimal]$Assertion.presetMilliseconds) { throw 'Wrong timer preset.' }
    $et = Get-BenchmarkLadPinWire $graph $timerId 'ET'
    $etEnds = @($et.ChildNodes | Where-Object { $_ -is [Xml.XmlElement] })
    if ($etEnds.Count -ne 2 -or @($etEnds | Where-Object { $_.LocalName -ceq 'OpenCon' }).Count -ne 1) { throw 'ET must remain unbound.' }
    Assert-BenchmarkLadGraphConsumed $graph
    return "Verified static TON receiver, IN/Q wiring and $milliseconds ms PT. Runtime timing not tested."
}
