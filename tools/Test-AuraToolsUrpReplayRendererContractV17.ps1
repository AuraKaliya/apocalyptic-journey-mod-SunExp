param(
    [string]$ManagedPath = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $ManagedPath = Join-Path $repoRoot "Managed"
}
if (-not (Test-Path -LiteralPath $ManagedPath -PathType Container)) {
    throw "Managed directory is missing: $ManagedPath"
}

$requiredAssemblies = @(
    "UnityEngine.CoreModule.dll",
    "Unity.RenderPipelines.Core.Runtime.dll",
    "Unity.RenderPipelines.Universal.Runtime.dll",
    "Unity.RenderPipelines.Universal.2D.Runtime.dll",
    "Postprocess.dll")
foreach ($name in $requiredAssemblies) {
    $path = Join-Path $ManagedPath $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Replay URP compatibility assembly is missing: $path"
    }
    [System.Reflection.Assembly]::LoadFrom($path) | Out-Null
}

function Get-RequiredType([string]$FullName) {
    $type = [AppDomain]::CurrentDomain.GetAssemblies() |
        ForEach-Object { $_.GetType($FullName, $false, $false) } |
        Where-Object { $null -ne $_ } |
        Select-Object -First 1
    if ($null -eq $type) {
        throw "Replay URP compatibility type is missing: $FullName"
    }
    return $type
}

function Find-InstanceMember([Type]$Type, [string]$Kind, [string]$Name) {
    $flags = [System.Reflection.BindingFlags]::Instance `
        -bor [System.Reflection.BindingFlags]::Public `
        -bor [System.Reflection.BindingFlags]::NonPublic `
        -bor [System.Reflection.BindingFlags]::DeclaredOnly
    for ($current = $Type; $null -ne $current; $current = $current.BaseType) {
        if ($Kind -eq "Field") {
            $member = $current.GetField($Name, $flags)
        }
        elseif ($Kind -eq "Property") {
            $member = $current.GetProperty($Name, $flags)
        }
        else {
            $member = @($current.GetMethods($flags) | Where-Object {
                $_.Name -eq $Name
            }) | Select-Object -First 1
        }
        if ($null -ne $member) { return $member }
    }
    throw "Replay URP compatibility member is missing: $($Type.FullName).$Name"
}

$assetType = Get-RequiredType "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset"
$dataType = Get-RequiredType "UnityEngine.Rendering.Universal.ScriptableRendererData"
$cameraDataType = Get-RequiredType "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData"
$rendererFeatureType = Get-RequiredType "UnityEngine.Rendering.Universal.ScriptableRendererFeature"
$renderPassType = Get-RequiredType "UnityEngine.Rendering.Universal.ScriptableRenderPass"
$fullScreenFeatureType = Get-RequiredType "UnityEngine.Rendering.Universal.FullScreenPassRendererFeature"
$renderer2DType = Get-RequiredType "UnityEngine.Rendering.Universal.Renderer2D"
$uiBlurFeatureType = Get-RequiredType "UIBlurGrabPassFeature"

foreach ($field in @("m_RendererDataList", "m_Renderers", "m_DefaultRendererIndex")) {
    [void](Find-InstanceMember $assetType "Field" $field)
}
[void](Find-InstanceMember $assetType "Method" "GetRenderer")
[void](Find-InstanceMember $dataType "Property" "rendererFeatures")
[void](Find-InstanceMember $dataType "Field" "m_RendererFeatureMap")
[void](Find-InstanceMember $dataType "Method" "InternalCreateRenderer")
[void](Find-InstanceMember $rendererFeatureType "Property" "isActive")
[void](Find-InstanceMember $renderPassType "Method" "ConfigureInput")
[void](Find-InstanceMember $renderPassType "Method" "RecordRenderGraph")
[void](Find-InstanceMember $renderer2DType "Method" "GetRenderPassInputs")
foreach ($field in @("fetchColorBuffer", "requirements", "passMaterial", "passIndex")) {
    [void](Find-InstanceMember $fullScreenFeatureType "Field" $field)
}
$fullScreenPassType = $fullScreenFeatureType.GetNestedType(
    "FullScreenRenderPass",
    [System.Reflection.BindingFlags]::NonPublic)
if ($null -eq $fullScreenPassType) {
    throw "Replay URP full-screen RenderGraph pass type is unavailable."
}
[void](Find-InstanceMember $fullScreenPassType "Method" "RecordRenderGraph")
$uiBlurPassType = $uiBlurFeatureType.GetNestedType(
    "UIBlurGrabPass",
    [System.Reflection.BindingFlags]::NonPublic)
if ($null -eq $uiBlurPassType) {
    throw "Native UI blur pass type is unavailable."
}
$declaredRecordRenderGraph = $uiBlurPassType.GetMethod(
    "RecordRenderGraph",
    [System.Reflection.BindingFlags]::Instance `
        -bor [System.Reflection.BindingFlags]::Public `
        -bor [System.Reflection.BindingFlags]::NonPublic `
        -bor [System.Reflection.BindingFlags]::DeclaredOnly)
if ($null -ne $declaredRecordRenderGraph) {
    throw "Native UI blur now implements RenderGraph; replay feature policy must be reviewed instead of excluding it."
}

$setRenderer = @($cameraDataType.GetMethods(
        [System.Reflection.BindingFlags]::Instance `
        -bor [System.Reflection.BindingFlags]::Public `
        -bor [System.Reflection.BindingFlags]::NonPublic) | Where-Object {
        $_.Name -eq "SetRenderer" `
            -and $_.GetParameters().Count -eq 1 `
            -and $_.GetParameters()[0].ParameterType -eq [int]
    })
if ($setRenderer.Count -ne 1) {
    throw "Replay URP SetRenderer(int) compatibility contract is unavailable."
}
foreach ($property in @(
        "scriptableRenderer",
        "renderPostProcessing",
        "renderShadows",
        "requiresColorTexture",
        "requiresDepthTexture",
        "allowXRRendering")) {
    [void](Find-InstanceMember $cameraDataType "Property" $property)
}

Write-Host "AuraTools replay URP renderer compatibility contract passed."
