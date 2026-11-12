#if NET11_0_OR_GREATER
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestLibrary;

public static class InFlightTargets
{
	public static readonly List<string> Events = new();
	public static TaskCompletionSource<int> Gate = NewGate();

	public static TaskCompletionSource<int> NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	public static void Reset()
	{
		Events.Clear();
		Gate = NewGate();
	}

	public static async Task<int> RunAsync(int value)
	{
		Events.Add("start");
		var extra = await Gate.Task;
		Events.Add("resumed");
		return value + extra;
	}
}
#endif
