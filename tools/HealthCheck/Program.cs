using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace HealthCheck;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(2);
            var response = await client.GetAsync("http://localhost:4021/health");

            if (response.IsSuccessStatusCode)
            {
                return 0;
            }

            Console.Error.WriteLine($"Healthcheck failed with status code: {response.StatusCode}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Healthcheck failed with exception: {ex.Message}");
            return 1;
        }
    }
}
