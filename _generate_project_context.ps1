# collect_context.ps1
# Вказуємо шлях до файлу в батьківській папці
$outputFile = "..\PROJECT_CONTEXT.txt"
$excludeFolders = @("bin", "obj", ".git", ".vs", "publish", "TestResults")
$includeExtensions = @(".cs", ".axaml", ".md", ".json", ".csproj", ".slnx")
$excludeFiles = @("oauth.config.json") # Додано список для файлів-виключень

# Видаляємо старий файл, якщо він був (в батьківській папці)
Remove-Item $outputFile -ErrorAction SilentlyContinue

Write-Host "Збираю контекст проєкту у батьківську папку..." -ForegroundColor Cyan

Get-ChildItem -Recurse -File | Where-Object {
    $filePath = $_.FullName
    $fileName = $_.Name
    $ext = [System.IO.Path]::GetExtension($filePath)
    
    # Перевірка: чи не в ігнорованій папці
    $shouldInclude = $true
    foreach ($folder in $excludeFolders) {
        if ($filePath -like "*\$folder\*") { $shouldInclude = $false; break }
    }
    
    # Перевірка: чи не знаходиться файл у списку виключень
    if ($excludeFiles -contains $fileName) {
        $shouldInclude = $false
    }
    
    $shouldInclude -and ($includeExtensions -contains $ext)
} | ForEach-Object {
    # Отримуємо відносний шлях від поточної папки
    $relativeName = Resolve-Path $_.FullName -Relative
    Add-Content $outputFile "`n`n--- FILE: $relativeName ---`n"
    Add-Content $outputFile (Get-Content $_.FullName -Raw)
    Write-Host "Додано: $relativeName"
}

Write-Host "`nГотово! Файл створений за шляхом: $(Resolve-Path $outputFile)" -ForegroundColor Green