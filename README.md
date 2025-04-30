# D365FunctionApp

## Overview
This Azure Function project simulates fetching customer data either from a local test dataset or from a simulated Dynamics 365 Business Central OData API. The data is transformed into CSV format and uploaded to Azure Blob Storage. It is designed to be triggered via an HTTP GET request, for example from Azure Logic Apps.

## Features
- Accepts `customerId` and `balanceDate` as input parameters.
- Supports two operation modes: Test mode (reads from local JSON) and Production simulation (fakes a Dynamics call).
- CSV generation from filtered customer data.
- Uploads the CSV to Azure Blob Storage with retry logic.
- Uses Azure Key Vault to simulate fetching credentials.

## Getting Started

### Prerequisites
- [.NET 6 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
- [Azure Functions Core Tools](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local)
- [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite) for local Blob Storage emulation

### Setup Instructions
1. Clone this repository.
2. Create a `local.settings.json` file in the root:
```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "KEY_VAULT_URL": "https://fake-vault-url.vault.azure.net/",
    "TEST_MODE": "true"
  }
}
```
3. Add the `simulated_customers.json` file in the root with sample data.
4. Start Azurite:
```
npx azurite
```
5. Run the Function locally:
```
func start
```

### Example HTTP Call
```
GET http://localhost:7071/api/FetchCustomerData?customerId=12345&balanceDate=2024-12-31
```

## Output
- On success, a CSV file is uploaded to your local Azurite Blob container (`customer-data`).
- The HTTP response includes the blob name, URI, and input metadata.

## File Structure
```
├── D365FunctionApp/
│   ├── Services/
│   │   └── DynamicsService.cs
│   ├── simulated_customers.json
│   └── FetchCustomerDataFunction.cs
├── local.settings.json
├── README.md
```

## License
This project is licensed under the [MIT License](LICENSE).

