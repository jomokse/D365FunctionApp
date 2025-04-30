using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using D365FunctionApp.Services;

namespace D365FunctionApp
{
    public class FetchCustomerDataFunction
    {
        private readonly ILogger<FetchCustomerDataFunction> _logger;
        private readonly HttpClient _httpClient;
        private readonly SecretClient _secretClient; 
        private readonly DynamicsService _dynamicsService;       

        public FetchCustomerDataFunction(ILogger<FetchCustomerDataFunction> logger)
        {
            _logger = logger;            
            _httpClient = new HttpClient();

            // Initialize Key Vault client
            var keyVaultUrl = Environment.GetEnvironmentVariable("KEY_VAULT_URL");
            _secretClient = new SecretClient(new Uri(keyVaultUrl), new DefaultAzureCredential());
            
            // Initialize Dynamics Service
            _dynamicsService = new DynamicsService(_logger);
        }

        [Function("FetchCustomerDataFunction")]
        public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            string customerId = query["customerId"];
            string balanceDateParam = query["balanceDate"];

            var response = req.CreateResponse();
            response.Headers.Add("Content-Type", "application/json");

            // Validate input parameters
            if (string.IsNullOrEmpty(customerId) || !DateTime.TryParse(balanceDateParam, out var balanceDate))
            {
                response.StatusCode = System.Net.HttpStatusCode.BadRequest;
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    status = "error",
                    message = "Missing or invalid 'customerId' or 'balanceDate' parameter."
                }));
                return response;
            }

            try
            {
                // Simulated authentication using secrets from Key Vault
                string username = string.Empty;
                string password = string.Empty;

                // Check if running in test mode
                bool isTestMode = bool.TryParse(Environment.GetEnvironmentVariable("TEST_MODE"), out var testMode) && testMode;

                List<Customer> filteredCustomers;

                if (isTestMode)
                {
                    // Test mode: Load simulated data from external JSON file
                    string filePath = Path.Combine(AppContext.BaseDirectory, "TestData", "simulated_customers.json");
                    var jsonData = await File.ReadAllTextAsync(filePath);
                    var allCustomers = JsonSerializer.Deserialize<List<Customer>>(jsonData);

                    filteredCustomers = allCustomers
                        ?.Where(c => c.Customer_ID == customerId && c.BalanceDate == balanceDate.ToString("yyyy-MM-dd"))
                        .ToList();

                    _logger.LogInformation($"Found {filteredCustomers?.Count ?? 0} rows for customerId={customerId} on balanceDate={balanceDate:yyyy-MM-dd} (Test Mode)");
                }
                else
                {
                    // Production mode: Simulate Dynamics 365 OData connection
                    // (Here you would normally authenticate and fetch data from D365 OData API)

                    try
                    {
                        // Try to fetch secrets from Azure Key Vault
                        KeyVaultSecret usernameSecret = await _secretClient.GetSecretAsync("dynamics-username");
                        KeyVaultSecret passwordSecret = await _secretClient.GetSecretAsync("dynamics-password");
                        username = usernameSecret.Value;
                        password = passwordSecret.Value;
                        _logger.LogInformation("Fetched credentials from Key Vault.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Key Vault access failed. Falling back to environment variables.");

                        // Fallback: use environment variables
                        username = Environment.GetEnvironmentVariable("dynamics-username");
                        password = Environment.GetEnvironmentVariable("dynamics-password");

                        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                        {
                            throw new InvalidOperationException("Username and password not configured properly.");
                        }
                    }                  

                    // Production mode: Simulate Dynamics 365 fetch
                    filteredCustomers = _dynamicsService.SimulateDynamicsFetch(customerId, balanceDate.ToString("yyyy-MM-dd"));
                    _logger.LogInformation($"Simulated fetching {filteredCustomers.Count} customer(s) from Dynamics 365 for customerId={customerId} (Production Mode)");
                }

                // No data found, return informative message
                if (filteredCustomers == null || filteredCustomers.Count == 0)
                {
                    response.StatusCode = System.Net.HttpStatusCode.OK;
                    await response.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        status = "error",
                        message = $"No data found for customerId={customerId} and balanceDate={balanceDate:yyyy-MM-dd}"
                    }));
                    return response;
                }

                // Create CSV content
                var csvContent = new StringBuilder();
                csvContent.AppendLine("Customer_ID;Name;City;Country_Region_Code;Balance;BalanceDate");

                foreach (var customer in filteredCustomers)
                {
                    csvContent.AppendLine($"{customer.Customer_ID};{customer.Name};{customer.City};{customer.Country_Region_Code};{customer.Balance};{customer.BalanceDate}");
                }

                // Upload CSV content to Azure Blob Storage with retry logic
                (string blobUri, string blobName) = await UploadToBlobWithRetryAsync(csvContent.ToString());

                _logger.LogInformation($"CSV file uploaded to blob storage: {blobName}");

                // Return success response
                response.StatusCode = System.Net.HttpStatusCode.OK;
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    status = "success",
                    blobName,
                    blobUri,
                    customerId,
                    balanceDate = balanceDate.ToString("yyyy-MM-dd")
                }));
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                response.StatusCode = System.Net.HttpStatusCode.InternalServerError;
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    status = "error",
                    message = ex.Message
                }));
                return response;
            }
        }

        // Uploads content to Azure Blob Storage with retry mechanism
        private async Task<(string Uri, string Name)> UploadToBlobWithRetryAsync(string content)
        {
            string blobConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "UseDevelopmentStorage=true";
            string containerName = "customer-data";

            var blobServiceClient = new BlobServiceClient(blobConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync();

            string blobName = $"customers_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            var blobClient = containerClient.GetBlobClient(blobName);

            int maxRetries = 3;
            int delayMilliseconds = 2000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
                    await blobClient.UploadAsync(stream, overwrite: true);
                    return (blobClient.Uri.ToString(), blobName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Attempt {attempt} to upload blob failed.");
                    if (attempt == maxRetries)
                    {
                        _logger.LogError("Max retry attempts reached. Blob upload failed.");
                        throw;
                    }
                    await Task.Delay(delayMilliseconds);
                }
            }

            throw new Exception("Blob upload failed after retries.");
        }

            // Customer data model
        public class Customer
        {
            public string? Customer_ID { get; set; }
            public string? Name { get; set; }
            public string? City { get; set; }
            public string? Country_Region_Code { get; set; }
            public double Balance { get; set; }
            public string? BalanceDate { get; set; }
        }
    }
}
