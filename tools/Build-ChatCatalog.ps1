param(
    [string]$OutputPath = "",
    [string]$SignPrivateKeyPath = "",
    [string]$SignPrivateKeyXml = "",
    [string]$CatalogVersion = "2026.06.22",
    [string]$StickerDirectory = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "TestMods\ChatExp\SharedResources\Chat\catalog.auraenc"
}

$catalogDirectory = Split-Path -Parent $OutputPath
if ([string]::IsNullOrWhiteSpace($catalogDirectory)) {
    $catalogDirectory = (Get-Location).Path
}

if ([string]::IsNullOrWhiteSpace($StickerDirectory)) {
    $StickerDirectory = Join-Path $catalogDirectory "Stickers"
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

function Test-IsInsideDirectory {
    param(
        [string]$Path,
        [string]$Directory
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $fullDirectory = [System.IO.Path]::GetFullPath($Directory).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    return $fullPath.StartsWith($fullDirectory + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function ConvertTo-RelativeResourcePath {
    param(
        [string]$Path,
        [string]$Directory
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullDirectory = [System.IO.Path]::GetFullPath($Directory)
    if (-not (Test-IsInsideDirectory -Path $fullPath -Directory $fullDirectory)) {
        throw "Sticker resource must stay under the catalog directory. path=$fullPath directory=$fullDirectory"
    }

    $baseUri = [Uri]($fullDirectory.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar)
    $fileUri = [Uri]$fullPath
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($fileUri).ToString()).Replace('\', '/')
}

function New-StickerId {
    param(
        [int]$Index,
        [string]$Name
    )

    $stem = [System.IO.Path]::GetFileNameWithoutExtension($Name).ToLowerInvariant()
    $id = [regex]::Replace($stem, '[^a-z0-9_.-]+', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($id)) {
        return "sticker_{0:D3}" -f $Index
    }

    return $id
}

function Get-StickerEntries {
    param(
        [string]$Directory,
        [string]$CatalogDirectory
    )

    if (-not (Test-Path -LiteralPath $Directory)) {
        return @()
    }

    $files = @(Get-ChildItem -LiteralPath $Directory -File |
        Where-Object { $_.Extension -match '^\.(png|jpg|jpeg|webp)$' } |
        Sort-Object Name)

    $entries = @()
    $index = 1
    foreach ($file in $files) {
        $id = New-StickerId -Index $index -Name $file.Name
        while ($entries | Where-Object { $_.id -eq $id }) {
            $id = "sticker_{0:D3}" -f $index
            $index++
        }

        $entries += [ordered]@{
            id = $id
            packId = "initial"
            stickerId = $id
            resourcePath = ConvertTo-RelativeResourcePath -Path $file.FullName -Directory $CatalogDirectory
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash.ToLowerInvariant()
            order = 1000 + ($index * 10)
        }
        $index++
    }

    return $entries
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

$stickers = @(Get-StickerEntries -Directory $StickerDirectory -CatalogDirectory $catalogDirectory)

$catalog = [ordered]@{
    schemaVersion = 1
    catalogId = "chat_catalog_v1"
    catalogVersion = $CatalogVersion
    messages = @(
        [ordered]@{ id = "bye"; text = "$([char]0x518D)$([char]0x89C1)$([char]0xFF01)"; order = 10 },
        [ordered]@{ id = "thanks"; text = "$([char]0x8C22)$([char]0x8C22)$([char]0xFF01)"; order = 20 },
        [ordered]@{ id = "hello"; text = "$([char]0x4F60)$([char]0x597D)$([char]0xFF01)"; order = 30 },
        [ordered]@{ id = "guard"; text = "$([char]0x6765)$([char]0x4EBA)$([char]0xFF0C)$([char]0x62A4)$([char]0x9A7E)$([char]0xFF01)"; order = 40 },
        [ordered]@{ id = "watch_next_time"; text = "$([char]0x4E0B)$([char]0x6B21)$([char]0x6CE8)$([char]0x610F)$([char]0x70B9)~"; order = 50 },
        [ordered]@{ id = "dare_to_fight"; text = "$([char]0x5C14)$([char]0x7B49)$([char]0x6562)$([char]0x5E94)$([char]0x6218)$([char]0x5426)$([char]0xFF1F)"; order = 60 },
        [ordered]@{ id = "wait_think"; text = "$([char]0x4E14)$([char]0x6162)$([char]0xFF01)$([char]0x5BB9)$([char]0x6211)$([char]0x4E09)$([char]0x601D)..."; order = 70 },
        [ordered]@{ id = "watch_me"; text = "$([char]0x6C5D)$([char]0x7B49)$([char]0x7ED9)$([char]0x6211)$([char]0x770B)$([char]0x597D)$([char]0x4E86)$([char]0xFF01)"; order = 80 },
        [ordered]@{ id = "raise_army"; text = "$([char]0x65F6)$([char]0x673A)$([char]0x5DF2)$([char]0x5230)$([char]0xFF0C)$([char]0x5373)$([char]0x523B)$([char]0x8D77)$([char]0x5175)$([char]0xFF01)"; order = 90 },
        [ordered]@{ id = "fight_to_death"; text = "$([char]0x552F)$([char]0x6709)$([char]0x6B7B)$([char]0x6218)$([char]0xFF0C)$([char]0x5B89)$([char]0x80FD)$([char]0x8A00)$([char]0x964D)$([char]0xFF1F)"; order = 100 }
    )
    stickers = $stickers
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
