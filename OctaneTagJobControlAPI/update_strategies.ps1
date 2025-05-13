$files = Get-ChildItem -Path "JobStrategies" -Filter "JobStrategy*.cs"
foreach ($file in $files) {
    if ($file.Name -ne "JobStrategy8DualReaderEnduranceStrategy.cs") {
        $content = Get-Content $file.FullName -Raw
        $newContent = $content -replace "ReaderSettings readerSettings\) : base\(hostname, logFile, readerSettings\)", "Dictionary<string, ReaderSettings> readerSettings) : base(hostname, logFile, readerSettings)"
        Set-Content -Path $file.FullName -Value $newContent
    }
}
