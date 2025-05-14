# RFID Job Control API

A RESTful API service for managing and controlling RFID job operations. This API allows you to configure, start, monitor, and stop RFID tag reading and writing jobs remotely.

## Features

- REST API for remote job control
- Multiple job strategies for different RFID operations:
  - BatchSerializationStrategy: Batch processing of tag serialization
  - CheckBoxStrategy: Single-reader tag verification and encoding
  - MultiReaderEnduranceStrategy: Dual-reader endurance testing
  - ReadOnlyLoggingStrategy: Non-invasive tag monitoring
  - SpeedTestStrategy: Performance testing of tag operations
- Real-time job status monitoring
- Centralized configuration management
- Detailed job metrics and logs
- Swagger UI for API documentation and testing
- Support for multiple reader configurations
- Tag operation tracking and logging
- EPC list generation utilities

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

- `GET /api/job` - Get all jobs
- `GET /api/job/{jobId}` - Get a specific job
- `POST /api/job` - Create a new job
- `POST /api/job/{jobId}/start` - Start a job
- `POST /api/job/{jobId}/stop` - Stop a job
- `GET /api/job/{jobId}/metrics` - Get job metrics
- `GET /api/job/{jobId}/logs` - Get job logs
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

## Job Strategies

### BatchSerializationStrategy
Handles batch processing of tag serialization operations. Supports configurable batch sizes and serialization patterns.

### CheckBoxStrategy
Single-reader strategy for tag verification and encoding. Reads tags during a configurable period, confirms tag count, and writes new EPCs based on selected encoding method.

### MultiReaderEnduranceStrategy
Dual-reader strategy for endurance testing. Uses separate readers for reading and writing operations, supporting continuous testing scenarios.

### ReadOnlyLoggingStrategy
Non-invasive monitoring strategy that reads and logs tag information without modification. Ideal for inventory and monitoring applications.

### SpeedTestStrategy
Performance testing strategy for evaluating tag operation speed and reliability.

## Example Usage

### Creating a Job

```bash
curl -X POST "http://localhost:5000/api/job" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Job1",
    "strategyType": "ReadOnlyLoggingStrategy",
    "readerSettings": {
      "reader": {
        "hostname": "192.168.1.100",
        "txPowerInDbm": 33
      }
    },
    "parameters": {
      "epcHeader": "E7",
      "sku": "012345678901"
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

### Stopping a Job

```bash
curl -X POST "http://localhost:5000/api/job/{jobId}/stop"
```

## Architecture

The API is built with ASP.NET Core and follows a clean architecture pattern:

- **Controllers:** Handle HTTP requests and responses
- **Services:** Implement business logic and job management
- **Models:** Define data structures for API operations
- **Background Services:** Manage long-running operations
- **Job Strategies:** Implement specific RFID operation patterns
- **Storage Services:** Handle data persistence and logging
- **Utilities:** Provide helper functions and EPC generation tools

## Project Structure

- **OctaneTagJobControlAPI:** Main API implementation
- **EpcListGenerator:** Utility for generating EPC lists
- **TagUtils:** Common tag-related utilities and helpers

## Extending the API

To add a new job strategy:

1. Create a new class that implements `IJobStrategy` in the JobStrategies directory
2. Implement the required interface methods
3. Add appropriate configuration and validation logic
4. The new strategy will automatically be available through the API

## License

This project is licensed under the MIT License - see the LICENSE file for details.
