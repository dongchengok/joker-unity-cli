using System.Net;

namespace Joker.UnityCli.Tests.Infrastructure;

public static class PortHelper
{
    public static int FindAvailablePort(int min = 63000, int max = 63100)
    {
        var random = new Random();
        for (int i = 0; i < 100; i++)
        {
            var port = random.Next(min, max);
            try
            {
                var testListener = new HttpListener();
                testListener.Prefixes.Add($"http://127.0.0.1:{port}/");
                testListener.Start();
                testListener.Stop();
                return port;
            }
            catch (HttpListenerException) { }
        }
        throw new InvalidOperationException("No available port found for test");
    }
}
