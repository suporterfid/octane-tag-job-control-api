$strategies = @(
    "JobStrategy0ReadOnlyLogging.cs",
    "JobStrategy1SpeedStrategy.cs",
    "JobStrategy4VerificationCycleStrategy.cs",
    "JobStrategy5EnduranceStrategy.cs",
    "JobStrategy6RobustnessStrategy.cs",
    "JobStrategy7OptimizedStrategy.cs"
)

foreach ($strategy in $strategies) {
    $filePath = "JobStrategies/$strategy"
    $content = Get-Content $filePath -Raw
    
    # Update constructor parameter
    $content = $content -replace 'string hostname, string logFile, ReaderSettings readerSettings\)', 'string hostname, string logFile, Dictionary<string, ReaderSettings> readerSettings)'
    
    # Update base constructor call
    $content = $content -replace ': base\(hostname, logFile, readerSettings\)', ': base(hostname, logFile, readerSettings)'
    
    # Update ConfigureReader method
    $configureReaderPattern = '(?s)protected\s+virtual\s+Settings\s+ConfigureReader\(\)\s*\{.*?\}'
    $newConfigureReader = @'
        protected virtual Settings ConfigureReader()
        {
            var writerSettings = GetSettingsForRole("writer");
            reader.Connect(writerSettings.Hostname);
            reader.ApplyDefaultSettings();

            Settings readerSettings = reader.QueryDefaultSettings();
            readerSettings.Report.IncludeFastId = writerSettings.IncludeFastId;
            readerSettings.Report.IncludePeakRssi = writerSettings.IncludePeakRssi;
            readerSettings.Report.IncludeAntennaPortNumber = writerSettings.IncludeAntennaPortNumber;
            readerSettings.Report.Mode = (ReportMode)Enum.Parse(typeof(ReportMode), writerSettings.ReportMode);
            readerSettings.RfMode = (uint)writerSettings.RfMode;

            readerSettings.Antennas.DisableAll();
            readerSettings.Antennas.GetAntenna(1).IsEnabled = true;
            readerSettings.Antennas.GetAntenna(1).TxPowerInDbm = writerSettings.TxPowerInDbm;
            readerSettings.Antennas.GetAntenna(1).MaxRxSensitivity = writerSettings.MaxRxSensitivity;
            readerSettings.Antennas.GetAntenna(1).RxSensitivityInDbm = writerSettings.RxSensitivityInDbm;

            readerSettings.SearchMode = (SearchMode)Enum.Parse(typeof(SearchMode), writerSettings.SearchMode);
            readerSettings.Session = (ushort)writerSettings.Session;

            readerSettings.Filters.TagFilter1.MemoryBank = (MemoryBank)Enum.Parse(typeof(MemoryBank), writerSettings.MemoryBank);
            readerSettings.Filters.TagFilter1.BitPointer = (ushort)writerSettings.BitPointer;
            readerSettings.Filters.TagFilter1.TagMask = writerSettings.TagMask;
            readerSettings.Filters.TagFilter1.BitCount = writerSettings.BitCount;
            readerSettings.Filters.TagFilter1.FilterOp = (TagFilterOp)Enum.Parse(typeof(TagFilterOp), writerSettings.FilterOp);
            readerSettings.Filters.Mode = (TagFilterMode)Enum.Parse(typeof(TagFilterMode), writerSettings.FilterMode);

            return readerSettings;
        }
'@
    
    $content = $content -replace $configureReaderPattern, $newConfigureReader
    Set-Content $filePath $content
}
