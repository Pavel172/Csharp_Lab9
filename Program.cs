using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Diagnostics;

class Program
{
    private static HttpClient client = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };
    private static string apiToken = "RWhhYmZtRy1qVUIwcUJrdnB3TjE5aXVjdnZPLVJ1WXFkS2dqR2pCQXBfdz0";
    private static string outputFile = "results.txt";
    private static SemaphoreSlim semaphore = new SemaphoreSlim(5);
    private static int successCount = 0;
    private static ConcurrentDictionary<string, double> results = new ConcurrentDictionary<string, double>();

    static async Task Main()
    {
        string[] tickers = await File.ReadAllLinesAsync("ticker.txt");
        tickers = tickers.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToArray();
        int totalTickers = tickers.Length;
        var toDate = DateTime.Now;
        var fromDate = toDate.AddMonths(-11);
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiToken}");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        var tasks = new List<Task>();
        var processedCount = 0;
        for (int i = 0; i < tickers.Length; i += 25)
        {
            var batch = tickers.Skip(i).Take(25);
            var batchTasks = batch.Select(async ticker =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await ProcessTickerAsync(ticker, fromDate, toDate);
                    var currentCount = Interlocked.Increment(ref processedCount);
                    var successRate = (double)successCount / currentCount * 100;

                }
                finally
                {
                    semaphore.Release();
                }
            });
            tasks.AddRange(batchTasks);
        }
        await Task.WhenAll(tasks);
        var sortedResults = results.OrderBy(r => r.Key)
                                 .Select(r => $"{r.Key}:{r.Value:F2}");
        await File.WriteAllLinesAsync(outputFile, sortedResults);
    }

    static async Task ProcessTickerAsync(string ticker, DateTime fromDate, DateTime toDate)
    {
        try
        {
            string url = $"https://api.marketdata.app/v1/stocks/candles/D/{ticker}/?from={fromDate:yyyy-MM-dd}&to={toDate:yyyy-MM-dd}&format=json&adjusted=true";
            var response = await client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<StockData>(response);
            double averagePrice = 0;
            int count = data.h.Length;
            for (int i = 0; i < count; i++)
            {
                averagePrice += (data.h[i] + data.l[i]) / 2;
            }
            averagePrice /= count;
            results.TryAdd(ticker, averagePrice);
            Interlocked.Increment(ref successCount);
        }
        catch { }
    }
}

class StockData
{
    public double[] o { get; set; }   // Цены открытия
    public double[] h { get; set; }   // Максимальные цены
    public double[] l { get; set; }   // Минимальные цены
    public double[] c { get; set; }   // Цены закрытия
    public long[] v { get; set; }     // Объемы торгов
}
