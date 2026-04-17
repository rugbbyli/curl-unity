using System;
using Xunit;

namespace CurlUnity.UnitTests.TestSupport
{
    internal static class ReproGate
    {
        private const string ReproEnvVar = "CURL_UNITY_RUN_REPROS";

        public static void RequireEnabled()
        {
            Skip.IfNot(
                string.Equals(Environment.GetEnvironmentVariable(ReproEnvVar), "1", StringComparison.Ordinal),
                $"Set {ReproEnvVar}=1 to run issue reproduction tests.");
        }
    }
}
