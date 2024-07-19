using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Instrumentation.AWSLambda;
using OpenTelemetry.ResourceDetectors.AWS;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;

using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace VerifyResponse;

public class Function
{
    public static readonly TracerProvider TracerProvider;
    private static ILambdaLogger? _logger;
    private static readonly HttpClient client = new HttpClient();

    static Function()
    {
      TracerProvider = ConfigureSplunkTelemetry()!;
    }

    // Note: Do not forget to point function handler to here.
    public Task<APIGatewayProxyResponse> TracingFunctionHandler(APIGatewayProxyRequest apigProxyEvent, ILambdaContext context)
    
      {
        _logger = context.Logger;
        _logger?.LogLine($"Request received: {JsonSerializer.Serialize(apigProxyEvent)}");
        return AWSLambdaWrapper.Trace(TracerProvider, FunctionHandler, apigProxyEvent, context);
      }

    private static TracerProvider ConfigureSplunkTelemetry()
    {

      var serviceName = Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME") ?? "Unknown";
      var accessToken = Environment.GetEnvironmentVariable("SPLUNK_ACCESS_TOKEN")?.Trim();
      var realm = Environment.GetEnvironmentVariable("SPLUNK_REALM")?.Trim();
      var deploymentEnvironment = Environment.GetEnvironmentVariable("DEPLOYMENT.ENVIRONMENT") ?? "development";


      ArgumentNullException.ThrowIfNull(accessToken, "SPLUNK_ACCESS_TOKEN");
      ArgumentNullException.ThrowIfNull(realm, "SPLUNK_REALM");

      var builder = Sdk.CreateTracerProviderBuilder()
            // Use Add[instrumentation-name]Instrumentation to instrument missing services
            // Use Nuget to find different instrumentation libraries
            .AddHttpClientInstrumentation()
            .AddAWSInstrumentation()
            // Use AddSource to add your custom DiagnosticSource source names
            //.AddSource("My.Source.Name")
            .SetSampler(new AlwaysOnSampler())
            .AddAWSLambdaConfigurations(opts => opts.DisableAwsXRayContextExtraction = true)
            .ConfigureResource(configure => configure
                  .AddService(serviceName, serviceVersion: "1.0.0")
                  .AddAttributes(new KeyValuePair<string, object>[]
                    {
                        new KeyValuePair<string, object>("deployment.environment", deploymentEnvironment)
                    })
                  // Different resource detectors can be found at
                  // https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.ResourceDetectors.AWS#usage
                  .AddDetector(new AWSEBSResourceDetector()))
            .AddOtlpExporter(opts =>
            {
               opts.Endpoint = new Uri($"https://ingest.{realm}.signalfx.com/v2/trace/otlp");
               opts.Protocol = OtlpExportProtocol.HttpProtobuf;
               opts.Headers = $"X-SF-TOKEN={accessToken}";
            });
      return builder.Build()!;
    }

    private static async Task<string> GetCallingIP()
    {

        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Add("User-Agent", "AWS Lambda .Net Client");

        var msg = await client.GetStringAsync("http://checkip.amazonaws.com/").ConfigureAwait(continueOnCapturedContext:false);
        _logger?.LogLine("Finished GetCallingIP");
        return msg.Replace("\n","");
    }
   
    public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest apigProxyEvent, ILambdaContext context)
    {
        _logger?.LogLine("Received request body: " + apigProxyEvent.Body);
/* 
        var requestBody = JsonSerializer.Deserialize<Dictionary<string, string>>(apigProxyEvent.Body);
          string inputString = requestBody?.GetValueOrDefault("inputString") ?? "default value";
        if (requestBody == null || !requestBody.TryGetValue("inputString", out var inputString) || string.IsNullOrWhiteSpace(inputString))
        {
            _logger?.LogLine("inputString is required and cannot be empty");
            return new APIGatewayProxyResponse
            {
                Body = JsonSerializer.Serialize(new { message = "inputString is required and cannot be empty" }),
                StatusCode = 422,
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }
 */
        var location = await GetCallingIP();
        var body = new Dictionary<string, string>
        {
            { "message", "DATA" + " verified!" },
            { "location", location }
        };

        return new APIGatewayProxyResponse
        {
            Body = JsonSerializer.Serialize(body),
            StatusCode = 200,
            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
        };
    }
}