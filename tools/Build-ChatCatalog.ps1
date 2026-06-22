param(
    [string]$OutputPath = "",
    [string]$SignPrivateKeyPath = "",
    [string]$SignPrivateKeyXml = "",
    [string]$CatalogVersion = "2026.06.22"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "TestMods\ChatExp\SharedResources\Chat\catalog.auraenc"
}

if ([string]::IsNullOrWhiteSpace($SignPrivateKeyPath)) {
    $defaultKeyPath = Join-Path $repoRoot "TestMods\ChatExp-Dev\Secrets\chat-sign-private.xml"
    if (Test-Path $defaultKeyPath) {
        $SignPrivateKeyPath = $defaultKeyPath
    }
}

if ([string]::IsNullOrWhiteSpace($SignPrivateKeyXml) -and -not [string]::IsNullOrWhiteSpace($SignPrivateKeyPath)) {
    $SignPrivateKeyXml = Get-Content -Raw -Path $SignPrivateKeyPath
}

if ([string]::IsNullOrWhiteSpace($SignPrivateKeyXml) -and -not [string]::IsNullOrWhiteSpace($env:CHATEXP_SIGN_PRIVATE_KEY_XML)) {
    $SignPrivateKeyXml = $env:CHATEXP_SIGN_PRIVATE_KEY_XML
}

if ([string]::IsNullOrWhiteSpace($SignPrivateKeyXml)) {
    throw "Provide the RSA signing private key through -SignPrivateKeyPath, -SignPrivateKeyXml, or CHATEXP_SIGN_PRIVATE_KEY_XML."
}

$encryptionPublicKeyXml = '<RSAKeyValue><Modulus>uMzKnra1A1YRFvWDFeb+XQGxg7f8rYSbojFZ2zOif96V/gGxXitjJcmMWI5uiY24kJJaTLwOct26nkD4Z1qSbZKfgasJ1+kCZYin8tE3iYVFNnumVYT/YJljyajY+qCwowVbO0Jct9CTwr1ZNJt503xd/65fOdk2YyXVieA78+4dqVAXUhajpgZ4GlxISD1AGM2cvKMKHuqqPXqTCAdd5QNkAcMI8v5AoYJGuioOQCkUSUjxem1ArRQR+pdPYGjQSR/7jiBTBwjEw6n6fjiFtaZReWBsVSNnFWj7++QMm+oNBkUqVeLSQYZ8LAdXMrjvO+kj+AeUh2w0Qs4CnhOFBQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>'

function ConvertTo-Hex {
    param([byte[]]$Bytes)
    $builder = [System.Text.StringBuilder]::new($Bytes.Length * 2)
    foreach ($value in $Bytes) {
        [void]$builder.Append($value.ToString("x2"))
    }
    $builder.ToString()
}

function Canonicalize-Envelope {
    param($Envelope)
    "Format=$($Envelope.Format)`n" +
    "Version=$($Envelope.Version)`n" +
    "KeyId=$($Envelope.KeyId)`n" +
    "EncAlg=$($Envelope.EncAlg)`n" +
    "SigAlg=$($Envelope.SigAlg)`n" +
    "EncryptedKey=$($Envelope.EncryptedKey)`n" +
    "Iv=$($Envelope.Iv)`n" +
    "Ciphertext=$($Envelope.Ciphertext)`n" +
    "Hmac=$($Envelope.Hmac)`n" +
    "PayloadSha256=$($Envelope.PayloadSha256)"
}

$catalog = [ordered]@{
    schemaVersion = 1
    catalogId = "chat_catalog_v1"
    catalogVersion = $CatalogVersion
    messages = @(
        [ordered]@{ id = "bye"; text = "再见！"; order = 10 },
        [ordered]@{ id = "thanks"; text = "谢谢！"; order = 20 },
        [ordered]@{ id = "hello"; text = "你好！"; order = 30 }
    )
    stickers = @()
}

$catalogJson = $catalog | ConvertTo-Json -Depth 8 -Compress
$plainBytes = [System.Text.Encoding]::UTF8.GetBytes($catalogJson)
$payloadSha256 = ConvertTo-Hex ([System.Security.Cryptography.SHA256]::Create().ComputeHash($plainBytes))

$keyMaterial = New-Object byte[] 64
$iv = New-Object byte[] 16
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($keyMaterial)
$rng.GetBytes($iv)

$aesKey = New-Object byte[] 32
$hmacKey = New-Object byte[] 32
[Array]::Copy($keyMaterial, 0, $aesKey, 0, 32)
[Array]::Copy($keyMaterial, 32, $hmacKey, 0, 32)

$aes = [System.Security.Cryptography.Aes]::Create()
$aes.KeySize = 256
$aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
$aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
$aes.Key = $aesKey
$aes.IV = $iv
$encryptor = $aes.CreateEncryptor()
$ciphertext = $encryptor.TransformFinalBlock($plainBytes, 0, $plainBytes.Length)

$macInput = New-Object byte[] ($iv.Length + $ciphertext.Length)
[Array]::Copy($iv, 0, $macInput, 0, $iv.Length)
[Array]::Copy($ciphertext, 0, $macInput, $iv.Length, $ciphertext.Length)
$hmac = [System.Security.Cryptography.HMACSHA256]::new($hmacKey)
$macBytes = $hmac.ComputeHash($macInput)

$encryptRsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new(2048)
$encryptRsa.FromXmlString($encryptionPublicKeyXml)
$encryptedKey = $encryptRsa.Encrypt($keyMaterial, $true)

$envelope = [ordered]@{
    format = "AuraChatCatalogEncrypted"
    version = 1
    keyId = "chat-rsa-2026-06"
    encAlg = "RSA-OAEP-SHA1+A256CBC-HS256"
    sigAlg = "RSA-SHA256"
    encryptedKey = [Convert]::ToBase64String($encryptedKey)
    iv = [Convert]::ToBase64String($iv)
    ciphertext = [Convert]::ToBase64String($ciphertext)
    hmac = [Convert]::ToBase64String($macBytes)
    payloadSha256 = $payloadSha256
}

$signRsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new(2048)
$signRsa.FromXmlString($SignPrivateKeyXml)
$canonical = Canonicalize-Envelope $envelope
$signature = $signRsa.SignData([System.Text.Encoding]::UTF8.GetBytes($canonical), [System.Security.Cryptography.CryptoConfig]::MapNameToOID("SHA256"))
$envelope.signature = [Convert]::ToBase64String($signature)

$directory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($directory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$envelope | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host "Encrypted ChatExp catalog written to $OutputPath"
Write-Host "Catalog payload hash: $payloadSha256"
