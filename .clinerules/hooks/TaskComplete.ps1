# TaskComplete Hook
# PowerShell template for Windows hook execution.

try {
    $rawInput = [Console]::In.ReadToEnd()
    if ($rawInput) {
        $null = $rawInput | ConvertFrom-Json
    }
} catch {
    Write-Error "[TaskComplete] Invalid JSON input: $($_.Exception.Message)"
}

# --- ВСТАВЛЯЄМО СЮДИ ---
Write-Host "[Cline] Запускаю перевірочну збірку проєкту..." -ForegroundColor Yellow
dotnet build AmberNotes.Desktop /p:Configuration=Debug
$buildResult = $LASTEXITCODE
# -----------------------

$errorMsg = ""
if ($buildResult -ne 0) {
    $errorMsg = "Збірка AmberNotes.Desktop завершилася з помилкою. Перевір код перед завершенням таску."
}

@{
    cancel = $false
    contextModification = ""
    errorMessage = $errorMsg
} | ConvertTo-Json -Compress
