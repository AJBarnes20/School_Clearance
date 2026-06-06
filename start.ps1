# Online Clearance - Startup Script
# Double-click start.bat to launch.

$ErrorActionPreference = 'Continue'
$port = 5183

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Online Clearance System" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Detect this PC's LAN IP (interface with default gateway = active network)
$lanIp = $null
try {
    $lanIp = (Get-NetIPConfiguration -ErrorAction SilentlyContinue |
              Where-Object { $_.IPv4DefaultGateway -ne $null -and $_.NetAdapter.Status -eq 'Up' } |
              Select-Object -First 1).IPv4Address.IPAddress
} catch {}

# Step 1: Download cloudflared if not present
$cloudflaredPath = "$PSScriptRoot\cloudflared.exe"
if (-not (Test-Path $cloudflaredPath)) {
    Write-Host "Downloading tunnel tool (one-time, ~20 MB)..." -ForegroundColor Yellow
    try {
        Invoke-WebRequest -Uri "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe" `
            -OutFile $cloudflaredPath -UseBasicParsing
        Write-Host "Downloaded." -ForegroundColor Green
    } catch {
        Write-Host "Failed to download cloudflared: $_" -ForegroundColor Red
        Write-Host "Check your internet connection and try again." -ForegroundColor Red
        pause
        exit
    }
}

# Step 2: Start public tunnel
Write-Host "Opening public tunnel..." -ForegroundColor Yellow
$tempLog = "$env:TEMP\cf_tunnel.txt"
if (Test-Path $tempLog) { Remove-Item $tempLog -Force }

$tunnelProc = Start-Process -FilePath $cloudflaredPath `
    -ArgumentList "tunnel --url http://localhost:$port" `
    -RedirectStandardError $tempLog `
    -NoNewWindow -PassThru

# Step 3: Wait for public URL
$publicUrl = ""
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 1
    if (Test-Path $tempLog) {
        $content = Get-Content $tempLog -Raw -ErrorAction SilentlyContinue
        if ($content -match 'https://[\w\-]+\.trycloudflare\.com') {
            $publicUrl = $Matches[0].Trim()
            break
        }
    }
}

# Step 4: Save public URL into appsettings.json (used by QR code on login page)
$settingsPath = "$PSScriptRoot\appsettings.json"
if ($publicUrl) {
    try {
        $json = Get-Content $settingsPath -Raw | ConvertFrom-Json
        $json.PublicUrl = $publicUrl
        $json | ConvertTo-Json -Depth 10 | Set-Content $settingsPath -Encoding UTF8
    } catch {
        Write-Host "Could not update appsettings.json: $_" -ForegroundColor Red
    }
}

# Step 5: Show all URLs clearly
Write-Host ""
Write-Host "+---------------------------------------------------------+" -ForegroundColor Green
Write-Host "|  OPEN ANY OF THESE IN YOUR BROWSER:                     |" -ForegroundColor Green
Write-Host "+---------------------------------------------------------+" -ForegroundColor Green
Write-Host ""
Write-Host "  This PC          : " -NoNewline; Write-Host "http://localhost:$port" -ForegroundColor White
if ($lanIp) {
    Write-Host "  Same WiFi/LAN    : " -NoNewline; Write-Host "http://${lanIp}:$port" -ForegroundColor White
}
if ($publicUrl) {
    Write-Host "  Anywhere (phone) : " -NoNewline; Write-Host "$publicUrl" -ForegroundColor White
    Write-Host ""
    Write-Host "  (QR code on login page is updated automatically)" -ForegroundColor Gray
} else {
    Write-Host "  Public URL not ready - tunnel failed. Only local URLs work." -ForegroundColor Yellow
}
Write-Host ""
Write-Host "  Browser will open automatically..." -ForegroundColor Gray
Write-Host "  Press Ctrl+C in this window to stop the server." -ForegroundColor Gray
Write-Host ""

# Step 6: Auto-open browser once the app is actually listening on the port
Start-Job -ScriptBlock {
    param($url, $portNum)
    for ($i = 0; $i -lt 90; $i++) {
        Start-Sleep -Seconds 1
        try {
            $tcp = New-Object System.Net.Sockets.TcpClient
            $tcp.Connect('localhost', $portNum)
            $tcp.Close()
            Start-Sleep -Seconds 1
            Start-Process $url
            return
        } catch {}
    }
} -ArgumentList "http://localhost:$port", $port | Out-Null

# Step 7: Run the app in WATCH mode (auto-reload on file changes)
Set-Location $PSScriptRoot
dotnet watch --launch-profile http
