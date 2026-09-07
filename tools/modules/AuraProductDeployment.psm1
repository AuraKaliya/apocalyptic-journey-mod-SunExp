Set-StrictMode -Version Latest

function Assert-AuraDeploymentPath {
    param([string]$Root, [string]$Path)
    $absoluteRoot=[IO.Path]::GetFullPath($Root).TrimEnd('\','/')
    $absolute=[IO.Path]::GetFullPath($Path)
    if(-not $absolute.StartsWith($absoluteRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw "Deployment path escapes root: $Path"}
    $ancestor=$absolute
    while(-not[string]::IsNullOrWhiteSpace($ancestor)){
        if(Test-Path -LiteralPath $ancestor){
            if(((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "Deployment path contains a reparse point: $ancestor"}
        }
        $ancestor=Split-Path -Parent $ancestor
    }
}

function Write-AuraDeploymentJournal {
    param([string]$Path, [object]$Document)
    $temp=$Path+'.pending'
    $bytes=[Text.Encoding]::UTF8.GetBytes(($Document|ConvertTo-Json -Depth 9))
    $stream=[IO.File]::Open($temp,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
    # PowerShell converts $null to an empty string for this overload. Use an
    # explicit previous-journal path, which also preserves the last durable state.
    if(Test-Path -LiteralPath $Path){[IO.File]::Replace($temp,$Path,$Path+'.previous')}else{[IO.File]::Move($temp,$Path)}
}

function Restore-AuraDeploymentOperations {
    param([object[]]$Operations,[string]$ModsRoot,[string]$BackupRoot)
    # Validate every target and original before touching any installed file.
    foreach($operation in $Operations){
        Assert-AuraDeploymentPath $ModsRoot $operation.target
        Assert-AuraDeploymentPath $BackupRoot $operation.backup
        if($operation.existed -and (Get-FileHash -LiteralPath $operation.backup).Hash -ne $operation.previousSha256){throw "Original backup is damaged: $($operation.relative)"}
    }
    for($index=$Operations.Count-1;$index -ge 0;$index--){
        $operation=$Operations[$index]
        if($operation.existed){
            [IO.Directory]::CreateDirectory((Split-Path -Parent $operation.target))|Out-Null
            Copy-Item -LiteralPath $operation.backup -Destination $operation.target -Force
            if((Get-FileHash -LiteralPath $operation.target).Hash -ne $operation.previousSha256){throw "Original restore failed: $($operation.relative)"}
        }elseif(Test-Path -LiteralPath $operation.target -PathType Leaf){Remove-Item -LiteralPath $operation.target -Force}
    }
}

Export-ModuleMember -Function Assert-AuraDeploymentPath,Write-AuraDeploymentJournal,Restore-AuraDeploymentOperations
