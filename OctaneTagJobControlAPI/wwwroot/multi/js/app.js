function jobControl() {
    return {
        activeTab: 'configure',
        availableJobs: [],
        selectedJob: '',
        activeJob: null,
        tagData: [],
        logEntries: [],
        showToast: false,
        toastMessage: '',
        refreshInterval: null,
        
        config: {
            name: 'SGTIN_Encoding_Test',
            hasDetector: true,
            hasWriter: true,
            hasVerifier: true,
            detector: {
                hostname: '192.168.68.248',
                txPowerInDbm: 18,
                antennaPort: 1,
                enableGpiTrigger: false,
                gpiPort: 1,
                readerID: 'Detector-01'
            },
            writer: {
                hostname: '192.168.1.100',
                txPowerInDbm: 33,
                antennaPort: 1,
                enableLock: true,
                enablePermalock: false,
                readerID: 'Writer-01',
                searchMode: 'DualTarget'
            },
            verifier: {
                hostname: '192.168.68.93',
                txPowerInDbm: 33,
                antennaPort: 1,
                enableGpiTrigger: true,
                gpiPort: 1,
                gpiTriggerState: 'true',
                enableGpoOutput: true,
                gpoPort: 1,
                gpoVerificationTimeoutMs: 1000,
                readerID: 'Verifier-01'
            },
            parameters: {
                encodingMethod: 'SGTIN96',
                epcHeader: '30',
                sku: '7891033079360',
                partitionValue: 6,
                itemReference: 0,
                maxCycles: 10000,
                enableLock: true,
                enablePermalock: false
            }
        },
        
        // Helper function to safely access properties of potentially null objects
        safeGet(obj, path, defaultValue = '') {
            try {
                if (!obj) return defaultValue;
                
                const parts = path.split('.');
                let current = obj;
                
                for (const part of parts) {
                    if (current === null || current === undefined) {
                        return defaultValue;
                    }
                    current = current[part];
                }
                
                return current === null || current === undefined ? defaultValue : current;
            } catch (e) {
                console.error('Error in safeGet:', e);
                return defaultValue;
            }
        },
        
        // Helper functions for accessing job properties safely
        getJobId() {
            return this.safeGet(this.activeJob, 'jobId', '');
        },
        
        getJobState() {
            return this.safeGet(this.activeJob, 'state', '');
        },
        
        getJobName() {
            return this.safeGet(this.activeJob, 'jobName', '');
        },
        
        getJobOperation() {
            return this.safeGet(this.activeJob, 'currentOperation', '');
        },
        
        getJobProgress() {
            return this.safeGet(this.activeJob, 'progressPercentage', 0);
        },
        
        getJobTagsProcessed() {
            return this.safeGet(this.activeJob, 'totalTagsProcessed', 0);
        },
        
        getJobSuccessCount() {
            return this.safeGet(this.activeJob, 'successCount', 0);
        },
        
        getJobFailureCount() {
            return this.safeGet(this.activeJob, 'failureCount', 0);
        },
        
        getMetric(name, defaultValue = 0) {
            return this.safeGet(this.activeJob, `metrics.${name}`, defaultValue);
        },
        
        isJobRunning() {
            return this.getJobState() === 'Running';
        },
        
        init() {
            this.loadJobs();
            
            // Set up a refresh interval for the active job
            this.refreshInterval = setInterval(() => {
                if (this.activeTab === 'monitor' && this.selectedJob) {
                    this.refreshJobStatus();
                }
            }, 3000);
        },
        
        loadJobs() {
            fetch('../api/job')
                .then(response => response.json())
                .then(data => {
                    this.availableJobs = data || [];
                })
                .catch(error => {
                    console.error('Error loading jobs:', error);
                    this.showToastMessage('Error loading jobs. Please try again.');
                });
        },
        
        createJob() {
            // Build the request payload
            const payload = {
                name: this.config.name,
                strategyType: 'MultiReaderEnduranceStrategy',
                readerSettings: {
                    detector: this.config.hasDetector ? {
                        hostname: this.config.detector.hostname,
                        txPowerInDbm: parseInt(this.config.detector.txPowerInDbm),
                        antennaPort: parseInt(this.config.detector.antennaPort),
                        includeAntennaPortNumber: true,
                        includeFastId: true,
                        includePeakRssi: true,
                        parameters: {
                            enableGpiTrigger: this.config.detector.enableGpiTrigger.toString(),
                            gpiPort: this.config.detector.gpiPort.toString(),
                            ReaderID: this.config.detector.readerID
                        }
                    } : null,
                    writer: this.config.hasWriter ? {
                        hostname: this.config.writer.hostname,
                        txPowerInDbm: parseInt(this.config.writer.txPowerInDbm),
                        antennaPort: parseInt(this.config.writer.antennaPort),
                        includeAntennaPortNumber: true,
                        includeFastId: true,
                        includePeakRssi: true,
                        searchMode: this.config.writer.searchMode,
                        parameters: {
                            enableLock: this.config.writer.enableLock.toString(),
                            enablePermalock: this.config.writer.enablePermalock.toString(),
                            ReaderID: this.config.writer.readerID
                        }
                    } : null,
                    verifier: this.config.hasVerifier ? {
                        hostname: this.config.verifier.hostname,
                        txPowerInDbm: parseInt(this.config.verifier.txPowerInDbm),
                        antennaPort: parseInt(this.config.verifier.antennaPort),
                        includeAntennaPortNumber: true,
                        includeFastId: true,
                        includePeakRssi: true,
                        parameters: {
                            enableGpiTrigger: this.config.verifier.enableGpiTrigger.toString(),
                            gpiPort: this.config.verifier.gpiPort.toString(),
                            gpiTriggerState: this.config.verifier.gpiTriggerState,
                            enableGpoOutput: this.config.verifier.enableGpoOutput.toString(),
                            gpoPort: this.config.verifier.gpoPort.toString(),
                            gpoVerificationTimeoutMs: this.config.verifier.gpoVerificationTimeoutMs.toString(),
                            ReaderID: this.config.verifier.readerID
                        }
                    } : null
                },
                parameters: {
                    epcHeader: this.config.parameters.epcHeader,
                    sku: this.config.parameters.sku,
                    encodingMethod: this.config.parameters.encodingMethod,
                    partitionValue: this.config.parameters.partitionValue.toString(),
                    itemReference: this.config.parameters.itemReference.toString(),
                    enableLock: this.config.parameters.enableLock.toString(),
                    enablePermalock: this.config.parameters.enablePermalock.toString(),
                    maxCycles: this.config.parameters.maxCycles.toString()
                }
            };
            
            // Validate at least one reader is selected
            if (!this.config.hasDetector && !this.config.hasWriter && !this.config.hasVerifier) {
                this.showToastMessage('At least one reader must be configured');
                return;
            }
            
            // Send the request
            fetch('../api/job', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(payload)
            })
            .then(response => {
                if (!response.ok) {
                    throw new Error(`Failed to create job: ${response.statusText}`);
                }
                return response.json();
            })
            .then(data => {
                this.showToastMessage('Job created successfully!');
                this.loadJobs();
                if (data && data.jobId) {
                    this.selectedJob = data.jobId;
                    this.activeTab = 'monitor';
                    this.refreshJobStatus();
                }
            })
            .catch(error => {
                console.error('Error creating job:', error);
                this.showToastMessage(`Error creating job: ${error.message}`);
            });
        },
        
        refreshJobStatus() {
            const jobId = this.selectedJob;
            if (!jobId) return;
            
            // Clear old values to avoid displaying stale data
            if (this.activeJob && jobId !== this.getJobId()) {
                this.activeJob = null;
                this.tagData = [];
            }
            
            // Get job status
            fetch(`../api/job/${jobId}`)
                .then(response => {
                    if (!response.ok) {
                        throw new Error(`Failed to get job status: ${response.statusText}`);
                    }
                    return response.json();
                })
                .then(data => {
                    // Check if data is valid
                    if (!data) {
                        this.activeJob = null;
                        return;
                    }
                    
                    this.activeJob = data;
                    
                    // Only fetch tag data if active job is running
                    if (this.isJobRunning()) {
                        this.fetchTagData(jobId);
                    }
                })
                .catch(error => {
                    console.error('Error fetching job status:', error);
                    // Don't show toast for auto-refresh errors
                });
        },
        
        fetchTagData(jobId) {
            if (!jobId) return;
            
            fetch(`../api/job/${jobId}/tags?pageSize=20&sortBy=timestamp&descending=true`)
                .then(response => {
                    if (!response.ok) {
                        throw new Error(`Failed to get tag data: ${response.statusText}`);
                    }
                    return response.json();
                })
                .then(data => {
                    if (data && data.data && data.data.tags) {
                        this.tagData = data.data.tags.map(tag => ({
                            tid: tag.TID || '',
                            epc: tag.EPC || '',
                            expectedEpc: tag.AdditionalData?.ExpectedEPC || null,
                            rssi: tag.RSSI || 0,
                            antennaPort: tag.AntennaPort || 0,
                            success: tag.AdditionalData?.VerificationSuccess === 'true'
                        }));
                    } else {
                        this.tagData = [];
                    }
                })
                .catch(error => {
                    console.error('Error fetching tag data:', error);
                    this.tagData = [];
                });
        },
        
        startJob() {
            const jobId = this.selectedJob;
            if (!jobId) return;
            
            // Check for active jobs first
            fetch('../api/job/active')
                .then(response => {
                    // 404 means no active job, which is good
                    if (response.status === 404) {
                        return this.doStartJob(jobId);
                    }
                    // Otherwise we have an active job
                    return response.json().then(data => {
                        if (data && data.data && data.data.jobId !== jobId) {
                            this.showToastMessage('Another job is already running. Please stop it first.');
                            return null;
                        }
                        return this.doStartJob(jobId);
                    });
                })
                .catch(error => {
                    console.error('Error checking active jobs:', error);
                    this.showToastMessage(`Error checking active jobs: ${error.message}`);
                });
        },
        
        doStartJob(jobId) {
            if (!jobId) return null;
            
            fetch(`../api/job/${jobId}/start`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ timeoutSeconds: 3600 }) // 1 hour timeout
            })
            .then(response => {
                if (!response.ok) {
                    throw new Error(`Failed to start job: ${response.statusText}`);
                }
                return response.json();
            })
            .then(data => {
                if (data && data.success) {
                    this.showToastMessage('Job started successfully!');
                    this.refreshJobStatus();
                } else {
                    this.showToastMessage(`Error starting job: ${data?.message || 'Unknown error'}`);
                }
                return data;
            })
            .catch(error => {
                console.error('Error starting job:', error);
                this.showToastMessage(`Error starting job: ${error.message}`);
                return null;
            });
        },
        
        stopJob() {
            const jobId = this.selectedJob;
            if (!jobId) return;
            
            fetch(`../api/job/${jobId}/stop`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                }
            })
            .then(response => {
                if (!response.ok) {
                    throw new Error(`Failed to stop job: ${response.statusText}`);
                }
                return response.json();
            })
            .then(data => {
                if (data && data.success) {
                    this.showToastMessage('Job stopped successfully!');
                    this.refreshJobStatus();
                } else {
                    this.showToastMessage(`Error stopping job: ${data?.message || 'Unknown error'}`);
                }
            })
            .catch(error => {
                console.error('Error stopping job:', error);
                this.showToastMessage(`Error stopping job: ${error.message}`);
            });
        },
        
        refreshLogs() {
            if (!this.selectedJob) return;
            
            fetch(`../api/job/${this.selectedJob}/logs`)
                .then(response => {
                    if (!response.ok) {
                        throw new Error(`Failed to get logs: ${response.statusText}`);
                    }
                    return response.json();
                })
                .then(data => {
                    if (data && data.logEntries) {
                        this.logEntries = data.logEntries;
                    } else {
                        this.logEntries = [];
                    }
                })
                .catch(error => {
                    console.error('Error fetching logs:', error);
                    this.showToastMessage(`Error fetching logs: ${error.message}`);
                    this.logEntries = [];
                });
        },
        
        showToastMessage(message) {
            this.toastMessage = message || 'An error occurred';
            this.showToast = true;
            setTimeout(() => {
                this.showToast = false;
            }, 4000);
        },
        
        formatRunTime(seconds) {
            if (!seconds) return '0:00:00';
            const hours = Math.floor(seconds / 3600);
            const minutes = Math.floor((seconds % 3600) / 60);
            const secs = Math.floor(seconds % 60);
            return `${hours}:${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
        }
    };
}