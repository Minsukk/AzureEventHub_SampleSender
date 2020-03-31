using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace EventHubProducer
{
    class Program
    {
        static int _loopCount = -1;
        static int _delay = 100;
        static int _messageSize = 25000;
        static string _connectionString;
        static string _eventhubnamespace;
        static TelemetryClient _telemetryClient;
        static ILogger<Program> _logger;
        private static void Init()
        {
            // Create the DI container.
            IServiceCollection services = new ServiceCollection();
            // Being a regular console app, there is no appsettings.json or configuration providers enabled by default.
            // Hence instrumentation key and any changes to default logging level must be specified here.
            services.AddLogging(loggingBuilder =>
            loggingBuilder
            .AddFilter<Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider>("", LogLevel.Warning)
            .AddConsole(options => options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.fff zzz]")
            );
            services.AddApplicationInsightsTelemetryWorkerService();
            // Build ServiceProvider.
            IServiceProvider serviceProvider = services.BuildServiceProvider();
            // Obtain logger instance from DI.
            _logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            // Obtain TelemetryClient instance from DI, for additional manual tracking or to flush.
            _telemetryClient = serviceProvider.GetRequiredService<TelemetryClient>();

            var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");
            if (!string.IsNullOrEmpty(connectionString))
            {
                _connectionString = connectionString;
                var parts = _connectionString.Split(';').Select(part => part.Split('=')).ToDictionary(k => k[0], v => v[1]);
                _eventhubnamespace = parts["Endpoint"];
            }
            else
            {
                throw new ArgumentException("Missing env:CONNECTION_STRING");
            }
            var loopCount = Environment.GetEnvironmentVariable("LOOP_COUNT");
            if (!string.IsNullOrEmpty(loopCount))
            {
                _loopCount = int.Parse(loopCount);
            }
            var delay = Environment.GetEnvironmentVariable("MESSAGE_DELAY");
            if (!string.IsNullOrEmpty(delay))
            {
                _delay = int.Parse(delay);
            }
            var messageSize = Environment.GetEnvironmentVariable("MESSAGE_SIZE");
            if (!string.IsNullOrEmpty(messageSize))
            {
                _messageSize = int.Parse(messageSize);
            }
        }
        static async Task Main(string[] args)
        {
            Init();
            _logger.LogInformation($"Starting message producer");
            if (_loopCount == -1)
                _logger.LogInformation("LoopCount is set to -1. Will run indefinetly");
            var producerA = new EventHubProducerClient(_connectionString, "typea");
            var producerB = new EventHubProducerClient(_connectionString, "typeb");
            var rand = new Random();
            var messageTypes = new string[] { "typeA", "typeB" };
            int typesA = 0;
            int typesB = 0;
            var messageString = new string('x', _messageSize);
            var sw = new Stopwatch();
            try
            {
                for (int i = 0; _loopCount == -1 || i < _loopCount; i++)
                {
                    var message = new EventData(Encoding.UTF8.GetBytes(messageString));
                    var type = messageTypes[rand.Next(0, messageTypes.Length)];
                    message.Properties.Add("messagetype", type);
                    var startTime = DateTime.UtcNow;
                    if (type == messageTypes[0])
                    {
                        typesA++;
                        using (EventDataBatch eventBatch = await producerA.CreateBatchAsync())
                        {
                            eventBatch.TryAdd(message);
                            sw.Restart();
                            await producerA.SendAsync(eventBatch);
                        }
                    }
                    else
                    {
                        typesB++;
                        using (EventDataBatch eventBatch = await producerB.CreateBatchAsync())
                        {
                            eventBatch.TryAdd(message);
                            sw.Restart();
                            await producerB.SendAsync(eventBatch);
                        }
                    }
                    sw.Stop();
                    var duration = sw.Elapsed;
                    _logger.LogInformation($"Sent message {i} with type {type} in {duration}");
                    var dt = new DependencyTelemetry()
                    {
                        Duration = duration,
                        Name = _eventhubnamespace,
                        Target = _eventhubnamespace,
                        Type = "SendEventAsync",
                        Data = $"MessageType={type}"
                    };
                    _telemetryClient.TrackDependency(dt);
                    await Task.Delay(Math.Max(0, _delay - (int)duration.TotalMilliseconds));
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Exception on sending data to IoT Hub. Message: {e.Message}");
                _telemetryClient.TrackException(e);
            }
            finally
            {
                _telemetryClient.Flush();
                await Task.Delay(5000);
            }
            _logger.LogInformation($"Finished message producer after {_loopCount} messages. Sent {typesA} of type A and {typesB} of type B");
        }
    }
}
