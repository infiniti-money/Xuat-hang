param([switch]$Test)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\Hisa.App\Hisa.App.csproj'
$output = Join-Path $PSScriptRoot 'publish'
$localSdk = Join-Path $env:LOCALAPPDATA 'HisaBuild\dotnet\dotnet.exe'
$sdk = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
& $sdk publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Không đóng gói được ứng dụng. Kiểm tra thông báo biên dịch phía trên.' }
$exe = Join-Path $output 'HISA Kho Vat Tu.exe'
Write-Output "Đã tạo: $exe"
if ($Test) {
    $verification = Join-Path $PSScriptRoot 'verification'
    New-Item -ItemType Directory -Path $verification -Force | Out-Null
    $report = Join-Path $verification ('self-test-' + [guid]::NewGuid().ToString('N') + '.json')
    $process = Start-Process -FilePath $exe -ArgumentList @('--self-test','--output',('"' + $report + '"')) -WindowStyle Hidden -PassThru -Wait
    if (!(Test-Path -LiteralPath $report)) { throw 'Tiến trình kiểm tra không tạo kết quả mới; không sử dụng kết quả của bản cũ.' }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or $result.success -ne $true -or $result.checks.Count -eq 0) { throw "Kiểm tra thất bại. Xem: $report" }
    $finalReport = Join-Path $verification 'self-test.json'
    Move-Item -LiteralPath $report -Destination $finalReport -Force
    Get-Content -LiteralPath $finalReport
}
