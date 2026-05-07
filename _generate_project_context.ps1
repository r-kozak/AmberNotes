# collect_context.ps1
$outputFile = "PROJECT_CONTEXT.txt"
$excludeFolders = @("bin", "obj", ".git", ".vs", "publish", "TestResults")
$includeExtensions = @(".cs", ".axaml", ".md", ".json", ".csproj", ".slnx")

Remove-Item $outputFile -ErrorAction SilentlyContinue

Write-Host "Збираю контекст проєкту..." -ForegroundColor Cyan

Get-ChildItem -Recurse -File | Where-Object {
    $filePath = $_.FullName
    $ext = [System.IO.Path]::GetExtension($filePath)
    
    # Перевірка: чи не в ігнорованій папці і чи має потрібне розширення
    $shouldInclude = $true
    foreach ($folder in $excludeFolders) {
        if ($filePath -like "*\$folder\*") { $shouldInclude = $false; break }
    }
    
    $shouldInclude -and ($includeExtensions -contains $ext)
} | ForEach-Object {
    $relativeName = Resolve-Path $_.FullName -Relative
    Add-Content $outputFile "`n`n--- FILE: $relativeName ---`n"
    Add-Content $outputFile (Get-Content $_.FullName -Raw)
    Write-Host "Додано: $relativeName"
}

Write-Host "`nГотово! Файл $outputFile створений." -ForegroundColor Green
