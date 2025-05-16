# RFID Job Control API

A RESTful API service for managing and controlling RFID job operations. This API allows you to configure, start, monitor, and stop RFID tag reading and writing jobs remotely.

## Features

- REST API for remote job control
- Multiple job strategies for different RFID operations:
  - **BatchSerializationStrategy**: Batch processing of tag serialization
  - **CheckBoxStrategy**: Single-reader tag verification and encoding
  - **MultiReaderEnduranceStrategy**: Dual-reader endurance testing with optional locking/permalocking
  - **ReadOnlyLoggingStrategy**: Non-invasive tag monitoring
  - **SpeedTestStrategy**: Performance testing of tag operations
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
Single-reader strategy for tag verification and encoding. Reads tags during a configurable period, confirms tag count, and writes new EPCs based on selected encoding method.

### MultiReaderEnduranceStrategy
Multi-reader strategy for endurance testing that can utilize up to three readers (detector, writer, and verifier). Uses separate readers for reading, writing and verifying operations, supporting continuous testing scenarios. Features:
- Optional tag locking/permalocking
- Cycle count tracking
- Performance metrics for write, verify, and lock operations
- Automatic retry on verification failure
- GPI/GPO support for tag presence detection and status indication
- Flexible reader role combinations

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

#### Example Configurations

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
Expected response:
```json
{
  "jobId": "job123",
  "status": "Created",
  "metrics": {
    "activeRoles": "Detector Writer Verifier",
    "hasDetectorRole": true,
    "hasWriterRole": true,
    "hasVerifierRole": true
  }
}
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
Expected response:
```json
{
  "jobId": "job124",
  "status": "Created",
  "metrics": {
    "activeRoles": "Writer",
    "hasDetectorRole": false,
    "hasWriterRole": true,
    "hasVerifierRole": false
  }
}
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
Expected response:
```json
{
  "jobId": "job125",
  "status": "Created",
  "metrics": {
    "activeRoles": "Writer Verifier",
    "hasDetectorRole": false,
    "hasWriterRole": true,
    "hasVerifierRole": true,
    "verifierGpiEnabled": true,
    "verifierGpiPort": "1",
    "verifierGpoEnabled": true,
    "verifierGpoPort": "1"
  }
}
```

4. **Verifier-Only with GPI Settings**
```bash
curl -X POST "http://localhost:5000/api/job" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "VerifierOnlyGpiTest",
    "strategyType": "MultiReaderEnduranceStrategy",
    "readerSettings": {
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
Expected response:
```json
{
  "jobId": "job126",
  "status": "Created",
  "metrics": {
    "activeRoles": "Verifier",
    "hasDetectorRole": false,
    "hasWriterRole": false,
    "hasVerifierRole": true,
    "verifierGpiEnabled": true,
    "verifierGpiPort": "1",
    "verifierGpoEnabled": true,
    "verifierGpoPort": "1"
  }
}
```

#### Monitoring Job Status

The job status response includes detailed metrics for each active reader role:

```bash
curl -X GET "http://localhost:5000/api/job/{jobId}"
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

#### Monitoring Job Status and Metrics

- Use `GET /api/job/{jobId}` to retrieve the current status of the job, including:
  - Current operation state
  - Success/failure counts
  - Reader roles and states
  - Current cycle count
- Use `GET /api/job/{jobId}/metrics` to get detailed metrics including:
  - Write operation performance
  - Verification success rate
  - Lock operation statistics
  - Reader-specific metrics
  - Tag operation timing data
- Use `GET /api/job/{jobId}/logs` to view job logs with detailed operation history
- Use `GET /api/job/{jobId}/tags` to get tag read/write data including:
  - TID/EPC pairs
  - Operation timestamps
  - Success/failure status
  - Reader identification

#### Stopping a Job

To stop a running job, use:

```bash
curl -X POST "http://localhost:5000/api/job/{jobId}/stop"
```

### SpeedTestStrategy
Performance testing strategy for evaluating tag operation speed and reliability.

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
- EPC encoding options
- Lock/permalock settings
- Test duration and cycle limits
- Multi-antenna options

## Example Usage

### Creating a Job

```bash
curl -X POST "http://localhost:5000/api/job" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "ReadOnlyJob",
    "strategyType": "ReadOnlyLoggingStrategy",
    "readerSettings": {
      "writer": {
        "hostname": "192.168.1.100",
        "txPowerInDbm": 30,
        "antennaPort": 1,
        "searchMode": "DualTarget"
      }
    },
    "parameters": {
      "readDurationSeconds": "60",
      "filterDuplicates": "true"
    }
  }'
```

### Creating an Endurance Test Job

```bash
curl -X POST "http://localhost:5000/api/job" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "EnduranceTest",
    "strategyType": "MultiReaderEnduranceStrategy",
    "readerSettings": {
      "detector": {
        "hostname": "192.168.68.248",
        "txPowerInDbm": 18
      },
      "writer": {
        "hostname": "192.168.1.100",
        "txPowerInDbm": 33
      },
      "verifier": {
        "hostname": "192.168.68.93",
        "txPowerInDbm": 33
      }
    },
    "parameters": {
      "epcHeader": "E7",
      "sku": "012345678901",
      "enableLock": "false",
      "enablePermalock": "false",
      "maxCycles": "10000"
    }
  }'
```

### Starting a Job

```bash
curl -X POST "http://localhost:5000/api/job/{jobId}/start" \
  -H "Content-Type: application/json" \
  -d '{
    "timeoutSeconds": 300
  }'
```

### Monitoring Job Status

```bash
curl -X GET "http://localhost:5000/api/job/{jobId}"
```

### Getting Tag Data

```bash
curl -X GET "http://localhost:5000/api/job/{jobId}/tags?page=1&pageSize=50&sortBy=timestamp&descending=true"
```

### Stopping a Job

```bash
curl -X POST "http://localhost:5000/api/job/{jobId}/stop"
```

## Extending the API

To add a new job strategy:

1. Create a new class that implements `IJobStrategy` (or extends `SingleReaderStrategyBase`/`MultiReaderStrategyBase`)
2. Add a `StrategyDescriptionAttribute` to describe the strategy's capabilities
3. Implement the required interface methods (`RunJob`, `GetStatus`, `GetMetadata`)
4. Add appropriate configuration and validation logic
5. The `StrategyFactory` will automatically discover the new strategy

## License

This project is licensed under the MIT License - see the LICENSE file for details.