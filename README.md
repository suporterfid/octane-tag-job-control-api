# RFID Job Control API

A RESTful API service for managing and controlling RFID job operations. This API allows you to configure, start, monitor, and stop RFID tag reading and writing jobs remotely.

## Features

- REST API for remote job control
- Multiple job strategies for different RFID operations:
  - **BatchSerializationStrategy**: Batch processing of tag serialization
  - **CheckBoxStrategy**: Single-reader tag verification and encoding
  - **MultiReaderEnduranceStrategy**: Multi-reader testing with optional locking/permalocking
  - **ReadOnlyLoggingStrategy**: Non-invasive tag monitoring
  - **SpeedTestStrategy**: Performance testing of tag operations
  - **ImpinjR700CapStrategy**: Impinj R700 CAP application support
- Real-time job status monitoring
- Centralized configuration management
- Detailed job metrics and logs
- Tag operation tracking with TID/EPC history
- Job persistence through file-based storage
- Circular log buffer for efficient log management
- Swagger UI for API documentation and testing
- Support for multiple reader configurations
- Extensive job strategy customization options
- EPC list generation utilities
- SGTIN-96 encoding support

## Architecture

The API is built with ASP.NET Core and follows a clean architecture pattern:

- **Controllers**: Handle HTTP requests and responses
- **Services**: Implement business logic and job management
- **Models**: Define data structures for API operations
- **Background Services**: Manage long-running operations
- **Job Strategies**: Implement specific RFID operation patterns
- **Storage Services**: Handle data persistence and logging
- **Repositories**: Provide abstraction for data access
- **Utilities**: Provide helper functions and EPC generation tools

### Storage System

The API uses a file-based storage system with the following components:

- **FileStorageService**: Implements the `IStorageService` interface for persisting data to JSON files
- **CircularLogBuffer**: Provides efficient log rotation and management
- **Repositories**: Abstract data access through `JobRepository` and `ConfigurationRepository`

### Job Management

Jobs are managed through a `JobManager` service that:

- Registers and tracks job status
- Starts and stops jobs
- Monitors job progress
- Collects metrics and logs
- Manages job cleanup

### Strategy System

The API uses a flexible strategy pattern for job execution:

- **IJobStrategy**: Core interface for all job strategies
- **JobStrategyBase**: Base class with common functionality
- **SingleReaderStrategyBase**: For strategies using one RFID reader
- **MultiReaderStrategyBase**: For strategies using multiple RFID readers
- **StrategyFactory**: Creates strategy instances based on configuration

## Getting Started

### Prerequisites

- .NET 8.0 SDK or Docker
- Existing RFID reader infrastructure
- Compatible Impinj RFID readers

### Building and Running the API

#### Using .NET CLI

```bash
# Clone the repository
git clone https://github.com/suporterfid/octane-tag-job-control-api
cd octane-tag-job-control-api

# Build the solution
dotnet build

# Run the API
cd OctaneTagJobControlAPI
dotnet run
```

#### Using Docker

```bash
# Build the Docker image
docker build -t octane-tag-job-control-api .

# Run the container
docker run -d -p 5000:5000 --name rfid-api octane-tag-job-control-api
```

### API Access

Once running, the API is available at:

- API Endpoint: `http://localhost:5000/api`
- Swagger UI: `http://localhost:5000/swagger`

## API Endpoints

### Jobs

- `GET /api/job` - Get all jobs (with optional sorting)
- `GET /api/job/{jobId}` - Get a specific job
- `POST /api/job` - Create a new job
- `POST /api/job/{jobId}/start` - Start a job
- `POST /api/job/{jobId}/stop` - Stop a job
- `GET /api/job/{jobId}/metrics` - Get job metrics
- `GET /api/job/{jobId}/logs` - Get job logs
- `GET /api/job/{jobId}/tags` - Get tag data for a job
- `GET /api/job/strategies` - Get available job strategies

### Configurations

- `GET /api/configuration` - Get all configurations
- `GET /api/configuration/{id}` - Get a specific configuration
- `POST /api/configuration` - Create a new configuration
- `PUT /api/configuration/{id}` - Update a configuration
- `DELETE /api/configuration/{id}` - Delete a configuration

### System Status

- `GET /api/status` - Get system status
- `GET /api/status/version` - Get API version
- `GET /api/status/readers` - Get connected readers
- `GET /api/status/metrics` - Get system metrics
- `GET /api/status/health` - Get system health status
- `GET /api/status/logs` - Get system logs
- `GET /api/status/files` - Browse files in the system

## Job Strategies

### ReadOnlyLoggingStrategy
Non-invasive monitoring strategy that reads and logs tag information without modification. Ideal for inventory and monitoring applications.

### BatchSerializationStrategy
Handles batch processing of tag serialization operations. Supports configurable batch sizes and serialization patterns.

### CheckBoxStrategy
Single-reader strategy for tag verification and encoding. Reads tags during a configurable period, confirms tag count, and writes new EPCs based on selected encoding method. The strategy is controlled by GPI events and supports multiple EPC encoding methods.

#### Features
- GPI-triggered operation flow (Port 1)
- Multiple EPC encoding methods:
  - BasicWithTidSuffix: Combines header + SKU + TID suffix
  - SGTIN-96: GS1 SGTIN-96 format
  - CustomFormat: Reserved for future custom encoding
- Automatic tag verification after writing
- Detailed logging of tag operations
- GPO status indication
- Configurable read/write timeouts

#### Configuration Options

The strategy supports the following configuration parameters:
- `epcHeader`: EPC header value (e.g., "E7")
- `sku`: SKU or GS1 company prefix (12 digits for BasicWithTidSuffix, 13+ for SGTIN-96)
- `encodingMethod`: EPC encoding method ("BasicWithTidSuffix", "SGTIN96", or "CustomFormat")
- `partitionValue`: SGTIN-96 partition value (0-6, defaults to 6)
- `itemReference`: SGTIN-96 item reference value
- GPI/GPO settings for operation control and status indication

#### Example Configurations

1. **Basic Configuration with TID Suffix**
```bash
curl -X POST "http://localhost:5000/api/job" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "BasicCheckBoxTest",
    "strategyType": "CheckBoxStrategy",
    "readerSettings": {
      "writer": {
        "hostname": "192.168.1.100",
        "txPowerInDbm": 30,
        "parameters": {
          "enableGpiTrigger": "true",
          "gpiPort": "1"
        }
      }
    },
    "parameters": {
      "epcHeader": "E7",
      "sku": "012345678901",
      "encodingMethod": "BasicWithTidSuffix"
    }
  }'
```

Expected response:
```json
{
  "jobId": "job123",
  "status": "Created",
  "metrics": {
    "encodingMethod": "BasicWithTidSuffix",
    "collectedTags": 0,
    "verifiedTags": 0,
    "sku": "012345678901",
    "epcHeader": "E7"
  }
}
```

2. **SGTIN-96 Configuration with Verification**
```bash
curl -X POST "http://localhost:5000/api/job" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Sgtin96CheckBoxTest",
    "strategyType": "CheckBoxStrategy",
    "readerSettings": {
      "writer": {
        "hostname": "192.168.1.100",
        "txPowerInDbm": 30,
        "parameters": {
          "enableGpiTrigger": "true",
          "gpiPort": "1",
          "enableGpoOutput": "true",
          "gpoPort": "1"
        }
      }
    },
    "parameters": {
      "sku": "0123456789012",
      "encodingMethod": "SGTIN96",
      "partitionValue": "6",
      "itemReference": "0"
    }
  }'
```

Expected response:
```json
{
  "jobId": "job124",
  "status": "Created",
  "metrics": {
    "encodingMethod": "SGTIN96",
    "collectedTags": 0,
    "verifiedTags": 0,
    "sku": "0123456789012",
    "partitionValue": 6
  }
}
```

### MultiReaderEnduranceStrategy
Multi-reader strategy for endurance testing that can utilize up to three readers (detector, writer, and verifier). Uses separate readers for reading, writing and verifying operations, supporting continuous testing scenarios. Features:
- Optional tag locking/permalocking
- Cycle count tracking
- Performance metrics for write, verify, and lock operations
- Automatic retry on verification failure
- GPI/GPO support for tag presence detection and status indication
- Flexible reader role combinations
- SGTIN-96 encoding support

#### Configuration Options

The strategy supports various reader role combinations:
- Full configuration (detector + writer + verifier)
- Writer-only configuration
- Writer-verifier configuration
- Verifier-only configuration with GPI settings

Each reader can be configured with:
- Hostname/IP and power settings
- GPI trigger settings for tag detection
- GPO settings for operation status indication
- Lock/permalock options (for writer)
- Verification timeouts and retry settings

#### SGTIN-96 Encoding with Custom GTIN

Here's an example configuration for using the MultiReaderEnduranceStrategy with three readers and SGTIN-96 encoding for a specific GTIN (7891033079360):

```bash
curl -X POST "http://localhost:5000/api/job" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "SGTIN_Encoding_Test",
    "strategyType": "MultiReaderEnduranceStrategy",
    "readerSettings": {
      "detector": {
        "hostname": "192.168.68.248",
        "txPowerInDbm": 18,
        "antennaPort": 1,
        "includeAntennaPortNumber": true,
        "includeFastId": true,
        "includePeakRssi": true,
        "parameters": {
          "enableGpiTrigger": "false",
          "ReaderID": "Detector-01"
        }
      },
      "writer": {
        "hostname": "192.168.1.100",
        "txPowerInDbm": 33,
        "antennaPort": 1,
        "includeAntennaPortNumber": true,
        "includeFastId": true,
        "includePeakRssi": true,
        "searchMode": "DualTarget",
        "parameters": {
          "enableLock": "true",
          "enablePermalock": "false",
          "ReaderID": "Writer-01"
        }
      },
      "verifier": {
        "hostname": "192.168.68.93",
        "txPowerInDbm": 33,
        "antennaPort": 1,
        "includeAntennaPortNumber": true,
        "includeFastId": true,
        "includePeakRssi": true,
        "parameters": {
          "enableGpiTrigger": "true",
          "gpiPort": "1",
          "gpiTriggerState": "true",
          "enableGpoOutput": "true",
          "gpoPort": "1",
          "gpoVerificationTimeoutMs": "1000",
          "ReaderID": "Verifier-01"
        }
      }
    },
    "parameters": {
      "sku": "7891033079360",
      "epcHeader": "30",
      "encodingMethod": "SGTIN96",
      "partitionValue": "6",
      "itemReference": "0",
      "enableLock": "true",
      "enablePermalock": "false",
      "maxCycles": "10000"
    }
  }'
```

This configuration will:
1. Use the GTIN "7891033079360" for SGTIN-96 encoding
2. Apply the partition value 6 to determine bit allocations
3. Use the header "30" (standard for SGTIN-96)
4. Write EPCs using SGTIN-96 format with TID-derived serials
5. Verify tags after writing
6. Lock tags (but not permalock)
7. Use GPI/GPO on the verifier for status indication

The resulting EPC for each tag will be in SGTIN-96 format containing components of the GTIN with a TID-derived serial.

#### Other Configuration Examples

1. **Full Configuration (All Readers)**
```bash
curl -X POST "http://localhost:5000/api/job" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "FullEnduranceTest",
    "strategyType": "MultiReaderEnduranceStrategy",
    "readerSettings": {
      "detector": {
        "hostname": "192.168.68.248",
        "txPowerInDbm": 18,
        "parameters": {
          "enableGpiTrigger": "false"
        }
      },
      "writer": {
        "hostname": "192.168.1.100",
        "txPowerInDbm": 33,
        "parameters": {
          "enableLock": "true",
          "enablePermalock": "false"
        }
      },
      "verifier": {
        "hostname": "192.168.68.93",
        "txPowerInDbm": 33,
        "parameters": {
          "enableGpiTrigger": "false"
        }
      }
    }
  }'
```

2. **Writer-Only Configuration**
```bash
curl -X POST "http://localhost:5000/api/job" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "WriterOnlyTest",
    "strategyType": "MultiReaderEnduranceStrategy",
    "readerSettings": {
      "writer": {
        "hostname": "192.168.1.100",
        "txPowerInDbm": 33,
        "parameters": {
          "enableLock": "true",
          "enablePermalock": "false",
          "enableGpiTrigger": "false"
        }
      }
    }
  }'
```

3. **Writer-Verifier with GPI Settings**
```bash
curl -X POST "http://localhost:5000/api/job" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "WriterVerifierGpiTest",
    "strategyType": "MultiReaderEnduranceStrategy",
    "readerSettings": {
      "writer": {
        "hostname": "192.168.1.100",
        "txPowerInDbm": 33,
        "parameters": {
          "enableLock": "true"
        }
      },
      "verifier": {
        "hostname": "192.168.68.93",
        "txPowerInDbm": 33,
        "parameters": {
          "enableGpiTrigger": "true",
          "gpiPort": "1",
          "gpiTriggerState": "true",
          "enableGpoOutput": "true",
          "gpoPort": "1",
          "gpoVerificationTimeoutMs": "1000"
        }
      }
    }
  }'
```

#### Starting a Job

Once you've created a job (and received its ID), you can start it:

```bash
curl -X POST "http://localhost:5000/api/job/job123/start" \
  -H "Content-Type: application/json" \
  -d '{
    "timeoutSeconds": 3600
  }'
```

This starts the job with a timeout of 1 hour (3600 seconds).

#### Monitoring Job Status

To check how your job is progressing:

```bash
curl -X GET "http://localhost:5000/api/job/job123"
```

Example response:
```json
{
  "status": "Running",
  "metrics": {
    "activeRoles": "Writer Verifier",
    "cycleCount": 42,
    "maxCycle": 100,
    "avgWriteTimeMs": 15.5,
    "avgVerifyTimeMs": 8.2,
    "avgLockTimeMs": 20.1,
    "lockedTags": 42,
    "gpiEventsTotal": 50,
    "gpiEventsVerified": 48,
    "gpiEventsMissingTag": 2,
    "readerMetrics": {
      "writer": {
        "readRate": 150.5,
        "successCount": 42,
        "failureCount": 0,
        "avgWriteTimeMs": 15.5,
        "lockedTags": 42
      },
      "verifier": {
        "readRate": 160.2,
        "successCount": 42,
        "failureCount": 0,
        "avgVerifyTimeMs": 8.2
      }
    }
  }
}
```

#### Detailed Metrics

To get detailed metrics for your job:

```bash
curl -X GET "http://localhost:5000/api/job/job123/metrics"
```

#### Job Logs

To view the logs for your job:

```bash
curl -X GET "http://localhost:5000/api/job/job123/logs"
```

#### Tag Data

To get detailed information about processed tags:

```bash
curl -X GET "http://localhost:5000/api/job/job123/tags?page=1&pageSize=50&sortBy=timestamp&descending=true"
```

#### Stopping a Job

When you're done, stop the job:

```bash
curl -X POST "http://localhost:5000/api/job/job123/stop"
```

### SpeedTestStrategy
Performance testing strategy for evaluating tag operation speed and reliability.

### ImpinjR700CapStrategy
Specialized strategy for Impinj R700 readers implementing the CAP (Control Application) requirements, offering REST-based tag reading and writing functionality.

## Configuration Options

### Reader Settings

Each reader can be configured with:

- Hostname/IP address
- RF mode and power settings
- Antenna port selection
- Search mode and session
- Filter settings
- Report settings (FastID, RSSI, etc.)

### Strategy Configuration

Strategies can be customized with:

- Read/write parameters
- EPC encoding options (BasicWithTidSuffix, SGTIN96)
- Lock/permalock settings
- Test duration and cycle limits
- Multi-antenna options
- GPI/GPO settings

## Extending the API

To add a new job strategy:

1. Create a new class that implements `IJobStrategy` (or extends `SingleReaderStrategyBase`/`MultiReaderStrategyBase`)
2. Add a `StrategyDescriptionAttribute` to describe the strategy's capabilities
3. Implement the required interface methods (`RunJob`, `GetStatus`, `GetMetadata`)
4. Add appropriate configuration and validation logic
5. The `StrategyFactory` will automatically discover the new strategy

## License

This project is licensed under the MIT License - see the LICENSE file for details.