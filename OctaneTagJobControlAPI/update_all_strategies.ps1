$strategies = @(
    "JobStrategy0ReadOnlyLogging.cs",
    "JobStrategy1SpeedStrategy.cs",
    "JobStrategy3BatchSerializationPermalockStrategy.cs",
    "JobStrategy4VerificationCycleStrategy.cs",
    "JobStrategy5EnduranceStrategy.cs",
    "JobStrategy6RobustnessStrategy.cs",
    "JobStrategy7OptimizedStrategy.cs"
)

foreach ($strategy in $strategies) {
    $filePath = "JobStrategies/$strategy"
    $content = Get-Content $filePath -Raw
    
    # Update constructor parameter
    $content = $content -replace 'ReaderSettings readerSettings\) : base\(hostname, logFile, readerSettings\)', 'Dictionary<string, ReaderSettings> readerSettings) : base(hostname, logFile, readerSettings)'
    
    # Update ConfigureReader method if it exists
    $content = $content -replace 'reader\.Connect\(settings\.Hostname\)', 'reader.Connect(GetSettingsForRole("writer").Hostname)'
    $content = $content -replace 'settings\.IncludeFastId', 'GetSettingsForRole("writer").IncludeFastId'
    $content = $content -replace 'settings\.IncludePeakRssi', 'GetSettingsForRole("writer").IncludePeakRssi'
    $content = $content -replace 'settings\.IncludeAntennaPortNumber', 'GetSettingsForRole("writer").IncludeAntennaPortNumber'
    $content = $content -replace 'settings\.ReportMode', 'GetSettingsForRole("writer").ReportMode'
    $content = $content -replace 'settings\.RfMode', 'GetSettingsForRole("writer").RfMode'
    $content = $content -replace 'settings\.AntennaPort', 'GetSettingsForRole("writer").AntennaPort'
    $content = $content -replace 'settings\.TxPowerInDbm', 'GetSettingsForRole("writer").TxPowerInDbm'
    $content = $content -replace 'settings\.MaxRxSensitivity', 'GetSettingsForRole("writer").MaxRxSensitivity'
    $content = $content -replace 'settings\.RxSensitivityInDbm', 'GetSettingsForRole("writer").RxSensitivityInDbm'
    $content = $content -replace 'settings\.SearchMode', 'GetSettingsForRole("writer").SearchMode'
    $content = $content -replace 'settings\.Session', 'GetSettingsForRole("writer").Session'
    $content = $content -replace 'settings\.MemoryBank', 'GetSettingsForRole("writer").MemoryBank'
    $content = $content -replace 'settings\.BitPointer', 'GetSettingsForRole("writer").BitPointer'
    $content = $content -replace 'settings\.TagMask', 'GetSettingsForRole("writer").TagMask'
    $content = $content -replace 'settings\.BitCount', 'GetSettingsForRole("writer").BitCount'
    $content = $content -replace 'settings\.FilterOp', 'GetSettingsForRole("writer").FilterOp'
    $content = $content -replace 'settings\.FilterMode', 'GetSettingsForRole("writer").FilterMode'
    
    Set-Content $filePath $content
}
