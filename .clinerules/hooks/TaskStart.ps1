# TaskStart Hook
# PowerShell template for Windows hook execution.

try {
    $rawInput = [Console]::In.ReadToEnd()
    if ($rawInput) {
        $null = $rawInput | ConvertFrom-Json
    }
} catch {
    Write-Error "[TaskStart] Invalid JSON input: $($_.Exception.Message)"
}

# --- ВСТАВЛЯЄМО ЛОГІКУ ЗЧИТУВАННЯ ЖУРНАЛУ ---
$journalPath = "PROJECT_JOURNAL.md"
$journalContent = ""

if (Test-Path $journalPath) {
    # Зчитуємо журнал, щоб передати його як контекст
    $journalContent = Get-Content $journalPath -Raw
    $contextMod = @"

[SYSTEM CONTEXT: PROJECT_JOURNAL.md]
Нижче наведено поточний стан проєкту та дорожня карта з твого журналу. 
Використовуй це як основне джерело істини для розуміння контексту:
$journalContent
"@
} else {
    $contextMod = "Попередження: Файл PROJECT_JOURNAL.md не знайдено. Створи його для кращого відстеження прогресу."
}
# -------------------------------------------

@{
    cancel = $false
    contextModification = $contextMod
    errorMessage = ""
} | ConvertTo-Json -Compress
