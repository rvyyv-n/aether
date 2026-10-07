using System;
using System.Threading;

namespace Aether.Core.Tests.TestSupport;

public static class TestWait
{
    public static bool ForCondition(Func<bool> condition, int timeoutMs = 4000)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }
            Thread.Sleep(5);
        }
        return condition();
    }
}
