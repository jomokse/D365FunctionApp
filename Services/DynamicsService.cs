// Services/DynamicsService.cs
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using D365FunctionApp;

namespace D365FunctionApp.Services
{
    public class DynamicsService
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;

        public DynamicsService(ILogger logger)
        {
            _logger = logger;
            _httpClient = new HttpClient();
        }

        // Load simulated data from local JSON file (Test Mode)
        public async Task<List<FetchCustomerDataFunction.Customer>> LoadSimulatedDataAsync(string customerId, string balanceDate)
        {
            try
            {
                var jsonData = await File.ReadAllTextAsync("simulated_customers.json");
                var allCustomers = JsonSerializer.Deserialize<List<FetchCustomerDataFunction.Customer>>(jsonData);

                var filteredCustomers = allCustomers?
                    .Where(c => c.Customer_ID == customerId && c.BalanceDate == balanceDate)
                    .ToList();

                return filteredCustomers ?? new List<FetchCustomerDataFunction.Customer>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load simulated customer data.");
                return new List<FetchCustomerDataFunction.Customer>();
            }
        }

        // Simulate real Dynamics 365 data fetch (Production Mode)
        public List<FetchCustomerDataFunction.Customer> SimulateDynamicsFetch(string customerId, string balanceDate)
        {
            // Build fake URL and OData query (this is just an example):
            string serverUrl = "https://demo.company.net/data/CompaniesData";
            string filters = $"?$filter=BalanceDate eq datetime'{balanceDate}' and Customer_ID eq '{customerId}'";
            string selects = "&$select=Customer_ID,Name,City,Country_Region_Code,Balance,BalanceDate";
            string format = "&$format=json";

            string requestUrl = serverUrl + filters + selects + format;

            _logger.LogInformation($"Simulated Dynamics fetch URL: {requestUrl}");

            // Fake credentials
            string username = "fromKeyVault"; // replace with real Key Vault values in production
            string password = "fromKeyVault";

            // Create HTTP request
            var request = (HttpWebRequest)WebRequest.Create(requestUrl);
            request.PreAuthenticate = true;
            request.Credentials = new NetworkCredential(username, password);

            try
            {
                using var response = (HttpWebResponse)request.GetResponse();
                using var stream = response.GetResponseStream();
                using var reader = new StreamReader(stream);

                var json = reader.ReadToEnd();

                // Deserialize response to List<Customer>
                var customers = JsonSerializer.Deserialize<List<FetchCustomerDataFunction.Customer>>(json);

                return customers ?? new List<FetchCustomerDataFunction.Customer>();
            }
            catch (WebException webEx)
            {
                _logger.LogError(webEx, "Error during Dynamics 365 fetch simulation.");
                return new List<FetchCustomerDataFunction.Customer>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during Dynamics 365 fetch simulation.");
                return new List<FetchCustomerDataFunction.Customer>();
            }
        }
    }
}
